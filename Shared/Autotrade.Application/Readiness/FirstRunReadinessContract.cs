namespace Autotrade.Application.Readiness;

public enum ReadinessCapability
{
    PaperTrading,
    LiveTrading
}

public enum ReadinessCheckCategory
{
    Runtime,
    Database,
    Migrations,
    Api,
    MarketData,
    WebSocket,
    BackgroundJobs,
    AccountSync,
    Compliance,
    ExecutionMode,
    RiskLimits,
    Credentials
}

public enum ReadinessCheckRequirement
{
    Required,
    Optional,
    LiveOnly
}

public enum ReadinessCheckStatus
{
    Ready,
    Degraded,
    Unhealthy,
    Blocked,
    Skipped
}

public enum ReadinessOverallStatus
{
    Ready,
    Degraded,
    Blocked
}

public sealed record FirstRunWizardContract(
    string ContractVersion,
    string Product,
    IReadOnlyList<ReadinessCheckDefinition> Checks,
    IReadOnlyList<ReadinessCapabilityDefinition> Capabilities);

public sealed record ReadinessCheckDefinition(
    string Id,
    string DisplayName,
    ReadinessCheckCategory Category,
    ReadinessCheckRequirement Requirement,
    IReadOnlyList<ReadinessCapability> RequiredFor,
    string Description,
    string ReadyCriteria,
    string RemediationHint,
    IReadOnlyList<string> Sources);

public sealed record ReadinessCapabilityDefinition(
    ReadinessCapability Capability,
    string DisplayName,
    string Description,
    IReadOnlyList<string> RequiredCheckIds);

public sealed record ReadinessReport(
    string ContractVersion,
    DateTimeOffset CheckedAtUtc,
    ReadinessOverallStatus Status,
    IReadOnlyList<ReadinessCheckResult> Checks,
    IReadOnlyList<ReadinessCapabilityResult> Capabilities);

public sealed record ReadinessCheckResult(
    string Id,
    ReadinessCheckCategory Category,
    ReadinessCheckRequirement Requirement,
    ReadinessCheckStatus Status,
    string Source,
    DateTimeOffset LastCheckedAtUtc,
    string Summary,
    string RemediationHint,
    IReadOnlyDictionary<string, string> Evidence);

public sealed record ReadinessCapabilityResult(
    ReadinessCapability Capability,
    ReadinessOverallStatus Status,
    IReadOnlyList<string> BlockingCheckIds,
    string Summary);

public sealed record ReadinessCheckProbe(
    ReadinessCheckStatus Status,
    string Source,
    string Summary,
    DateTimeOffset LastCheckedAtUtc,
    string? RemediationHint = null,
    IReadOnlyDictionary<string, string>? Evidence = null);

public interface IReadinessProbeCollector
{
    Task<IReadOnlyDictionary<string, ReadinessCheckProbe>> CollectAsync(
        CancellationToken cancellationToken = default);
}

public interface IReadinessReportService
{
    Task<ReadinessReport> GetReportAsync(CancellationToken cancellationToken = default);
}

public static class FirstRunReadinessContract
{
    public const string ContractVersion = "2026-05-12.builder-attribution.v1";
    public const string Product = "Autotrade first-run readiness";

    public static FirstRunWizardContract Create()
    {
        var checks = CreateChecks();
        return new FirstRunWizardContract(
            ContractVersion,
            Product,
            checks,
            CreateCapabilities(checks));
    }

    private static IReadOnlyList<ReadinessCheckDefinition> CreateChecks()
    {
        return
        [
            Required(
                "runtime.configuration.loaded",
                "Configuration loaded",
                ReadinessCheckCategory.Runtime,
                "The host can load base configuration, local overrides, environment variables, and user secrets.",
                "Effective configuration is available without parsing errors.",
                "Fix malformed JSON, missing files, or invalid environment overrides before starting Autotrade.",
                "Configuration"),
            Required(
                "runtime.modules.inventory",
                "Module inventory",
                ReadinessCheckCategory.Runtime,
                "The host exposes which bounded-context modules are loaded and whether persistence and background jobs are active.",
                "The module inventory lists every loaded bounded context and its hosting capabilities.",
                "Load Autotrade modules through the shared hosting registration before starting automation.",
                "Autotrade.Hosting"),
            Required(
                "database.connection",
                "Database connection",
                ReadinessCheckCategory.Database,
                "Trading, MarketData, and Strategy contexts can reach PostgreSQL.",
                "All bounded-context DbContext instances can connect to the configured database.",
                "Start PostgreSQL or update ConnectionStrings:AutotradeDatabase.",
                "EF Core"),
            Required(
                "database.migrations.current",
                "Database migrations current",
                ReadinessCheckCategory.Migrations,
                "The configured database schema matches the repository migrations.",
                "No pending migrations remain for Trading, MarketData, or Strategy contexts.",
                "Run the migration path before starting workers or command surfaces.",
                "EF Core"),
            Required(
                "api.control_room.reachable",
                "Control room API reachable",
                ReadinessCheckCategory.Api,
                "The local API health endpoint and control-room snapshot endpoint are reachable.",
                "The API responds locally and reports a readable control-room snapshot.",
                "Start Autotrade.Api locally and keep control-room access loopback-only.",
                "Autotrade.Api"),
            Optional(
                "market_data.public_api.reachable",
                "Public market data reachable",
                ReadinessCheckCategory.MarketData,
                "The Polymarket Gamma or CLOB public endpoints can be reached for discovery and order-book data.",
                "A public market-data probe succeeds within the configured timeout.",
                "Check network connectivity or keep operating from the local catalog until public data recovers.",
                "Polymarket public API"),
            LiveOnly(
                "market_data.websocket.healthy",
                "Market WebSocket healthy",
                ReadinessCheckCategory.WebSocket,
                "The market data WebSocket clients are connected when subscriptions are required.",
                "Required WebSocket clients are connected or intentionally idle with no subscriptions.",
                "Check WebSocket credentials, network access, and subscription state.",
                "MarketData WebSocket clients"),
            Required(
                "background_jobs.heartbeats.fresh",
                "Background jobs fresh",
                ReadinessCheckCategory.BackgroundJobs,
                "Background job scheduling and worker heartbeats are recent enough for automation.",
                "Required recurring jobs are registered and no required heartbeat is stale.",
                "Enable BackgroundJobs or inspect the Hangfire recurring job registration.",
                "BackgroundJobs"),
            LiveOnly(
                "account_sync.configured",
                "Account sync configured",
                ReadinessCheckCategory.AccountSync,
                "Account sync is configured to detect exchange drift before Live order placement.",
                "Account sync is enabled and its latest status is successful or intentionally blocked.",
                "Configure account sync and resolve external order or position drift.",
                "Trading account sync"),
            LiveOnly(
                "compliance.geo_kyc.confirmed",
                "Compliance confirmed",
                ReadinessCheckCategory.Compliance,
                "The operator explicitly confirms the geo, KYC, and compliance prerequisites for Live trading.",
                "Compliance guard passes all Live-blocking checks.",
                "Set the compliance confirmation only after the operator has verified eligibility.",
                "ComplianceGuard"),
            Required(
                "execution.mode.paper_default",
                "Paper default path",
                ReadinessCheckCategory.ExecutionMode,
                "Fresh checkout remains safe by default and can run in Paper mode without Live prerequisites.",
                "Execution mode is Paper unless Live is explicitly armed later.",
                "Set Execution:Mode=Paper for first run or when Live prerequisites are missing.",
                "ExecutionOptions",
                [ReadinessCapability.PaperTrading]),
            LiveOnly(
                "execution.live_armed",
                "Live explicitly armed",
                ReadinessCheckCategory.ExecutionMode,
                "Live trading requires an explicit arming action and evidence record.",
                "A current Live arming evidence record exists and matches the effective configuration.",
                "Complete the Live arming checklist from the CLI or control room.",
                "Live arming gate"),
            Required(
                "risk.limits.configured",
                "Risk limits configured",
                ReadinessCheckCategory.RiskLimits,
                "Risk limits are present and conservative enough for the selected execution mode.",
                "Capital, order-count, and per-market limits are configured and pass compliance constraints.",
                "Configure Risk and Execution limits before starting strategies.",
                "RiskOptions"),
            LiveOnly(
                "credentials.exchange.present",
                "Exchange authentication present",
                ReadinessCheckCategory.Credentials,
                "Exchange authentication material is available to the server process without exposing values.",
                "Required Live authentication settings are present in user secrets or environment variables.",
                "Configure exchange authentication outside the repository and never expose values to the browser.",
                "Server configuration"),
            Optional(
                "polymarket.builder_attribution.ready",
                "Builder attribution configured",
                ReadinessCheckCategory.Credentials,
                "Polymarket builder metadata is configured for attributed order-flow evidence.",
                "A non-zero bytes32 builder code is configured, or attribution is explicitly disabled for the current run.",
                "Set Polymarket:Clob:BuilderCode to a bytes32 hex builder code before claiming builder-attributed order flow.",
                "Server configuration")
        ];
    }

    private static IReadOnlyList<ReadinessCapabilityDefinition> CreateCapabilities(
        IReadOnlyList<ReadinessCheckDefinition> checks)
    {
        return
        [
            CreateCapability(
                checks,
                ReadinessCapability.PaperTrading,
                "Paper trading",
                "Safe first-run mode for strategy evaluation without exchange order placement."),
            CreateCapability(
                checks,
                ReadinessCapability.LiveTrading,
                "Live trading",
                "Armed mode that can place real orders only after all Live-blocking checks pass.")
        ];
    }

    private static ReadinessCapabilityDefinition CreateCapability(
        IReadOnlyList<ReadinessCheckDefinition> checks,
        ReadinessCapability capability,
        string displayName,
        string description)
    {
        var requiredCheckIds = checks
            .Where(check => check.RequiredFor.Contains(capability))
            .Select(check => check.Id)
            .ToArray();

        return new ReadinessCapabilityDefinition(
            capability,
            displayName,
            description,
            requiredCheckIds);
    }

    private static ReadinessCheckDefinition Required(
        string id,
        string displayName,
        ReadinessCheckCategory category,
        string description,
        string readyCriteria,
        string remediationHint,
        string source,
        IReadOnlyList<ReadinessCapability>? requiredFor = null)
    {
        return new ReadinessCheckDefinition(
            id,
            displayName,
            category,
            ReadinessCheckRequirement.Required,
            requiredFor ?? [ReadinessCapability.PaperTrading, ReadinessCapability.LiveTrading],
            description,
            readyCriteria,
            remediationHint,
            [source]);
    }

    private static ReadinessCheckDefinition Optional(
        string id,
        string displayName,
        ReadinessCheckCategory category,
        string description,
        string readyCriteria,
        string remediationHint,
        string source)
    {
        return new ReadinessCheckDefinition(
            id,
            displayName,
            category,
            ReadinessCheckRequirement.Optional,
            [],
            description,
            readyCriteria,
            remediationHint,
            [source]);
    }

    private static ReadinessCheckDefinition LiveOnly(
        string id,
        string displayName,
        ReadinessCheckCategory category,
        string description,
        string readyCriteria,
        string remediationHint,
        string source)
    {
        return new ReadinessCheckDefinition(
            id,
            displayName,
            category,
            ReadinessCheckRequirement.LiveOnly,
            [ReadinessCapability.LiveTrading],
            description,
            readyCriteria,
            remediationHint,
            [source]);
    }
}

public sealed class ReadinessReportService(IReadinessProbeCollector probeCollector) : IReadinessReportService
{
    public async Task<ReadinessReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probeCollector);

        var checkedAtUtc = DateTimeOffset.UtcNow;
        var probes = await probeCollector.CollectAsync(cancellationToken).ConfigureAwait(false);
        return ReadinessReportFactory.Create(checkedAtUtc, probes);
    }
}

public static class ReadinessReportFactory
{
    public static ReadinessReport Create(
        DateTimeOffset checkedAtUtc,
        IReadOnlyDictionary<string, ReadinessCheckProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);

        var contract = FirstRunReadinessContract.Create();
        var checks = contract.Checks
            .Select(definition => CreateCheckResult(definition, probes, checkedAtUtc))
            .ToArray();
        var capabilities = contract.Capabilities
            .Select(capability => CreateCapabilityResult(capability, checks))
            .ToArray();

        return new ReadinessReport(
            contract.ContractVersion,
            checkedAtUtc,
            ResolveOverallStatus(capabilities, checks),
            checks,
            capabilities);
    }

    private static ReadinessCheckResult CreateCheckResult(
        ReadinessCheckDefinition definition,
        IReadOnlyDictionary<string, ReadinessCheckProbe> probes,
        DateTimeOffset checkedAtUtc)
    {
        if (probes.TryGetValue(definition.Id, out var probe))
        {
            return new ReadinessCheckResult(
                definition.Id,
                definition.Category,
                definition.Requirement,
                probe.Status,
                probe.Source,
                probe.LastCheckedAtUtc,
                probe.Summary,
                string.IsNullOrWhiteSpace(probe.RemediationHint)
                    ? definition.RemediationHint
                    : probe.RemediationHint,
                probe.Evidence ?? new Dictionary<string, string>());
        }

        return new ReadinessCheckResult(
            definition.Id,
            definition.Category,
            definition.Requirement,
            ReadinessCheckStatus.Skipped,
            "not-collected",
            checkedAtUtc,
            "This readiness check has not been collected by the current surface.",
            definition.RemediationHint,
            new Dictionary<string, string>());
    }

    private static ReadinessCapabilityResult CreateCapabilityResult(
        ReadinessCapabilityDefinition capability,
        IReadOnlyList<ReadinessCheckResult> checks)
    {
        var required = checks
            .Where(check => capability.RequiredCheckIds.Contains(check.Id, StringComparer.Ordinal))
            .ToArray();
        var blocking = required
            .Where(IsBlocking)
            .Select(check => check.Id)
            .ToArray();

        if (blocking.Length > 0)
        {
            return new ReadinessCapabilityResult(
                capability.Capability,
                ReadinessOverallStatus.Blocked,
                blocking,
                $"{capability.DisplayName} is blocked by {blocking.Length} readiness check(s).");
        }

        if (required.Any(check => check.Status == ReadinessCheckStatus.Degraded))
        {
            return new ReadinessCapabilityResult(
                capability.Capability,
                ReadinessOverallStatus.Degraded,
                [],
                $"{capability.DisplayName} is usable with degraded readiness.");
        }

        return new ReadinessCapabilityResult(
            capability.Capability,
            ReadinessOverallStatus.Ready,
            [],
            $"{capability.DisplayName} is ready.");
    }

    private static ReadinessOverallStatus ResolveOverallStatus(
        IReadOnlyList<ReadinessCapabilityResult> capabilities,
        IReadOnlyList<ReadinessCheckResult> checks)
    {
        var paper = capabilities.First(capability => capability.Capability == ReadinessCapability.PaperTrading);
        if (paper.Status == ReadinessOverallStatus.Blocked)
        {
            return ReadinessOverallStatus.Blocked;
        }

        return capabilities.Any(capability => capability.Status != ReadinessOverallStatus.Ready)
               || checks.Any(check => check.Requirement == ReadinessCheckRequirement.Optional
                                      && check.Status is ReadinessCheckStatus.Degraded or ReadinessCheckStatus.Unhealthy)
            ? ReadinessOverallStatus.Degraded
            : ReadinessOverallStatus.Ready;
    }

    private static bool IsBlocking(ReadinessCheckResult check)
    {
        return check.Status is ReadinessCheckStatus.Blocked
            or ReadinessCheckStatus.Unhealthy
            or ReadinessCheckStatus.Skipped;
    }
}
