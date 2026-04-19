using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PaymentServices.Compliance.Repositories;
using PaymentServices.Compliance.Services;
using PaymentServices.Shared.Enums;
using PaymentServices.Shared.Infrastructure;
using PaymentServices.Shared.Interfaces;
using PaymentServices.Shared.Messages;

namespace PaymentServices.Compliance.Functions;

/// <summary>
/// Service Bus Trigger — subscribed to kyc-check subscription (state: KycPending).
/// Runs KYC on source and destination in parallel.
/// On pass  → publishes TmsPending.
/// On fail  → publishes KycFailed or KycManualReview → EventNotification.
/// </summary>
public sealed class KycFunction
{
    private readonly IKycService _kycService;
    private readonly ITransactionStateRepository _transactionStateRepository;
    private readonly IServiceBusPublisher _publisher;
    private readonly ILogger<KycFunction> _logger;

    public KycFunction(
        IKycService kycService,
        ITransactionStateRepository transactionStateRepository,
        IServiceBusPublisher publisher,
        ILogger<KycFunction> logger)
    {
        _kycService = kycService;
        _transactionStateRepository = transactionStateRepository;
        _publisher = publisher;
        _logger = logger;
    }

    [Function(nameof(KycFunction))]
    public async Task RunAsync(
        [ServiceBusTrigger(
            topicName: "%app:AppSettings:SERVICE_BUS_TOPIC%",
            subscriptionName: "%app:AppSettings:SERVICE_BUS_KYC_SUBSCRIPTION%",
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
                "KYC started. EvolveId={EvolveId} CorrelationId={CorrelationId}",
                message.EvolveId, message.CorrelationId);

            // Update state to KycPending in Cosmos
            await _transactionStateRepository.UpdateStateAsync(
                message.EvolveId,
                TransactionState.KycPending,
                cancellationToken: cancellationToken);

            // Run KYC for source and destination in parallel
            var result = await _kycService.RunAsync(message, cancellationToken);

            // Update party outcomes on message
            message.Source.KycOutcome = result.SourceOutcome;
            message.Destination.KycOutcome = result.DestinationOutcome;

            if (!result.Passed)
            {
                // Determine terminal state
                var isManualReview = result.SourceOutcome == KycOutcome.ManualReview
                    || result.DestinationOutcome == KycOutcome.ManualReview;

                var terminalState = isManualReview
                    ? TransactionState.KycManualReview
                    : TransactionState.KycFailed;

                _logger.LogWarning(
                    "KYC did not pass. EvolveId={EvolveId} State={State} Reason={Reason}",
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

                // Publish to EventNotification
                await _publisher.PublishAsync(message, cancellationToken);
                await messageActions.CompleteMessageAsync(serviceBusMessage, cancellationToken);
                return;
            }

            // KYC passed — advance to TMS
            message.State = TransactionState.TmsPending;
            message.TransactionFlags = result.Flags;

            await _transactionStateRepository.UpdateStateAsync(
                message.EvolveId,
                TransactionState.KycCompleted,
                tx => tx.TransactionFlags = result.Flags,
                cancellationToken);

            await _publisher.PublishAsync(message, cancellationToken);

            _logger.LogInformation(
                "KYC passed. EvolveId={EvolveId} advancing to TMS.",
                message.EvolveId);

            await messageActions.CompleteMessageAsync(serviceBusMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "KYC exception. EvolveId={EvolveId} CorrelationId={CorrelationId}",
                message?.EvolveId ?? "unknown", message?.CorrelationId ?? "unknown");

            await messageActions.DeadLetterMessageAsync(
                serviceBusMessage,
                deadLetterReason: "UnhandledException",
                deadLetterErrorDescription: ex.Message,
                cancellationToken: cancellationToken);
        }
    }
}
