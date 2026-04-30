using PaymentServices.Shared.Models;

namespace PaymentServices.Compliance.Models;

/// <summary>
/// Compliance-specific settings bound from <c>app:AppSettings</c>.
/// </summary>
public sealed class ComplianceSettings : AppSettings
{
    // -------------------------------------------------------------------------
    // Alloy API
    // -------------------------------------------------------------------------

    /// <summary>Alloy base URL.</summary>
    public string ALLOY_BASE_URL { get; set; } = "https://sandbox.alloy.co";

    /// <summary>Set to true in DEV/QA to send alloy-sandbox header.</summary>
    public bool ALLOY_SANDBOX { get; set; } = true;

    /// <summary>Alloy API token — used as username for Events API Basic Auth.</summary>
    public string ALLOY_API_TOKEN { get; set; } = string.Empty;

    /// <summary>Alloy API secret — used as password for Events API Basic Auth.</summary>
    public string ALLOY_API_SECRET { get; set; } = string.Empty;

    /// <summary>Individual KYC journey token — used as username for KYC Basic Auth.</summary>
    public string ALLOY_INDIVIDUAL_KYC_JOURNEY_TOKEN { get; set; } = string.Empty;

    /// <summary>Business KYC journey token.</summary>
    public string ALLOY_BUSINESS_KYC_JOURNEY_TOKEN { get; set; } = string.Empty;

    /// <summary>KYC workflow secret — used as password for KYC Basic Auth.</summary>
    public string ALLOY_KYC_WORKFLOW_SECRET { get; set; } = string.Empty;

    /// <summary>RTP TMS journey token (for future use).</summary>
    public string ALLOY_TMS_JOURNEY_TOKEN { get; set; } = string.Empty;

    // -------------------------------------------------------------------------
    // Cosmos
    // -------------------------------------------------------------------------
    public string COSMOS_TRANSACTIONS_CONTAINER { get; set; } = "tchSendTransactions";
    public string COSMOS_CUSTOMERS_CONTAINER { get; set; } = "customers";

    // -------------------------------------------------------------------------
    // Service Bus subscriptions
    // -------------------------------------------------------------------------
    public string SERVICE_BUS_KYC_SUBSCRIPTION { get; set; } = "kyc-check";
    public string SERVICE_BUS_TMS_SUBSCRIPTION { get; set; } = "tms-check";

    // -------------------------------------------------------------------------
    // Redis cache
    // -------------------------------------------------------------------------

    /// <summary>Azure Cache for Redis connection string — same instance as AccountResolution.</summary>
    public string REDIS_CONNSTRING { get; set; } = string.Empty;

    /// <summary>
    /// KYC result cache TTL in days. Default: 31 (matches original Node 31-day window).
    /// Only Approved results are cached — Manual Review and Denied always re-check Alloy.
    /// </summary>
    public int KYC_CACHE_TTL_DAYS { get; set; } = 31;

    // -------------------------------------------------------------------------
    // Feature flags — toggle compliance checks independently
    // Configurable via Azure App Configuration without redeployment
    // -------------------------------------------------------------------------

    /// <summary>
    /// When true, runs KYC check for source and destination.
    /// When false, skips KYC entirely and advances directly to TmsPending.
    /// Default: true
    /// </summary>
    public bool RUN_KYC { get; set; } = true;

    /// <summary>
    /// When true, runs TMS screening for source and destination.
    /// When false, skips TMS and advances directly to TransferPending.
    /// Default: true
    /// </summary>
    public bool RUN_TMS { get; set; } = true;
}
