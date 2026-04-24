using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.Compliance.Models;
using PaymentServices.Compliance.Repositories;
using PaymentServices.Compliance.Services;
using PaymentServices.Shared.Enums;
using PaymentServices.Shared.Infrastructure;
using PaymentServices.Shared.Interfaces;
using PaymentServices.Shared.Messages;

namespace PaymentServices.Compliance.Functions;

/// <summary>
/// Service Bus Trigger — subscribed to tms-check subscription (state: TmsPending).
/// Runs TMS on source and destination in parallel.
///
/// Feature flag RUN_TMS:
///   true  → runs TMS via Alloy, advances to TmsCompleted on pass
///   false → skips TMS entirely, advances directly to TmsCompleted
///
/// On pass            → publishes TmsCompleted → Transfer
/// On compliance alert → publishes TmsComplianceAlert → EventNotification
/// On fail            → publishes TmsFailed → EventNotification
/// </summary>
public sealed class TmsFunction
{
    private readonly ITmsService _tmsService;
    private readonly ITransactionStateRepository _transactionStateRepository;
    private readonly IServiceBusPublisher _publisher;
    private readonly ComplianceSettings _settings;
    private readonly ILogger<TmsFunction> _logger;

    public TmsFunction(
        ITmsService tmsService,
        ITransactionStateRepository transactionStateRepository,
        IServiceBusPublisher publisher,
        IOptions<ComplianceSettings> settings,
        ILogger<TmsFunction> logger)
    {
        _tmsService = tmsService;
        _transactionStateRepository = transactionStateRepository;
        _publisher = publisher;
        _settings = settings.Value;
        _logger = logger;
    }

    [Function(nameof(TmsFunction))]
    public async Task RunAsync(
        [ServiceBusTrigger(
            topicName: "%app:AppSettings:SERVICE_BUS_TOPIC%",
            subscriptionName: "%app:AppSettings:SERVICE_BUS_TMS_SUBSCRIPTION%",
            Connection = "app:AppSettings:SERVICE_BUS_CONNSTRING")]
        ServiceBusReceivedMessage serviceBusMessage,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        PaymentMessage? message = null;

        try
        {
            message = ServiceBusPublisher.Deserialize(serviceBusMessage);

            _logger.LogInformation(
                "TMS started. EvolveId={EvolveId} CorrelationId={CorrelationId} RunTms={RunTms}",
                message.EvolveId, message.CorrelationId, _settings.RUN_TMS);

            // -------------------------------------------------------------------------
            // Feature flag — skip TMS if disabled
            // -------------------------------------------------------------------------
            if (!_settings.RUN_TMS)
            {
                _logger.LogInformation(
                    "TMS disabled by feature flag. EvolveId={EvolveId} advancing directly to Transfer.",
                    message.EvolveId);

                await _transactionStateRepository.UpdateStateAsync(
                    message.EvolveId,
                    TransactionState.TmsCompleted,
                    cancellationToken: cancellationToken);

                message.State = TransactionState.TmsCompleted;
                message.TransactionFlags = ["R0: TMS Skipped (Feature Flag)"];

                await _publisher.PublishAsync(message, cancellationToken);
                await messageActions.CompleteMessageAsync(serviceBusMessage, cancellationToken);
                return;
            }

            // -------------------------------------------------------------------------
            // Run TMS
            // -------------------------------------------------------------------------
            await _transactionStateRepository.UpdateStateAsync(
                message.EvolveId,
                TransactionState.TmsPending,
                cancellationToken: cancellationToken);

            var result = await _tmsService.RunAsync(message, cancellationToken);

            message.Source.TmsOutcome = result.SourceOutcome;
            message.Destination.TmsOutcome = result.DestinationOutcome;

            if (!result.Passed)
            {
                var isComplianceAlert = result.SourceOutcome == TmsOutcome.ComplianceAlert
                    || result.DestinationOutcome == TmsOutcome.ComplianceAlert;

                var terminalState = isComplianceAlert
                    ? TransactionState.TmsComplianceAlert
                    : TransactionState.TmsFailed;

                _logger.LogWarning(
                    "TMS did not pass. EvolveId={EvolveId} State={State} Reason={Reason}",
                    message.EvolveId, terminalState, result.FailureReason);

                message.State = terminalState;
                message.FailureReason = result.FailureReason;
                message.TransactionFlags = result.Flags;

                await _transactionStateRepository.UpdateStateAsync(
                    message.EvolveId,
                    terminalState,
                    tx =>
                    {
                        tx.FailureReason = result.FailureReason;
                        tx.TransactionFlags = result.Flags;
                    },
                    cancellationToken);

                await _publisher.PublishAsync(message, cancellationToken);
                await messageActions.CompleteMessageAsync(serviceBusMessage, cancellationToken);
                return;
            }

            // TMS passed — advance to Transfer
            message.State = TransactionState.TmsCompleted;
            message.TransactionFlags = result.Flags;

            await _transactionStateRepository.UpdateStateAsync(
                message.EvolveId,
                TransactionState.TmsCompleted,
                tx => tx.TransactionFlags = result.Flags,
                cancellationToken);

            await _publisher.PublishAsync(message, cancellationToken);

            _logger.LogInformation(
                "TMS passed. EvolveId={EvolveId} advancing to Transfer.",
                message.EvolveId);

            await messageActions.CompleteMessageAsync(serviceBusMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TMS exception. EvolveId={EvolveId} CorrelationId={CorrelationId}",
                message?.EvolveId ?? "unknown", message?.CorrelationId ?? "unknown");

            await messageActions.DeadLetterMessageAsync(
                serviceBusMessage,
                deadLetterReason: "UnhandledException",
                deadLetterErrorDescription: ex.Message,
                cancellationToken: cancellationToken);
        }
    }
}
