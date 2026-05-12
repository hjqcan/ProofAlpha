using Autotrade.Application.Readiness;
using Autotrade.Application.Security;
using Autotrade.Hosting;
using Autotrade.Polymarket.BuilderAttribution;
using Autotrade.Polymarket.Options;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Autotrade.Cli.Readiness;

public sealed class CliReadinessProbeCollector(
    IConfiguration configuration,
    HealthCheckService healthCheckService,
    IServiceScopeFactory scopeFactory,
    IAutotradeDatabaseDiagnostics databaseDiagnostics,
    IAutotradeModuleInventory moduleInventory) : IReadinessProbeCollector
{
    public async Task<IReadOnlyDictionary<string, ReadinessCheckProbe>> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var health = await healthCheckService
            .CheckHealthAsync(entry => entry.Tags.Contains("ready"), cancellationToken)
            .ConfigureAwait(false);

        var probes = new Dictionary<string, ReadinessCheckProbe>(StringComparer.Ordinal)
        {
            ["runtime.configuration.loaded"] = Ready("Autotrade.Cli", "CLI configuration loaded.", now),
            ["runtime.modules.inventory"] = ProbeModuleInventory(now),
            ["database.connection"] = MapDatabaseHealth(health, now),
            ["market_data.public_api.reachable"] = MapHealthEntry(health, "polymarket_api", "Polymarket public API", now),
            ["market_data.websocket.healthy"] = MapHealthEntry(health, "websocket", "MarketData WebSocket clients", now),
            ["background_jobs.heartbeats.fresh"] = MapHealthEntry(health, "background_services", "BackgroundJobs", now),
            ["compliance.geo_kyc.confirmed"] = MapCompliance(health, now),
            ["risk.limits.configured"] = MapRisk(health, now),
            ["execution.mode.paper_default"] = ProbePaperDefault(now),
            ["execution.live_armed"] = await ProbeLiveArmingAsync(now, cancellationToken).ConfigureAwait(false),
            ["account_sync.configured"] = ProbeAccountSync(now),
            ["credentials.exchange.present"] = ProbeExchangeAuthentication(now),
            ["polymarket.builder_attribution.ready"] = ProbeBuilderAttribution(now),
            ["api.control_room.reachable"] = await ProbeControlRoomApiAsync(now, cancellationToken).ConfigureAwait(false)
        };

        probes["database.migrations.current"] = await ProbeDatabaseMigrationsAsync(now, cancellationToken)
            .ConfigureAwait(false);

        return probes;
    }

    private static ReadinessCheckProbe MapDatabaseHealth(HealthReport health, DateTimeOffset now)
    {
        var databaseEntries = health.Entries
            .Where(entry => entry.Key is
                "trading_db" or
                "marketdata_db" or
                "strategy_db" or
                "selfimprove_db" or
                "opportunity_discovery_db")
            .ToArray();

        if (databaseEntries.Length == 0)
        {
            return Skipped("HealthCheckService", "Database health checks were not registered.", now);
        }

        var unhealthy = databaseEntries
            .Where(entry => entry.Value.Status == HealthStatus.Unhealthy)
            .Select(entry => entry.Key)
            .ToArray();
        if (unhealthy.Length > 0)
        {
            return Unhealthy(
                "HealthCheckService",
                $"Database connection failed for: {string.Join(", ", unhealthy)}.",
                now,
                "Start PostgreSQL or fix ConnectionStrings:AutotradeDatabase.");
        }

        return Ready("HealthCheckService", "All bounded-context database health checks passed.", now);
    }

    private async Task<ReadinessCheckProbe> ProbeDatabaseMigrationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var diagnostics = await databaseDiagnostics.CheckMigrationsAsync(cancellationToken).ConfigureAwait(false);
        if (diagnostics.Count == 0)
        {
            return Skipped("EF Core", "Database contexts were not registered.", now);
        }

        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Failure is not null)
            {
                failures[diagnostic.Name] = FormatProbeException(diagnostic.Failure);
            }
            else if (diagnostic.PendingMigrationCount > 0)
            {
                pending[diagnostic.Name] = diagnostic.PendingMigrationCount.ToString();
            }
        }

        if (failures.Count > 0)
        {
            return Unhealthy(
                "EF Core",
                "Database migration check failed for one or more bounded contexts.",
                now,
                "Fix database connectivity and migration metadata access before starting automated trading.",
                failures);
        }

        return pending.Count == 0
            ? Ready("EF Core", "All bounded-context migrations are current.", now)
            : Unhealthy(
                "EF Core",
                "Pending database migrations are present.",
                now,
                "Apply pending migrations before starting automated trading.",
                pending);
    }

    private static ReadinessCheckProbe MapHealthEntry(
        HealthReport health,
        string entryName,
        string source,
        DateTimeOffset now)
    {
        return health.Entries.TryGetValue(entryName, out var entry)
            ? FromHealthEntry(source, entry, now)
            : Skipped(source, $"{entryName} health check was not registered.", now);
    }

    private static ReadinessCheckProbe MapCompliance(HealthReport health, DateTimeOffset now)
    {
        if (!health.Entries.TryGetValue("compliance", out var entry))
        {
            return Skipped("ComplianceGuard", "Compliance health check was not registered.", now);
        }

        return FromHealthEntry("ComplianceGuard", entry, now);
    }

    private static ReadinessCheckProbe MapRisk(HealthReport health, DateTimeOffset now)
    {
        if (!health.Entries.TryGetValue("risk_killswitch", out var entry))
        {
            return Skipped("RiskOptions", "Risk health check was not registered.", now);
        }

        return entry.Status == HealthStatus.Healthy
            ? Ready("RiskOptions", "Risk controls are configured and kill switch is inactive.", now)
            : FromHealthEntry("RiskOptions", entry, now);
    }

    private ReadinessCheckProbe ProbePaperDefault(DateTimeOffset now)
    {
        var mode = configuration["Execution:Mode"] ?? "Paper";
        return string.Equals(mode, "Paper", StringComparison.OrdinalIgnoreCase)
            ? Ready("ExecutionOptions", "Execution mode is Paper.", now, ("mode", mode))
            : Unhealthy("ExecutionOptions", $"Execution mode is {mode}.", now, "Set Execution:Mode=Paper for first run.");
    }

    private async Task<ReadinessCheckProbe> ProbeLiveArmingAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var liveArmingService = scope.ServiceProvider.GetService<ILiveArmingService>();
        if (liveArmingService is null)
        {
            return Blocked(
                "Live arming gate",
                "Live arming service is unavailable in this CLI host.",
                now,
                "Register Trading services before probing or changing Live arming state.");
        }

        try
        {
            var status = await liveArmingService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["state"] = status.State,
                ["configVersion"] = status.ConfigVersion
            };

            if (status.Evidence is not null)
            {
                evidence["evidenceId"] = status.Evidence.EvidenceId;
                evidence["operator"] = status.Evidence.Operator;
                evidence["armedAtUtc"] = status.Evidence.ArmedAtUtc.ToString("O");
                evidence["expiresAtUtc"] = status.Evidence.ExpiresAtUtc.ToString("O");
            }

            return status.IsArmed
                ? new ReadinessCheckProbe(
                    ReadinessCheckStatus.Ready,
                    "Live arming gate",
                    status.Reason,
                    status.CheckedAtUtc,
                    Evidence: evidence)
                : new ReadinessCheckProbe(
                    ReadinessCheckStatus.Blocked,
                    "Live arming gate",
                    $"{status.State}: {status.Reason}",
                    status.CheckedAtUtc,
                    "Complete the Live arming checklist from the CLI or control room.",
                    evidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Blocked(
                "Live arming gate",
                $"Live arming probe failed: {FormatProbeException(ex)}",
                now,
                "Fix live arming service registration before enabling Live trading.");
        }
    }

    private ReadinessCheckProbe ProbeAccountSync(DateTimeOffset now)
    {
        var enabled = configuration.GetValue("AccountSync:Enabled", true);
        return enabled
            ? Ready("Trading account sync", "Account sync is enabled.", now, ("enabled", "true"))
            : Blocked("Trading account sync", "Account sync is disabled.", now, "Set AccountSync:Enabled=true before Live arming.");
    }

    private ReadinessCheckProbe ProbeExchangeAuthentication(DateTimeOffset now)
    {
        var credentials = CredentialPresenceDiagnostics.Evaluate(
            CredentialPresenceDiagnostics.PolymarketClobFields,
            path => configuration[path]);

        return credentials.AllPresent
            ? new ReadinessCheckProbe(
                ReadinessCheckStatus.Ready,
                "Server configuration",
                "Exchange authentication settings are present.",
                now,
                Evidence: credentials.Evidence)
            : new ReadinessCheckProbe(
                ReadinessCheckStatus.Blocked,
                "Server configuration",
                $"Exchange authentication settings are missing: {string.Join(", ", credentials.MissingFields)}.",
                now,
                "Provide exchange authentication through user secrets or environment variables.",
                credentials.Evidence);
    }

    private ReadinessCheckProbe ProbeBuilderAttribution(DateTimeOffset now)
    {
        var readiness = PolymarketBuilderAttribution.EvaluateReadiness(
            new PolymarketClobOptions { BuilderCode = configuration["Polymarket:Clob:BuilderCode"] },
            configuration["Execution:Mode"]);
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["configured"] = readiness.BuilderCodeConfigured.ToString(),
            ["formatValid"] = readiness.FormatValid.ToString(),
            ["mode"] = readiness.Mode
        };

        if (!string.IsNullOrWhiteSpace(readiness.BuilderCodeHash))
        {
            evidence["builderCodeHash"] = readiness.BuilderCodeHash;
        }

        return readiness.Mode switch
        {
            "invalid" => new ReadinessCheckProbe(
                ReadinessCheckStatus.Unhealthy,
                "Polymarket builder attribution",
                readiness.Summary,
                now,
                "Configure Polymarket:Clob:BuilderCode as a bytes32 hex string or leave it unset.",
                evidence),
            "disabled" => new ReadinessCheckProbe(
                ReadinessCheckStatus.Skipped,
                "Polymarket builder attribution",
                readiness.Summary,
                now,
                "Set Polymarket:Clob:BuilderCode to produce builder-attributed order evidence.",
                evidence),
            _ => new ReadinessCheckProbe(
                ReadinessCheckStatus.Ready,
                "Polymarket builder attribution",
                readiness.Summary,
                now,
                Evidence: evidence)
        };
    }

    private async Task<ReadinessCheckProbe> ProbeControlRoomApiAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Readiness:ControlRoomApiBaseUrl"] ?? "http://localhost:5080";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl, UriKind.Absolute) };
            using var response = await client.GetAsync("/api/health", timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? Ready("Autotrade.Api", "Control room API health endpoint responded.", now, ("baseUrl", baseUrl))
                : Unhealthy(
                    "Autotrade.Api",
                    $"Control room API returned HTTP {(int)response.StatusCode}.",
                    now,
                    "Start Autotrade.Api locally or set Readiness:ControlRoomApiBaseUrl.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unhealthy(
                "Autotrade.Api",
                "Control room API probe timed out.",
                now,
                "Start Autotrade.Api locally or set Readiness:ControlRoomApiBaseUrl.");
        }
        catch (Exception ex)
        {
            return Unhealthy(
                "Autotrade.Api",
                $"Control room API probe failed: {SecretRedactor.Redact(ex.Message)}",
                now,
                "Start Autotrade.Api locally or set Readiness:ControlRoomApiBaseUrl.");
        }
    }

    private ReadinessCheckProbe ProbeModuleInventory(DateTimeOffset now)
    {
        var evidence = BuildModuleInventoryEvidence(moduleInventory);
        if (!moduleInventory.Enabled)
        {
            return new ReadinessCheckProbe(
                ReadinessCheckStatus.Skipped,
                "Autotrade.Hosting",
                "Autotrade modules are not registered in this CLI host.",
                now,
                Evidence: evidence);
        }

        return new ReadinessCheckProbe(
            ReadinessCheckStatus.Ready,
            "Autotrade.Hosting",
            "Autotrade bounded-context modules are registered.",
            now,
            Evidence: evidence);
    }

    private static Dictionary<string, string> BuildModuleInventoryEvidence(IAutotradeModuleInventory inventory)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["enabled"] = inventory.Enabled.ToString(),
            ["connectionStringName"] = inventory.ConnectionStringName,
            ["eventBusRegistered"] = inventory.EventBusRegistered.ToString(),
            ["hangfireRegistered"] = inventory.HangfireRegistered.ToString(),
            ["persistenceRegistered"] = inventory.PersistenceRegistered.ToString(),
            ["modules"] = string.Join(",", inventory.Modules.Select(module => module.Name)),
            ["dataContexts"] = string.Join(
                ",",
                inventory.Modules
                    .Where(module => module.DataContextRegistered)
                    .Select(module => module.DataContextName)),
            ["backgroundJobModules"] = string.Join(
                ",",
                inventory.Modules
                    .Where(module => module.BackgroundJobsRegistered)
                    .Select(module => module.Name))
        };
    }

    private static string FormatProbeException(Exception ex)
        => string.IsNullOrWhiteSpace(ex.Message)
            ? ex.GetType().Name
            : $"{ex.GetType().Name}: {SecretRedactor.Redact(ex.Message)}";

    private static ReadinessCheckProbe FromHealthEntry(
        string source,
        HealthReportEntry entry,
        DateTimeOffset now)
    {
        var status = entry.Status switch
        {
            HealthStatus.Healthy => ReadinessCheckStatus.Ready,
            HealthStatus.Degraded => ReadinessCheckStatus.Degraded,
            _ => ReadinessCheckStatus.Unhealthy
        };

        return new ReadinessCheckProbe(
            status,
            source,
            entry.Description ?? entry.Status.ToString(),
            now,
            Evidence: entry.Data.ToDictionary(item => item.Key, item => item.Value?.ToString() ?? string.Empty, StringComparer.Ordinal));
    }

    private static ReadinessCheckProbe Ready(
        string source,
        string summary,
        DateTimeOffset now,
        params (string Key, string Value)[] evidence)
        => new(
            ReadinessCheckStatus.Ready,
            source,
            summary,
            now,
            Evidence: evidence.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    private static ReadinessCheckProbe Skipped(
        string source,
        string summary,
        DateTimeOffset now)
        => new(ReadinessCheckStatus.Skipped, source, summary, now);

    private static ReadinessCheckProbe Unhealthy(
        string source,
        string summary,
        DateTimeOffset now,
        string? remediationHint = null,
        IReadOnlyDictionary<string, string>? evidence = null)
        => new(ReadinessCheckStatus.Unhealthy, source, summary, now, remediationHint, evidence);

    private static ReadinessCheckProbe Blocked(
        string source,
        string summary,
        DateTimeOffset now,
        string remediationHint)
        => new(ReadinessCheckStatus.Blocked, source, summary, now, remediationHint);
}
