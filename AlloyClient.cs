using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.Compliance.Models;

namespace PaymentServices.Compliance.Services;

public interface IAlloyClient
{
    /// <summary>
    /// Runs KYC via Alloy Journey Applications API.
    /// POST /v1/journeys/{journeyToken}/applications?fullData=true
    /// </summary>
    Task<AlloyKycResponse> RunKycAsync(
        AlloyKycInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a transaction event to Alloy for TMS screening.
    /// POST /v1/events (event_type: "transaction")
    /// Note: TMS outcome may be asynchronous via webhook.
    /// </summary>
    Task<AlloyTmsResponse> RunTmsAsync(
        AlloyTmsInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies Alloy of a new bank account (TMS account onboard).
    /// POST /v1/events (event_type: "bank_account_created")
    /// Called when a new customer account is onboarded.
    /// </summary>
    Task NotifyBankAccountCreatedAsync(
        AlloyBankAccountCreatedRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AlloyClient : IAlloyClient
{
    private readonly HttpClient _httpClient;
    private readonly ComplianceSettings _settings;
    private readonly ILogger<AlloyClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AlloyClient(
        HttpClient httpClient,
        IOptions<ComplianceSettings> settings,
        ILogger<AlloyClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // KYC — POST /v1/journeys/{journeyToken}/applications
    // -------------------------------------------------------------------------

    public async Task<AlloyKycResponse> RunKycAsync(
        AlloyKycInput input,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Running KYC. EntityId={EntityId} IsBusiness={IsBusiness}",
            input.EntityId, input.IsBusiness);

        var journeyToken = input.IsBusiness
            ? _settings.ALLOY_BUSINESS_KYC_JOURNEY_TOKEN
            : _settings.ALLOY_INDIVIDUAL_KYC_JOURNEY_TOKEN;

        var url = $"{_settings.ALLOY_BASE_URL}/v1/journeys/{journeyToken}/applications?fullData=true";

        var entity = new AlloyKycEntity
        {
            BranchName = input.IsBusiness ? "businesses" : "persons",
            NameFirst = input.IsBusiness ? null : input.NameFirst,
            NameLast = input.IsBusiness ? null : input.NameLast,
            BusinessName = input.IsBusiness ? input.BusinessName : null,
            Identifiers = new AlloyIdentifiers
            {
                ExternalEntityId = input.EntityId
            },
            Addresses = BuildAddress(input)
        };

        var requestBody = new AlloyKycRequest
        {
            Entities = [entity]
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        SetKycAuth(request);
        SetSandboxHeader(request);
        request.Content = BuildJsonContent(requestBody);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Alloy KYC failed. EntityId={EntityId} StatusCode={StatusCode} Body={Body}",
                input.EntityId, (int)response.StatusCode, responseBody);

            // Return Denied on API error — do not swallow
            return new AlloyKycResponse
            {
                CompleteOutcome = "Denied",
                JourneyApplicationStatus = "Denied"
            };
        }

        var result = JsonSerializer.Deserialize<AlloyKycResponse>(responseBody, _jsonOptions);

        _logger.LogInformation(
            "Alloy KYC complete. EntityId={EntityId} Outcome={Outcome} Tags={Tags}",
            input.EntityId, result?.Outcome, string.Join(", ", result?.Tags ?? []));

        return result ?? new AlloyKycResponse { CompleteOutcome = "Denied" };
    }

    // -------------------------------------------------------------------------
    // TMS — POST /v1/events (event_type: "transaction")
    // -------------------------------------------------------------------------

    public async Task<AlloyTmsResponse> RunTmsAsync(
        AlloyTmsInput input,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Running TMS. EntityId={EntityId} TransactionId={TransactionId} Amount={Amount}",
            input.EntityId, input.TransactionId, input.Amount);

        var url = $"{_settings.ALLOY_BASE_URL}/v1/events";

        // Build counterparty account name from available fields
        var counterpartyName = string.IsNullOrWhiteSpace(input.CounterpartyNameFirst)
            ? input.CounterpartyAccountNumber ?? "Unknown"
            : $"{input.CounterpartyNameFirst} {input.CounterpartyNameLast}".Trim();

        var requestBody = new AlloyTmsRequest
        {
            Data = new AlloyTmsData
            {
                ExternalTransactionId = input.TransactionId,
                ExternalEntityId = input.EntityId,
                ExternalAccountId = input.AccountId,
                Amount = -Math.Abs(input.Amount), // negative = withdrawal
                Counterparty = new AlloyTmsCounterparty
                {
                    ExternalCounterpartyId = input.CounterpartyAccountId ?? Guid.NewGuid().ToString(),
                    AccountName = counterpartyName,
                    AccountNumber = input.CounterpartyAccountNumber,
                    AccountType = "individual account"
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        SetEventsAuth(request);
        request.Content = BuildJsonContent(requestBody);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Alloy TMS event failed. EntityId={EntityId} StatusCode={StatusCode} Body={Body}",
                input.EntityId, (int)response.StatusCode, responseBody);

            // TMS errors default to Approved per original Node fallback behavior
            return new AlloyTmsResponse
            {
                StatusCode = (int)response.StatusCode,
                Summary = new AlloyOutcomeSummary
                {
                    Outcome = "Approved",
                    Tags = ["R0: TMS Cleared (Fallback — API Error)"]
                }
            };
        }

        var result = JsonSerializer.Deserialize<AlloyTmsResponse>(responseBody, _jsonOptions)
            ?? new AlloyTmsResponse();

        result.StatusCode = (int)response.StatusCode;

        // Parse outcome from evaluations[0].summary.outcome — synchronous result
        var evaluation = result.Event?.Evaluations?.FirstOrDefault();
        if (evaluation is not null)
        {
            result.Summary = evaluation.Summary;
            _logger.LogInformation(
                "Alloy TMS outcome. EntityId={EntityId} Outcome={Outcome} Tags={Tags} EvaluationToken={EvalToken}",
                input.EntityId,
                evaluation.Summary.Outcome,
                string.Join(", ", evaluation.Summary.Tags),
                evaluation.EvaluationToken);
        }
        else
        {
            // No evaluations — default to Approved and log warning
            _logger.LogWarning(
                "Alloy TMS returned no evaluations — defaulting to Approved. EntityId={EntityId} EventToken={EventToken}",
                input.EntityId, result.EventRequestToken);

            result.Summary = new AlloyOutcomeSummary
            {
                Outcome = "Approved",
                Tags = [$"R0: TMS Submitted (no evaluations — EventToken={result.EventRequestToken})"]
            };
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Bank Account Created — POST /v1/events
    // -------------------------------------------------------------------------

    public async Task NotifyBankAccountCreatedAsync(
        AlloyBankAccountCreatedRequest requestBody,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Notifying Alloy bank_account_created. ExternalEntityId={EntityId} AccountId={AccountId}",
            requestBody.Data.ExternalEntityId, requestBody.Data.ExternalAccountId);

        var url = $"{_settings.ALLOY_BASE_URL}/v1/events";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        SetEventsAuth(request);
        request.Content = BuildJsonContent(requestBody);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Alloy bank_account_created event failed. EntityId={EntityId} StatusCode={StatusCode} Body={Body}",
                requestBody.Data.ExternalEntityId, (int)response.StatusCode, responseBody);
            // Fire and forget — don't throw, account is already created in Cosmos
            return;
        }

        _logger.LogInformation(
            "Alloy bank_account_created accepted. EntityId={EntityId}",
            requestBody.Data.ExternalEntityId);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void SetKycAuth(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                $"{_settings.ALLOY_INDIVIDUAL_KYC_JOURNEY_TOKEN}:{_settings.ALLOY_KYC_WORKFLOW_SECRET}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private void SetEventsAuth(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                $"{_settings.ALLOY_API_TOKEN}:{_settings.ALLOY_API_SECRET}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private void SetSandboxHeader(HttpRequestMessage request)
    {
        if (_settings.ALLOY_SANDBOX)
            request.Headers.Add("alloy-sandbox", "true");
    }

    private static StringContent BuildJsonContent(object body)
    {
        var json = JsonSerializer.Serialize(body, _jsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static List<AlloyAddress>? BuildAddress(AlloyKycInput input)
    {
        if (string.IsNullOrWhiteSpace(input.AddressLine1))
            return null;

        return
        [
            new AlloyAddress
            {
                Type = "primary",
                Line1 = input.AddressLine1,
                City = input.AddressCity,
                State = input.AddressState,
                PostalCode = input.AddressPostalCode,
                CountryCode = input.AddressCountryCode ?? "US"
            }
        ];
    }
}
