using Microsoft.Extensions.Logging;
using PaymentServices.Compliance.Models;
using PaymentServices.Shared.Enums;
using PaymentServices.Shared.Messages;

namespace PaymentServices.Compliance.Services;

public interface IKycService
{
    /// <summary>
    /// Runs KYC for both source and destination parties in parallel.
    /// Returns true if both pass, false if either fails or is flagged for review.
    /// </summary>
    Task<KycCheckResult> RunAsync(
        PaymentMessage message,
        CancellationToken cancellationToken = default);
}

public sealed class KycCheckResult
{
    public bool Passed { get; init; }
    public KycOutcome SourceOutcome { get; init; }
    public KycOutcome DestinationOutcome { get; init; }
    public string? FailureReason { get; init; }
    public List<string> Flags { get; init; } = [];
}

public sealed class KycService : IKycService
{
    private readonly IAlloyClient _alloyClient;
    private readonly ILogger<KycService> _logger;

    public KycService(
        IAlloyClient alloyClient,
        ILogger<KycService> logger)
    {
        _alloyClient = alloyClient;
        _logger = logger;
    }

    public async Task<KycCheckResult> RunAsync(
        PaymentMessage message,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Running KYC checks. EvolveId={EvolveId} SourceEntityId={SourceEntityId} DestinationEntityId={DestinationEntityId}",
            message.EvolveId, message.Source.EntityId, message.Destination.EntityId);

        // Build KYC inputs for source and destination
        var sourceInput = BuildKycInput(message.Source, message.TaxId);
        var destinationInput = BuildKycInput(message.Destination, message.TaxId);

        // Run source and destination KYC in parallel
        var sourceTask = _alloyClient.RunKycAsync(sourceInput, cancellationToken);
        var destinationTask = _alloyClient.RunKycAsync(destinationInput, cancellationToken);

        await Task.WhenAll(sourceTask, destinationTask);

        var sourceResponse = await sourceTask;
        var destinationResponse = await destinationTask;

        var sourceOutcome = ParseKycOutcome(sourceResponse.Summary.Outcome);
        var destinationOutcome = ParseKycOutcome(destinationResponse.Summary.Outcome);

        _logger.LogInformation(
            "KYC results. EvolveId={EvolveId} SourceOutcome={SourceOutcome} DestinationOutcome={DestinationOutcome}",
            message.EvolveId, sourceOutcome, destinationOutcome);

        // Manual Review — suspend and notify
        if (sourceOutcome == KycOutcome.ManualReview || destinationOutcome == KycOutcome.ManualReview)
        {
            var party = sourceOutcome == KycOutcome.ManualReview ? "Source" : "Destination";
            return new KycCheckResult
            {
                Passed = false,
                SourceOutcome = sourceOutcome,
                DestinationOutcome = destinationOutcome,
                FailureReason = $"{party} KYC flagged for manual review.",
                Flags = [.. sourceResponse.Summary.Tags, .. destinationResponse.Summary.Tags]
            };
        }

        // Denied — hard fail
        if (sourceOutcome == KycOutcome.Denied || destinationOutcome == KycOutcome.Denied)
        {
            var party = sourceOutcome == KycOutcome.Denied ? "Source" : "Destination";
            return new KycCheckResult
            {
                Passed = false,
                SourceOutcome = sourceOutcome,
                DestinationOutcome = destinationOutcome,
                FailureReason = $"{party} KYC failed.",
                Flags = [.. sourceResponse.Summary.Tags, .. destinationResponse.Summary.Tags]
            };
        }

        // Both approved
        return new KycCheckResult
        {
            Passed = true,
            SourceOutcome = sourceOutcome,
            DestinationOutcome = destinationOutcome,
            Flags = [.. sourceResponse.Summary.Tags, .. destinationResponse.Summary.Tags]
        };
    }

    private static AlloyKycInput BuildKycInput(PaymentParty party, string taxId)
    {
        var isBusiness = party.Name.IsBusiness;
        return new AlloyKycInput
        {
            EntityId = party.EntityId ?? string.Empty,
            IsBusiness = isBusiness,
            AddressCountryCode = isBusiness ? null : "US",
            BusinessAddressCountryCode = isBusiness ? "US" : null,
            BusinessFederalEin = isBusiness ? taxId : null
        };
    }

    private static KycOutcome ParseKycOutcome(string outcome) => outcome switch
    {
        "Approved" => KycOutcome.Approved,
        "Manual Review" => KycOutcome.ManualReview,
        "Denied" => KycOutcome.Denied,
        _ => KycOutcome.Unknown
    };
}
