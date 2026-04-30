using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentServices.Compliance.Models;
using PaymentServices.Shared.Enums;
using PaymentServices.Shared.Messages;

namespace PaymentServices.Compliance.Services;

public interface IKycService
{
    /// <summary>
    /// Runs KYC for both source and destination parties in parallel.
    /// Checks Redis cache first — only calls Alloy on cache miss.
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

/// <summary>
/// Cached KYC result stored in Redis.
/// </summary>
public sealed class CachedKycResult
{
    public required string EntityId { get; init; }
    public required string Outcome { get; init; }
    public List<string> Tags { get; init; } = [];
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class KycService : IKycService
{
    private readonly IAlloyClient _alloyClient;
    private readonly ICacheService _cacheService;
    private readonly ComplianceSettings _settings;
    private readonly ILogger<KycService> _logger;

    private const string CacheKeyPrefix = "kyc:result:";

    public KycService(
        IAlloyClient alloyClient,
        ICacheService cacheService,
        IOptions<ComplianceSettings> settings,
        ILogger<KycService> logger)
    {
        _alloyClient = alloyClient;
        _cacheService = cacheService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<KycCheckResult> RunAsync(
        PaymentMessage message,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Running KYC checks. EvolveId={EvolveId} SourceEntityId={SourceEntityId} DestinationEntityId={DestinationEntityId}",
            message.EvolveId, message.Source.EntityId, message.Destination.EntityId);

        // Run source and destination KYC in parallel — both cache-first
        var sourceTask = RunKycWithCacheAsync(message.Source, cancellationToken);
        var destinationTask = RunKycWithCacheAsync(message.Destination, cancellationToken);

        await Task.WhenAll(sourceTask, destinationTask);

        var sourceResponse = await sourceTask;
        var destinationResponse = await destinationTask;

        var sourceOutcome = ParseKycOutcome(sourceResponse.Outcome);
        var destinationOutcome = ParseKycOutcome(destinationResponse.Outcome);

        _logger.LogInformation(
            "KYC results. EvolveId={EvolveId} SourceOutcome={SourceOutcome} DestinationOutcome={DestinationOutcome}",
            message.EvolveId, sourceOutcome, destinationOutcome);

        // Manual Review
        if (sourceOutcome == KycOutcome.ManualReview || destinationOutcome == KycOutcome.ManualReview)
        {
            var party = sourceOutcome == KycOutcome.ManualReview ? "Source" : "Destination";
            return new KycCheckResult
            {
                Passed = false,
                SourceOutcome = sourceOutcome,
                DestinationOutcome = destinationOutcome,
                FailureReason = $"{party} KYC flagged for manual review.",
                Flags = [.. sourceResponse.Tags, .. destinationResponse.Tags]
            };
        }

        // Denied
        if (sourceOutcome == KycOutcome.Denied || destinationOutcome == KycOutcome.Denied)
        {
            var party = sourceOutcome == KycOutcome.Denied ? "Source" : "Destination";
            return new KycCheckResult
            {
                Passed = false,
                SourceOutcome = sourceOutcome,
                DestinationOutcome = destinationOutcome,
                FailureReason = $"{party} KYC failed.",
                Flags = [.. sourceResponse.Tags, .. destinationResponse.Tags]
            };
        }

        // Both approved
        return new KycCheckResult
        {
            Passed = true,
            SourceOutcome = sourceOutcome,
            DestinationOutcome = destinationOutcome,
            Flags = [.. sourceResponse.Tags, .. destinationResponse.Tags]
        };
    }

    // -------------------------------------------------------------------------
    // Cache-first KYC check per party
    // -------------------------------------------------------------------------

    private async Task<AlloyKycResponse> RunKycWithCacheAsync(
        PaymentParty party,
        CancellationToken cancellationToken)
    {
        var entityId = party.EntityId ?? string.Empty;
        var cacheKey = $"{CacheKeyPrefix}{entityId}";
        var cacheTtl = TimeSpan.FromDays(_settings.KYC_CACHE_TTL_DAYS);

        // Check Redis cache first
        var cached = await _cacheService.GetAsync<CachedKycResult>(cacheKey, cancellationToken);

        if (cached is not null)
        {
            _logger.LogInformation(
                "KYC result from cache. EntityId={EntityId} Outcome={Outcome}",
                entityId, cached.Outcome);

            // Return cached result as AlloyKycResponse
            return new AlloyKycResponse
            {
                CompleteOutcome = cached.Outcome,
                JourneyApplicationStatus = cached.Outcome,
                Embedded = new AlloyKycEmbedded
                {
                    EntityApplications =
                    [
                        new AlloyEntityApplication
                        {
                            Output = new AlloyEntityApplicationOutput
                            {
                                Tags = cached.Tags
                            }
                        }
                    ]
                }
            };
        }

        _logger.LogInformation(
            "KYC cache miss — calling Alloy. EntityId={EntityId}", entityId);

        // Call Alloy
        var input = BuildKycInput(party);
        var response = await _alloyClient.RunKycAsync(input, cancellationToken);

        // Only cache Approved results
        if (response.Outcome == "Approved")
        {
            var cachedResult = new CachedKycResult
            {
                EntityId = entityId,
                Outcome = response.Outcome,
                Tags = response.Tags,
                CachedAt = DateTimeOffset.UtcNow
            };

            await _cacheService.SetAsync(cacheKey, cachedResult, cacheTtl, cancellationToken);

            _logger.LogInformation(
                "KYC result cached. EntityId={EntityId} TTL={TTL}days",
                entityId, _settings.KYC_CACHE_TTL_DAYS);
        }

        return response;
    }

    private static AlloyKycInput BuildKycInput(PaymentParty party)
    {
        var isBusiness = party.Name.IsBusiness;
        return new AlloyKycInput
        {
            EntityId = party.EntityId ?? string.Empty,
            IsBusiness = isBusiness,
            NameFirst = isBusiness ? null : party.Name.First,
            NameLast = isBusiness ? null : party.Name.Last,
            BusinessName = isBusiness ? party.Name.Company : null,
            AddressLine1 = party.Address?.Line1,
            AddressCity = party.Address?.City,
            AddressState = party.Address?.State,
            AddressPostalCode = party.Address?.PostalCode,
            AddressCountryCode = party.Address?.CountryCode ?? "US"
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

"compliance:AppSettings:ALLOY_BASE_URL": "https://sandbox.alloy.co",
"compliance:AppSettings:ALLOY_SANDBOX": "true",
"compliance:AppSettings:ALLOY_API_TOKEN": "<alloy-api-token>",
"compliance:AppSettings:ALLOY_API_SECRET": "<alloy-api-secret>",
"compliance:AppSettings:ALLOY_INDIVIDUAL_KYC_JOURNEY_TOKEN": "<individual-kyc-journey-token>",
"compliance:AppSettings:ALLOY_BUSINESS_KYC_JOURNEY_TOKEN": "<business-kyc-journey-token>",
"compliance:AppSettings:ALLOY_KYC_WORKFLOW_SECRET": "<kyc-workflow-secret>",
"compliance:AppSettings:ALLOY_TMS_JOURNEY_TOKEN": "<rtp-tms-journey-token>",
