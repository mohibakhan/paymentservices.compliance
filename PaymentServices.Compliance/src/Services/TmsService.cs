using Microsoft.Extensions.Logging;
using PaymentServices.Compliance.Models;
using PaymentServices.Shared.Enums;
using PaymentServices.Shared.Messages;

namespace PaymentServices.Compliance.Services;

public interface ITmsService
{
    /// <summary>
    /// Sends a transaction event to Alloy TMS for the source party.
    /// Destination counterparty details are included in the same event.
    /// Returns the TMS check result — note outcome may be async via webhook.
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
            "Running TMS. EvolveId={EvolveId} SourceEntityId={SourceEntityId} DestinationEntityId={DestinationEntityId}",
            message.EvolveId, message.Source.EntityId, message.Destination.EntityId);

        var amount = decimal.Parse(message.Amount);

        // Single transaction event — source entity sends, destination is counterparty
        // Alloy requires external_account_id which maps to our account.id
        var tmsInput = new AlloyTmsInput
        {
            EntityId = message.Source.EntityId ?? string.Empty,
            AccountId = message.Source.AccountId ?? string.Empty,
            TransactionId = message.EvolveId,
            Amount = amount,
            CounterpartyEntityId = message.Destination.EntityId,
            CounterpartyAccountId = message.Destination.AccountId,
            CounterpartyNameFirst = message.Destination.Name?.First,
            CounterpartyNameLast = message.Destination.Name?.Last,
            CounterpartyAccountNumber = message.Destination.AccountNumber
        };

        AlloyTmsResponse response;
        try
        {
            response = await _alloyClient.RunTmsAsync(tmsInput, cancellationToken);
        }
        catch (Exception ex)
        {
            // TMS errors default to Approved per original Node fallback behavior
            _logger.LogWarning(ex,
                "TMS call failed — defaulting to Approved. EvolveId={EvolveId}",
                message.EvolveId);

            return new TmsCheckResult
            {
                Passed = true,
                SourceOutcome = TmsOutcome.Approved,
                DestinationOutcome = TmsOutcome.Approved,
                Flags = ["R0: TMS Cleared (Fallback — Exception)"]
            };
        }

        var outcome = ParseTmsOutcome(response.Summary.Outcome);

        _logger.LogInformation(
            "TMS result. EvolveId={EvolveId} Outcome={Outcome} Tags={Tags}",
            message.EvolveId, outcome, string.Join(", ", response.Summary.Tags));

        if (outcome == TmsOutcome.ComplianceAlert)
        {
            return new TmsCheckResult
            {
                Passed = false,
                SourceOutcome = TmsOutcome.ComplianceAlert,
                DestinationOutcome = TmsOutcome.Approved,
                FailureReason = "TMS compliance alert.",
                Flags = response.Summary.Tags
            };
        }

        if (outcome == TmsOutcome.Denied)
        {
            return new TmsCheckResult
            {
                Passed = false,
                SourceOutcome = TmsOutcome.Denied,
                DestinationOutcome = TmsOutcome.Approved,
                FailureReason = "TMS check denied.",
                Flags = response.Summary.Tags
            };
        }

        return new TmsCheckResult
        {
            Passed = true,
            SourceOutcome = TmsOutcome.Approved,
            DestinationOutcome = TmsOutcome.Approved,
            Flags = response.Summary.Tags
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
