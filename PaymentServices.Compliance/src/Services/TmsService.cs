using Microsoft.Extensions.Logging;
using PaymentServices.Compliance.Models;
using PaymentServices.Shared.Enums;
using PaymentServices.Shared.Messages;

namespace PaymentServices.Compliance.Services;

public interface ITmsService
{
    /// <summary>
    /// Runs TMS checks for both source and destination parties in parallel.
    /// Returns true if both pass, false if either triggers a compliance alert.
    /// </summary>
    Task<TmsCheckResult> RunAsync(
        PaymentMessage message,
        CancellationToken cancellationToken = default);
}

public sealed class TmsCheckResult
{
    public bool Passed { get; init; }
    public TmsOutcome SourceOutcome { get; init; }
    public TmsOutcome DestinationOutcome { get; init; }
    public string? FailureReason { get; init; }
    public List<string> Flags { get; init; } = [];
}

public sealed class TmsService : ITmsService
{
    private readonly IAlloyClient _alloyClient;
    private readonly ILogger<TmsService> _logger;

    public TmsService(
        IAlloyClient alloyClient,
        ILogger<TmsService> logger)
    {
        _alloyClient = alloyClient;
        _logger = logger;
    }

    public async Task<TmsCheckResult> RunAsync(
        PaymentMessage message,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Running TMS checks. EvolveId={EvolveId} SourceEntityId={SourceEntityId} DestinationEntityId={DestinationEntityId}",
            message.EvolveId, message.Source.EntityId, message.Destination.EntityId);

        // Build TMS inputs — separate transaction IDs for source and destination
        var sourceInput = new AlloyTmsInput
        {
            EntityId = message.Source.EntityId ?? string.Empty,
            TransactionId = $"{message.EvolveId}_s",
            FintechId = message.FintechId,
            SourceRemoteId = message.Source.RemoteAccountId,
            DestinationRemoteId = message.Destination.RemoteAccountId
        };

        var destinationInput = new AlloyTmsInput
        {
            EntityId = message.Destination.EntityId ?? string.Empty,
            TransactionId = $"{message.EvolveId}_d",
            FintechId = message.FintechId,
            SourceRemoteId = message.Source.RemoteAccountId,
            DestinationRemoteId = message.Destination.RemoteAccountId
        };

        // Run source and destination TMS in parallel
        var sourceTask = _alloyClient.RunTmsAsync(sourceInput, cancellationToken);
        var destinationTask = _alloyClient.RunTmsAsync(destinationInput, cancellationToken);

        await Task.WhenAll(sourceTask, destinationTask);

        var sourceResponse = await sourceTask;
        var destinationResponse = await destinationTask;

        var sourceOutcome = ParseTmsOutcome(sourceResponse.Summary.Outcome);
        var destinationOutcome = ParseTmsOutcome(destinationResponse.Summary.Outcome);

        _logger.LogInformation(
            "TMS results. EvolveId={EvolveId} SourceOutcome={SourceOutcome} DestinationOutcome={DestinationOutcome}",
            message.EvolveId, sourceOutcome, destinationOutcome);

        // Compliance alert — hard stop, no transfer
        if (sourceOutcome == TmsOutcome.ComplianceAlert || destinationOutcome == TmsOutcome.ComplianceAlert)
        {
            var party = sourceOutcome == TmsOutcome.ComplianceAlert ? "Source" : "Destination";
            return new TmsCheckResult
            {
                Passed = false,
                SourceOutcome = sourceOutcome,
                DestinationOutcome = destinationOutcome,
                FailureReason = $"{party} TMS compliance alert.",
                Flags = [.. sourceResponse.Summary.Tags, .. destinationResponse.Summary.Tags]
            };
        }

        // Denied
        if (sourceOutcome == TmsOutcome.Denied || destinationOutcome == TmsOutcome.Denied)
        {
            var party = sourceOutcome == TmsOutcome.Denied ? "Source" : "Destination";
            return new TmsCheckResult
            {
                Passed = false,
                SourceOutcome = sourceOutcome,
                DestinationOutcome = destinationOutcome,
                FailureReason = $"{party} TMS check denied.",
                Flags = [.. sourceResponse.Summary.Tags, .. destinationResponse.Summary.Tags]
            };
        }

        // Both approved
        return new TmsCheckResult
        {
            Passed = true,
            SourceOutcome = sourceOutcome,
            DestinationOutcome = destinationOutcome,
            Flags = [.. sourceResponse.Summary.Tags, .. destinationResponse.Summary.Tags]
        };
    }

    private static TmsOutcome ParseTmsOutcome(string outcome) => outcome switch
    {
        "Approved" => TmsOutcome.Approved,
        "Compliance Alert" => TmsOutcome.ComplianceAlert,
        "Denied" => TmsOutcome.Denied,
        _ => TmsOutcome.Unknown
    };
}
