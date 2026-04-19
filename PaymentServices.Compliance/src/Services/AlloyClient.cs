using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.Compliance.Models;

namespace PaymentServices.Compliance.Services;

public interface IAlloyClient
{
    /// <summary>
    /// Runs a KYC evaluation for the given entity.
    /// Returns the Alloy KYC response with outcome summary.
    /// </summary>
    Task<AlloyKycResponse> RunKycAsync(
        AlloyKycInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a TMS evaluation for the given entity and transaction.
    /// Returns the Alloy TMS response with outcome summary.
    /// </summary>
    Task<AlloyTmsResponse> RunTmsAsync(
        AlloyTmsInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Alloy HTTP client.
/// TODO: Implement actual Alloy API calls when API details are confirmed.
///
/// Authentication:
/// TODO: Confirm auth mechanism — likely Basic Auth with workflow_token:workflow_secret
/// or API Key header. Update accordingly.
///
/// Endpoints:
/// TODO: Confirm Alloy base URL and endpoint paths for KYC and TMS evaluations.
/// </summary>
public sealed class AlloyClient : IAlloyClient
{
    private readonly HttpClient _httpClient;
    private readonly ComplianceSettings _settings;
    private readonly ILogger<AlloyClient> _logger;

    public AlloyClient(
        HttpClient httpClient,
        IOptions<ComplianceSettings> settings,
        ILogger<AlloyClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AlloyKycResponse> RunKycAsync(
        AlloyKycInput input,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Running KYC check. EntityId={EntityId} IsBusiness={IsBusiness}",
            input.EntityId, input.IsBusiness);

        // TODO: Implement actual Alloy KYC API call.
        // Steps:
        // 1. Build the request payload matching Alloy KYC API contract
        // 2. Set authentication headers (Basic Auth or API Key)
        //    e.g. var credentials = Convert.ToBase64String(
        //             Encoding.UTF8.GetBytes($"{_settings.ALLOY_KYC_WORKFLOW_TOKEN}:{_settings.ALLOY_KYC_WORKFLOW_SECRET}"));
        //         _httpClient.DefaultRequestHeaders.Authorization =
        //             new AuthenticationHeaderValue("Basic", credentials);
        // 3. POST to Alloy KYC endpoint
        //    e.g. var response = await _httpClient.PostAsync(
        //             $"{_settings.ALLOY_BASE_URL}/v1/evaluations", content, cancellationToken);
        // 4. Deserialize and return AlloyKycResponse

        // Stub — returns Approved for all entities until Alloy is integrated
        _logger.LogWarning(
            "AlloyClient.RunKycAsync is stubbed. Returning Approved. EntityId={EntityId}",
            input.EntityId);

        await Task.Delay(10, cancellationToken); // simulate async

        return new AlloyKycResponse
        {
            EntityToken = input.EntityId,
            Summary = new AlloyOutcomeSummary
            {
                Outcome = "Approved",
                Result = "success",
                Tags = ["R0: KYC Cleared (Stub)"]
            }
        };
    }

    public async Task<AlloyTmsResponse> RunTmsAsync(
        AlloyTmsInput input,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Running TMS check. EntityId={EntityId} TransactionId={TransactionId}",
            input.EntityId, input.TransactionId);

        // TODO: Implement actual Alloy TMS API call.
        // Steps:
        // 1. Build the request payload matching Alloy TMS API contract
        // 2. Set authentication headers
        //    e.g. var credentials = Convert.ToBase64String(
        //             Encoding.UTF8.GetBytes($"{_settings.ALLOY_TMS_WORKFLOW_TOKEN}:{_settings.ALLOY_TMS_WORKFLOW_SECRET}"));
        //         _httpClient.DefaultRequestHeaders.Authorization =
        //             new AuthenticationHeaderValue("Basic", credentials);
        // 3. POST to Alloy TMS endpoint
        //    e.g. var response = await _httpClient.PostAsync(
        //             $"{_settings.ALLOY_BASE_URL}/v1/evaluations", content, cancellationToken);
        // 4. Deserialize and return AlloyTmsResponse

        // Stub — returns Approved for all transactions until Alloy is integrated
        _logger.LogWarning(
            "AlloyClient.RunTmsAsync is stubbed. Returning Approved. EntityId={EntityId}",
            input.EntityId);

        await Task.Delay(10, cancellationToken); // simulate async

        return new AlloyTmsResponse
        {
            Summary = new AlloyOutcomeSummary
            {
                Outcome = "Approved",
                Tags = ["R0: TMS Cleared (Stub)"]
            }
        };
    }
}
