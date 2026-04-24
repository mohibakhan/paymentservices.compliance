using System.Diagnostics.CodeAnalysis;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentServices.Compliance.Models;
using PaymentServices.Compliance.Repositories;
using PaymentServices.Compliance.Services;
using PaymentServices.Shared.Extensions;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

namespace PaymentServices.Compliance;

[ExcludeFromCodeCoverage]
public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(SetupAppConfiguration)
            .ConfigureFunctionsWebApplication()
            .ConfigureServices((context, services) =>
            {
                var config = context.Configuration;

                SetupSerilog(config);

                // Application Insights
                services.AddApplicationInsightsTelemetryWorkerService();
                services.ConfigureFunctionsApplicationInsights();

                // Shared infrastructure
                services.AddPaymentAppSettings(config);
                services.AddPaymentCosmosClient(config);
                services.AddPaymentServiceBusPublisher(config);

                // Compliance-specific settings
                services.AddOptions<ComplianceSettings>()
                    .Configure<IConfiguration>((settings, cfg) =>
                        cfg.GetSection("app:AppSettings").Bind(settings));

                // Cosmos containers
                var database = config["app:AppSettings:COSMOS_DATABASE"] ?? "tptch";

                services.AddKeyedSingleton<Container>("transactions", (sp, _) =>
                {
                    var client = sp.GetRequiredService<CosmosClient>();
                    var container = config["app:AppSettings:COSMOS_TRANSACTIONS_CONTAINER"]
                        ?? "tchSendTransactions";
                    return client.GetContainer(database, container);
                });

                // Alloy HTTP client
                services.AddHttpClient<IAlloyClient, AlloyClient>();

                // Redis cache — singleton connection, silent fallback if unavailable
                var redisConnString = config["app:AppSettings:REDIS_CONNSTRING"] ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(redisConnString))
                {
                    services.AddSingleton<IConnectionMultiplexer>(_ =>
                        ConnectionMultiplexer.Connect(redisConnString));
                    services.AddSingleton<ICacheService, RedisCacheService>();
                }
                else
                {
                    services.AddSingleton<ICacheService, NoOpCacheService>();
                }

                // Repositories
                services.AddTransient<ITransactionStateRepository, TransactionStateRepository>();

                // Services
                services.AddTransient<IKycService, KycService>();
                services.AddTransient<ITmsService, TmsService>();

                services.AddHealthChecks();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.Services.Configure<LoggerFilterOptions>(options =>
                {
                    var defaultRule = options.Rules.FirstOrDefault(rule =>
                        rule.ProviderName ==
                        "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
                    if (defaultRule is not null)
                        options.Rules.Remove(defaultRule);
                });

                logging.AddSerilog(dispose: true);
            })
            .Build();

        await host.RunAsync();
    }

    private static void SetupAppConfiguration(IConfigurationBuilder builder)
    {
        builder.AddEnvironmentVariables();
        var settings = builder.Build();

        var appConfigUrl = settings["AppConfig:Endpoint"];
        var azureClientId = settings["AZURE_CLIENT_ID"];

        if (!string.IsNullOrWhiteSpace(appConfigUrl) && !string.IsNullOrWhiteSpace(azureClientId))
        {
            var credentialOptions = new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = azureClientId
            };
            var credential = new DefaultAzureCredential(credentialOptions);

            builder.AddAzureAppConfiguration(options =>
            {
                options
                    .Connect(new Uri(appConfigUrl), credential)
                    .Select("app:*")
                    .Select("telemetry:*")
                    .ConfigureKeyVault(kv => kv.SetCredential(credential));
            });
        }

        builder
            .SetBasePath(Environment.CurrentDirectory)
            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: false);
    }

    private static void SetupSerilog(IConfiguration config)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Azure.Functions.Worker", LogEventLevel.Warning)
            .MinimumLevel.Override("Host", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "PaymentServices.Compliance")
            .Enrich.WithProperty("Environment",
                Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Production")
            .CreateLogger();
    }
}
