using System.Reflection;
using Autotrade.Application.Readiness;
using Autotrade.Cli.Diagnostics;
using Autotrade.Cli.Health;
using Autotrade.Cli.HostedServices;
using Autotrade.Cli.Logging;
using Autotrade.Cli.Observability;
using Autotrade.Cli.Readiness;
using Autotrade.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Autotrade.Cli.Infrastructure;

public static class HostBuilderExtensions
{
    public static IHost BuildAutotradeHost(
        string[] args,
        string? configPath = null,
        bool suppressConsoleLogs = false)
    {
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? Environment.CurrentDirectory;

        return Host
            .CreateDefaultBuilder(args)
            .UseContentRoot(exeDir)
            .ConfigureAppConfiguration((_, config) =>
            {
                if (!string.IsNullOrWhiteSpace(configPath))
                {
                    config.AddJsonFile(configPath, optional: false, reloadOnChange: true);
                }

                config.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

                var devProjectDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
                if (File.Exists(Path.Combine(devProjectDir, "Autotrade.Cli.csproj")))
                {
                    config.AddJsonFile(
                        new PhysicalFileProvider(devProjectDir),
                        "appsettings.local.json",
                        optional: true,
                        reloadOnChange: true);
                }
            })
            .UseSerilog((context, _, loggerConfiguration) =>
            {
                if (suppressConsoleLogs)
                {
                    loggerConfiguration.MinimumLevel.Fatal();
                    return;
                }

                loggerConfiguration
                    .ReadFrom.Configuration(context.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.With(new CorrelationIdEnricher())
                    .Enrich.With(new TraceContextEnricher());
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<DiagnosticsOptions>(
                    context.Configuration.GetSection(DiagnosticsOptions.SectionName));

                services.Configure<ApiHealthCheckOptions>(
                    context.Configuration.GetSection(ApiHealthCheckOptions.SectionName));

                services.Configure<BackgroundServiceHealthCheckOptions>(
                    context.Configuration.GetSection(BackgroundServiceHealthCheckOptions.SectionName));
                services.AddScoped<IReadinessProbeCollector, CliReadinessProbeCollector>();
                services.AddScoped<IReadinessReportService, ReadinessReportService>();

                ThrowIfLiveModeUsesInMemoryEventBus(context.Configuration);

                var backgroundJobsEnabled = context.Configuration.GetValue("BackgroundJobs:Enabled", true);
                services.AddAutotradeModules(
                    context.Configuration,
                    context.HostingEnvironment,
                    options => options.RegisterHangfireCore = backgroundJobsEnabled);

                services.AddHostedService<AutotradeDatabaseMigrationHostedService>();
                if (backgroundJobsEnabled)
                {
                    services.AddHostedService<HangfireRecurringJobsHostedService>();
                }

                services.AddObservability(context.Configuration);

                services.AddHealthChecks()
                    .AddCheck<LivenessHealthCheck>("liveness", tags: new[] { "live" })
                    .AddAutotradeDatabaseHealthChecks()
                    .AddCheck<ComplianceHealthCheck>("compliance", tags: new[] { "ready" })
                    .AddCheck<RiskKillSwitchHealthCheck>("risk_killswitch", tags: new[] { "ready" })
                    .AddCheck<WebSocketHealthCheck>("websocket", tags: new[] { "ready" })
                    .AddCheck<ApiReachabilityHealthCheck>("polymarket_api", tags: new[] { "ready" })
                    .AddCheck<BackgroundServiceHealthCheck>("background_services", tags: new[] { "ready" });

                services.AddHostedService<AutotradeHostedService>();
                services.AddHostedService<DiagnosticsService>();
            })
            .Build();
    }

    private static void ThrowIfLiveModeUsesInMemoryEventBus(IConfiguration configuration)
    {
        var executionMode = configuration["Execution:Mode"];
        var useInMemoryEventBus = configuration.GetValue<bool?>("EventBus:UseInMemory");

        if (string.Equals(executionMode, "Live", StringComparison.OrdinalIgnoreCase)
            && useInMemoryEventBus == true)
        {
            throw new InvalidOperationException(
                "Live execution requires a durable EventBus. Set EventBus:UseInMemory=false and configure RabbitMQ.");
        }
    }
}
