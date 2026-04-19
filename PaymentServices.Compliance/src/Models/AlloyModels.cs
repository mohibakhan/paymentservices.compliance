using System.Text.Json.Serialization;
using PaymentServices.Shared.Enums;

namespace PaymentServices.Compliance.Models;

// ---------------------------------------------------------------------------
// Alloy KYC
// ---------------------------------------------------------------------------

/// <summary>
/// Input for an Alloy KYC evaluation.
/// TODO: Update fields to match actual Alloy KYC API contract.
/// </summary>
public sealed class AlloyKycInput
{
    public required string EntityId { get; init; }
    public string? AddressCountryCode { get; init; }
    public string? BusinessFederalEin { get; init; }
    public string? BusinessAddressCountryCode { get; init; }
    public bool IsBusiness { get; init; }
}

/// <summary>
/// Alloy KYC evaluation response.
/// TODO: Update to match actual Alloy API response structure.
/// </summary>
public sealed class AlloyKycResponse
{
    [JsonPropertyName("entity_token")]
    public string? EntityToken { get; set; }

    [JsonPropertyName("summary")]
    public AlloyOutcomeSummary Summary { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Alloy TMS
// ---------------------------------------------------------------------------

/// <summary>
/// Input for an Alloy TMS evaluation.
/// TODO: Update fields to match actual Alloy TMS API contract.
/// </summary>
public sealed class AlloyTmsInput
{
    public required string EntityId { get; init; }
    public required string TransactionId { get; init; }
    public required string FintechId { get; init; }
    public string? SourceRemoteId { get; init; }
    public string? DestinationRemoteId { get; init; }
}

/// <summary>
/// Alloy TMS evaluation response.
/// TODO: Update to match actual Alloy API response structure.
/// </summary>
public sealed class AlloyTmsResponse
{
    [JsonPropertyName("summary")]
    public AlloyOutcomeSummary Summary { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Shared outcome
// ---------------------------------------------------------------------------

public sealed class AlloyOutcomeSummary
{
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

// ---------------------------------------------------------------------------
// Compliance check result
// ---------------------------------------------------------------------------

/// <summary>
/// Result of a single KYC or TMS check for one party.
/// </summary>
public sealed class ComplianceCheckResult
{
    public required string EntityId { get; init; }
    public required KycOutcome KycOutcome { get; init; }
    public required TmsOutcome TmsOutcome { get; init; }
    public List<string> Flags { get; init; } = [];
    public bool Passed { get; init; }
    public string? FailureReason { get; init; }
}
