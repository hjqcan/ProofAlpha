using Autotrade.Api.ControlRoom;
using Autotrade.Application.Readiness;
using Autotrade.Application.Security;
using Autotrade.Hosting;
using Autotrade.MarketData.Application.WebSocket.Clob;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.Options;

namespace Autotrade.Api.Readiness;

public sealed class ApiReadinessProbeCollector(
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ControlRoomOptions> controlRoomOptions,
    IAutotradeDatabaseDiagnostics databaseDiagnostics,
    IAutotradeModuleInventory moduleInventory) : IReadinessProbeCollector
{
    public async Task<IReadOnlyDictionary<string, ReadinessCheckProbe>> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var probes = new Dictionary<string, ReadinessCheckProbe>(StringComparer.Ordinal)
        {
            ["runtime.configuration.loaded"] = Ready(
                "Autotrade.Api",
                "API configuration loaded.",
                now,
                ("environment", configuration["ASPNETCORE_ENVIRONMENT"] ?? "unknown")),
            ["runtime.modules.inventory"] = ProbeModuleInventory(now),
            ["api.control_room.reachable"] = Ready(
                "Autotrade.Api",
                "Control room API is reachable from this process.",
                now,
                ("localAccessRequired", controlRoomOptions.CurrentValue.RequireLocalAccess.ToString())),
            ["execution.mode.paper_default"] = ProbePaperDefault(now),
            ["execution.live_armed"] = await ProbeLiveArmingAsync(now, cancellationToken).ConfigureAwait(false),
            ["risk.limits.configured"] = ProbeRiskLimits(now),
            ["background_jobs.heartbeats.fresh"] = ProbeBackgroundJobs(now),
            ["account_sync.configured"] = ProbeAccountSync(now),
            ["compliance.geo_kyc.confirmed"] = ProbeCompliance(now),
            ["credentials.exchange.present"] = ProbeExchangeAuthentication(now)
        };

        probes["database.connection"] = await ProbeDatabaseConnectionAsync(now, cancellationToken)
            .ConfigureAwait(false);
        probes["database.migrations.current"] = await ProbeDatabaseMigrationsAsync(now, cancellationToken)
            .ConfigureAwait(false);

        using var scope = scopeFactory.CreateScope();
        probes["market_data.public_api.reachable"] = await ProbePublicMarketDataAsync(scope, now, cancellationToken)
            .ConfigureAwait(false);
        probes["market_data.websocket.healthy"] = ProbeWebSocket(scope, now);

        return probes;
    }

    private async Task<ReadinessCheckProbe> ProbeDatabaseConnectionAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var diagnostics = await databaseDiagnostics.CheckConnectionsAsync(cancellationToken).ConfigureAwait(false);
        if (diagnostics.Count == 0)
        {
            return Skipped(
                "EF Core",
                "Database contexts are not registered in this API process.",
                now,
                "Enable AutotradeApi:EnableModules or run CLI readiness from the full trading host.");
        }

        var failures = new List<string>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Failure is not null)
            {
                failures.Add($"{diagnostic.Name} ({FormatProbeException(diagnostic.Failure)})");
            }
            else if (!diagnostic.CanConnect)
            {
                failures.Add(diagnostic.Name);
            }
        }

        return failures.Count == 0
            ? Ready(
                "EF Core",
                "All bounded-context databases are reachable.",
                now,
                ("contexts", string.Join(",", diagnostics.Select(item => item.Name))))
            : Unhealthy("EF Core", $"Database connection failed for: {string.Join(", ", failures)}.", now);
    }

    private async Task<ReadinessCheckProbe> ProbeDatabaseMigrationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var diagnostics = await databaseDiagnostics.CheckMigrationsAsync(cancellationToken).ConfigureAwait(false);
        if (diagnostics.Count == 0)
        {
            return Skipped(
                "EF Core",
                "Database migration checks are not available because DbContexts are not registered.",
                now,
                "Enable AutotradeApi:EnableModules or run CLI readiness from the full trading host.");
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

    private async Task<ReadinessCheckProbe> ProbePublicMarketDataAsync(
        IServiceScope scope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!controlRoomOptions.CurrentValue.EnablePublicMarketData)
        {
            return Skipped(
                "Polymarket public API",
                "Public market data is disabled by configuration.",
                now,
                "Set AutotradeApi:ControlRoom:EnablePublicMarketData=true when public discovery should be verified.");
        }

        var clobClient = scope.ServiceProvider.GetService<IPolymarketClobClient>();
        if (clobClient is null)
        {
            return Skipped(
                "Polymarket public API",
                "Polymarket CLOB client is not registered in this API process.",
                now,
                "Enable control-room public market data registration before probing public API reachability.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await clobClient.GetServerTimeAsync(timeout.Token).ConfigureAwait(false);
            return Ready("Polymarket public API", "Public market data endpoint responded.", now);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unhealthy(
                "Polymarket public API",
                "Public market data probe timed out.",
                now,
                "Check network access to the configured Polymarket CLOB host.");
        }
        catch (Exception ex)
        {
            return Unhealthy(
                "Polymarket public API",
                $"Public market data probe failed: {SecretRedactor.Redact(ex.Message)}",
                now,
                "Check network access to the configured Polymarket CLOB host.");
        }
    }

    private static ReadinessCheckProbe ProbeWebSocket(IServiceScope scope, DateTimeOffset now)
    {
        var clobClient = scope.ServiceProvider.GetService<IClobMarketClient>();
        if (clobClient is null)
        {
            return Skipped(
                "MarketData WebSocket clients",
                "CLOB WebSocket client is not registered in this API process.",
                now,
                "Enable AutotradeApi:EnableModules or run CLI readiness from the full trading host.");
        }

        var subscribedAssetCount = clobClient.SubscribedAssets.Count;
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["clob_connected"] = clobClient.IsConnected.ToString(),
            ["clob_state"] = clobClient.State.ToString(),
            ["clob_subscribed_assets"] = subscribedAssetCount.ToString()
        };

        if (clobClient.IsConnected)
        {
            return new ReadinessCheckProbe(
                ReadinessCheckStatus.Ready,
                "MarketData WebSocket clients",
                "CLOB WebSocket is connected.",
                now,
                Evidence: evidence);
        }

        return subscribedAssetCount == 0
            ? new ReadinessCheckProbe(
                ReadinessCheckStatus.Ready,
                "MarketData WebSocket clients",
                "CLOB WebSocket is intentionally idle with no subscribed assets.",
                now,
                Evidence: evidence)
            : new ReadinessCheckProbe(
                ReadinessCheckStatus.Blocked,
                "MarketData WebSocket clients",
                "CLOB WebSocket is disconnected while assets are subscribed.",
                now,
                "Reconnect CLOB WebSocket clients or remove stale subscriptions before Live arming.",
                evidence);
    }

    private ReadinessCheckProbe ProbePaperDefault(DateTimeOffset now)
    {
        var mode = configuration["Execution:Mode"] ?? "Paper";
        return string.Equals(mode, "Paper", StringComparison.OrdinalIgnoreCase)
            ? Ready("ExecutionOptions", "Execution mode is Paper.", now, ("mode", mode))
            : Unhealthy(
                "ExecutionOptions",
                $"Execution mode is {mode}; first run is not on the Paper default path.",
                now,
                "Set Execution:Mode=Paper for first run.");
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
                "Live arming service is unavailable in this API process.",
                now,
                "Enable AutotradeApi:EnableModules before probing or changing Live arming state.");
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
                "Fix live arming service registration before enabling LiveServices commands.");
        }
    }

    private ReadinessCheckProbe ProbeRiskLimits(DateTimeOffset now)
    {
        var maxOpenOrders = configuration.GetValue("Risk:MaxOpenOrders", 20);
        var maxCapitalPerMarket = configuration.GetValue("Risk:MaxCapitalPerMarket", 0.05m);
        var maxTotalCapital = configuration.GetValue("Risk:MaxTotalCapitalUtilization", 0.50m);

        return maxOpenOrders > 0 && maxCapitalPerMarket > 0 && maxTotalCapital > 0
            ? Ready(
                "RiskOptions",
                "Core risk limits are configured.",
                now,
                ("maxOpenOrders", maxOpenOrders.ToString()),
                ("maxCapitalPerMarket", maxCapitalPerMarket.ToString("0.####")),
                ("maxTotalCapitalUtilization", maxTotalCapital.ToString("0.####")))
            : Unhealthy(
                "RiskOptions",
                "One or more core risk limits are missing or non-positive.",
                now,
                "Configure Risk:MaxOpenOrders, Risk:MaxCapitalPerMarket, and Risk:MaxTotalCapitalUtilization.");
    }

    private ReadinessCheckProbe ProbeBackgroundJobs(DateTimeOffset now)
    {
        var enabled = configuration.GetValue("BackgroundJobs:Enabled", true);
        return enabled
            ? Ready("BackgroundJobs", "Background jobs are enabled for the host.", now, ("enabled", "true"))
            : Unhealthy("BackgroundJobs", "Background jobs are disabled.", now, "Set BackgroundJobs:Enabled=true for automated operation.");
    }

    private ReadinessCheckProbe ProbeAccountSync(DateTimeOffset now)
    {
        var enabled = configuration.GetValue("AccountSync:Enabled", true);
        return enabled
            ? Ready("Trading account sync", "Account sync is enabled.", now, ("enabled", "true"))
            : Blocked("Trading account sync", "Account sync is disabled.", now, "Set AccountSync:Enabled=true before Live arming.");
    }

    private ReadinessCheckProbe ProbeCompliance(DateTimeOffset now)
    {
        var enabled = configuration.GetValue("Compliance:Enabled", true);
        var geoKycAllowed = configuration.GetValue("Compliance:GeoKycAllowed", false);
        if (!enabled)
        {
            return Blocked("ComplianceGuard", "Compliance guard is disabled.", now, "Enable Compliance:Enabled before Live arming.");
        }

        return geoKycAllowed
            ? Ready("ComplianceGuard", "Compliance prerequisites are confirmed.", now, ("geoKycAllowed", "true"))
            : Blocked(
                "ComplianceGuard",
                "Geo/KYC compliance confirmation is missing.",
                now,
                "Set Compliance:GeoKycAllowed only after operator verification.");
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

    private ReadinessCheckProbe ProbeModuleInventory(DateTimeOffset now)
    {
        var evidence = BuildModuleInventoryEvidence(moduleInventory);
        if (!moduleInventory.Enabled)
        {
            return new ReadinessCheckProbe(
                ReadinessCheckStatus.Skipped,
                "Autotrade.Hosting",
                "Autotrade modules are not registered in this API process.",
                now,
                "Set AutotradeApi:EnableModules=true to load bounded-context modules.",
                evidence);
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
            Evidence: ToEvidence(evidence));

    private static ReadinessCheckProbe Skipped(
        string source,
        string summary,
        DateTimeOffset now,
        string remediationHint)
        => new(
            ReadinessCheckStatus.Skipped,
            source,
            summary,
            now,
            remediationHint);

    private static ReadinessCheckProbe Unhealthy(
        string source,
        string summary,
        DateTimeOffset now,
        string? remediationHint = null,
        IReadOnlyDictionary<string, string>? evidence = null)
        => new(
            ReadinessCheckStatus.Unhealthy,
            source,
            summary,
            now,
            remediationHint,
            evidence);

    private static ReadinessCheckProbe Blocked(
        string source,
        string summary,
        DateTimeOffset now,
        string remediationHint)
        => new(
            ReadinessCheckStatus.Blocked,
            source,
            summary,
            now,
            remediationHint);

    private static IReadOnlyDictionary<string, string> ToEvidence(params (string Key, string Value)[] evidence)
        => evidence.Length == 0
            ? new Dictionary<string, string>()
            : evidence.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
}
