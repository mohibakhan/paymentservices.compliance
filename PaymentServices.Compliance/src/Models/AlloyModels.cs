using System.Text.Json.Serialization;
using PaymentServices.Shared.Enums;

namespace PaymentServices.Compliance.Models;

// ---------------------------------------------------------------------------
// KYC Journey Application — POST /v1/journeys/{journeyToken}/applications
// ---------------------------------------------------------------------------

/// <summary>
/// Request payload for Alloy KYC journey application.
/// Individual: branch_name = "persons", name_first, name_last, addresses
/// Business: branch_name = "businesses", business_name
/// </summary>
public sealed class AlloyKycRequest
{
    [JsonPropertyName("entities")]
    public required List<AlloyKycEntity> Entities { get; init; }
}

public sealed class AlloyKycEntity
{
    [JsonPropertyName("branch_name")]
    public string BranchName { get; init; } = "persons";

    [JsonPropertyName("name_first")]
    public string? NameFirst { get; init; }

    [JsonPropertyName("name_last")]
    public string? NameLast { get; init; }

    [JsonPropertyName("business_name")]
    public string? BusinessName { get; init; }

    [JsonPropertyName("addresses")]
    public List<AlloyAddress>? Addresses { get; init; }

    [JsonPropertyName("identifiers")]
    public required AlloyIdentifiers Identifiers { get; init; }

    [JsonPropertyName("meta")]
    public object Meta { get; init; } = new { };
}

public sealed class AlloyAddress
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "primary";

    [JsonPropertyName("line_1")]
    public string? Line1 { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; init; }

    [JsonPropertyName("country_code")]
    public string CountryCode { get; init; } = "US";
}

public sealed class AlloyIdentifiers
{
    [JsonPropertyName("external_entity_id")]
    public required string ExternalEntityId { get; init; }
}

// ---------------------------------------------------------------------------
// KYC Response — journey_application_status + complete_outcome
// ---------------------------------------------------------------------------

public sealed class AlloyKycResponse
{
    [JsonPropertyName("journey_application_token")]
    public string? JourneyApplicationToken { get; set; }

    [JsonPropertyName("complete_outcome")]
    public string? CompleteOutcome { get; set; }

    [JsonPropertyName("journey_application_status")]
    public string? JourneyApplicationStatus { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("_embedded")]
    public AlloyKycEmbedded? Embedded { get; set; }

    /// <summary>
    /// Convenience — returns the outcome from complete_outcome or journey_application_status.
    /// </summary>
    public string Outcome => CompleteOutcome
        ?? JourneyApplicationStatus
        ?? "Denied";

    /// <summary>
    /// Convenience — returns tags from the first entity application output.
    /// </summary>
    public List<string> Tags => Embedded?.EntityApplications
        ?.FirstOrDefault()?.Output?.Tags ?? [];
}

public sealed class AlloyKycEmbedded
{
    [JsonPropertyName("entity_applications")]
    public List<AlloyEntityApplication>? EntityApplications { get; set; }

    [JsonPropertyName("child_entities")]
    public List<AlloyChildEntity>? ChildEntities { get; set; }
}

public sealed class AlloyEntityApplication
{
    [JsonPropertyName("entity_token")]
    public string? EntityToken { get; set; }

    [JsonPropertyName("entity_application_outcome")]
    public string? EntityApplicationOutcome { get; set; }

    [JsonPropertyName("output")]
    public AlloyEntityApplicationOutput? Output { get; set; }
}

public sealed class AlloyEntityApplicationOutput
{
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("outcome_reasons")]
    public List<AlloyOutcomeReason>? OutcomeReasons { get; set; }
}

public sealed class AlloyOutcomeReason
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class AlloyChildEntity
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("external_entity_id")]
    public string? ExternalEntityId { get; set; }
}

// ---------------------------------------------------------------------------
// TMS Event — POST /v1/events (event_type: "transaction")
// ---------------------------------------------------------------------------

public sealed class AlloyTmsRequest
{
    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = "transaction";

    [JsonPropertyName("data")]
    public required AlloyTmsData Data { get; init; }
}

public sealed class AlloyTmsData
{
    [JsonPropertyName("external_transaction_id")]
    public required string ExternalTransactionId { get; init; }

    [JsonPropertyName("external_account_id")]
    public required string ExternalAccountId { get; init; }

    [JsonPropertyName("external_entity_id")]
    public required string ExternalEntityId { get; init; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("is_approved")]
    public bool IsApproved { get; init; } = true;

    [JsonPropertyName("transaction_created_date")]
    public string TransactionCreatedDate { get; init; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("transaction_settled_date")]
    public string? TransactionSettledDate { get; init; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("process_method")]
    public string ProcessMethod { get; init; } = "RTP";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "Withdrawal";

    [JsonPropertyName("interaction_point")]
    public string InteractionPoint { get; init; } = "Online";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "Outgoing RTP Send";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "Completed";

    [JsonPropertyName("status_detail")]
    public string StatusDetail { get; init; } = "Completed - Posted";

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "USD";

    [JsonPropertyName("is_triggered_by_user")]
    public bool IsTriggeredByUser { get; init; } = true;

    [JsonPropertyName("type_code")]
    public string TypeCode { get; init; } = "RTP01";

    [JsonPropertyName("type_description")]
    public string TypeDescription { get; init; } = "RTP Send";

    [JsonPropertyName("counterparty")]
    public required AlloyTmsCounterparty Counterparty { get; init; }

    [JsonPropertyName("supplemental_data")]
    public object SupplementalData { get; init; } = new { };

    [JsonPropertyName("meta")]
    public object Meta { get; init; } = new { };
}

public sealed class AlloyTmsCounterparty
{
    [JsonPropertyName("external_counterparty_id")]
    public required string ExternalCounterpartyId { get; init; }

    [JsonPropertyName("account_type")]
    public string AccountType { get; init; } = "individual account";

    [JsonPropertyName("account_name")]
    public string? AccountName { get; init; }

    [JsonPropertyName("account_number")]
    public string? AccountNumber { get; init; }

    [JsonPropertyName("country_code")]
    public string CountryCode { get; init; } = "US";
}

// ---------------------------------------------------------------------------
// Bank Account Created Event — POST /v1/events (event_type: "bank_account_created")
// ---------------------------------------------------------------------------

public sealed class AlloyBankAccountCreatedRequest
{
    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = "bank_account_created";

    [JsonPropertyName("data")]
    public required AlloyBankAccountData Data { get; init; }
}

public sealed class AlloyBankAccountData
{
    [JsonPropertyName("external_entity_id")]
    public required string ExternalEntityId { get; init; }

    [JsonPropertyName("external_account_id")]
    public required string ExternalAccountId { get; init; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("account_class")]
    public string AccountClass { get; init; } = "deposit";

    [JsonPropertyName("account_name")]
    public string AccountName { get; init; } = "Evolve Account";

    [JsonPropertyName("account_number")]
    public required string AccountNumber { get; init; }

    [JsonPropertyName("routing_number")]
    public required string RoutingNumber { get; init; }

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "USD";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "Active";

    [JsonPropertyName("account_balance")]
    public decimal AccountBalance { get; init; } = 0;

    [JsonPropertyName("entities")]
    public required List<AlloyBankAccountEntity> Entities { get; init; }

    [JsonPropertyName("meta")]
    public object Meta { get; init; } = new { };

    [JsonPropertyName("supplemental_data")]
    public object SupplementalData { get; init; } = new { processor_token = "" };
}

public sealed class AlloyBankAccountEntity
{
    [JsonPropertyName("account_holder_type")]
    public string AccountHolderType { get; init; } = "primary";

    [JsonPropertyName("external_entity_id")]
    public required string ExternalEntityId { get; init; }
}

// ---------------------------------------------------------------------------
// TMS Response
// ---------------------------------------------------------------------------

public sealed class AlloyTmsResponse
{
    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; }

    [JsonPropertyName("event_request_token")]
    public string? EventRequestToken { get; set; }

    [JsonPropertyName("event")]
    public AlloyTmsEventResult? Event { get; set; }

    [JsonPropertyName("error")]
    public object? Error { get; set; }

    /// <summary>
    /// Outcome summary — populated from evaluations if available,
    /// otherwise defaults to Approved (TMS is async via webhook).
    /// </summary>
    public AlloyOutcomeSummary Summary { get; set; } = new();
}

public sealed class AlloyTmsEventResult
{
    [JsonPropertyName("event_type")]
    public string? EventType { get; set; }

    [JsonPropertyName("event_status")]
    public string? EventStatus { get; set; }

    [JsonPropertyName("evaluations")]
    public List<object> Evaluations { get; set; } = [];

    [JsonPropertyName("journey_applications")]
    public List<object> JourneyApplications { get; set; } = [];
}

// ---------------------------------------------------------------------------
// Shared outcome summary
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
// Input models used by KycService / TmsService
// ---------------------------------------------------------------------------

public sealed class AlloyKycInput
{
    public required string EntityId { get; init; }
    public string? NameFirst { get; init; }
    public string? NameLast { get; init; }
    public string? BusinessName { get; init; }
    public bool IsBusiness { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressCity { get; init; }
    public string? AddressState { get; init; }
    public string? AddressPostalCode { get; init; }
    public string? AddressCountryCode { get; init; } = "US";
}

public sealed class AlloyTmsInput
{
    public required string EntityId { get; init; }
    public required string AccountId { get; init; }
    public required string TransactionId { get; init; }
    public required decimal Amount { get; init; }
    public string? CounterpartyEntityId { get; init; }
    public string? CounterpartyAccountId { get; init; }
    public string? CounterpartyNameFirst { get; init; }
    public string? CounterpartyNameLast { get; init; }
    public string? CounterpartyAccountNumber { get; init; }
}

// ---------------------------------------------------------------------------
// Compliance check result
// ---------------------------------------------------------------------------

public sealed class ComplianceCheckResult
{
    public required string EntityId { get; init; }
    public required KycOutcome KycOutcome { get; init; }
    public required TmsOutcome TmsOutcome { get; init; }
    public List<string> Flags { get; init; } = [];
    public bool Passed { get; init; }
    public string? FailureReason { get; init; }
}
