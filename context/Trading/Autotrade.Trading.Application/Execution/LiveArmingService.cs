using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.Application.Readiness;
using Autotrade.Application.Security;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Execution;

public interface ILiveArmingStateStore
{
    Task<LiveArmingEvidence?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LiveArmingEvidence evidence, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class FileLiveArmingStateStore(
    IOptions<LiveArmingOptions> options,
    ILogger<FileLiveArmingStateStore> logger) : ILiveArmingStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<LiveArmingEvidence?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolveEvidencePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<LiveArmingEvidence>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Live arming evidence file is not valid JSON: {Path}", path);
            return null;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Unable to read live arming evidence file: {Path}", path);
            return null;
        }
    }

    public async Task SaveAsync(LiveArmingEvidence evidence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var path = ResolveEvidencePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, evidence, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolveEvidencePath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string ResolveEvidencePath()
    {
        var configured = options.Value.EvidenceFilePath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = string.IsNullOrWhiteSpace(localAppData)
            ? AppContext.BaseDirectory
            : Path.Combine(localAppData, "Autotrade");

        return Path.Combine(baseDirectory, "live-arming-evidence.json");
    }
}

public sealed class LiveArmingService(
    IServiceProvider serviceProvider,
    ILiveArmingStateStore stateStore,
    IComplianceGuard complianceGuard,
    IAccountSyncService accountSyncService,
    IRiskManager riskManager,
    IConfiguration configuration,
    IOptions<ExecutionOptions> executionOptions,
    IOptions<LiveArmingOptions> armingOptions,
    IOptions<RiskOptions> riskOptions,
    IOptions<ComplianceOptions> complianceOptions,
    IOptions<AccountSyncOptions> accountSyncOptions) : ILiveArmingService
{
    private const string SelfCheckId = "execution.live_armed";

    public async Task<LiveArmingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = await stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var currentFingerprint = BuildConfigFingerprint();
        var configVersion = ResolveConfigVersion();

        if (evidence is null)
        {
            return NotArmed("Live arming evidence has not been recorded.", configVersion, now);
        }

        if (evidence.ExpiresAtUtc <= now)
        {
            return new LiveArmingStatus(
                false,
                "Expired",
                "Live arming evidence has expired.",
                configVersion,
                now,
                evidence,
                ["Live arming evidence has expired."]);
        }

        if (!string.Equals(evidence.ConfigFingerprint, currentFingerprint, StringComparison.Ordinal))
        {
            return new LiveArmingStatus(
                false,
                "ConfigChanged",
                "Critical configuration changed after Live arming.",
                configVersion,
                now,
                evidence,
                ["Critical configuration changed after Live arming."]);
        }

        return new LiveArmingStatus(
            true,
            "Armed",
            $"Live trading is armed until {evidence.ExpiresAtUtc:O}.",
            configVersion,
            now,
            evidence,
            []);
    }

    public async Task<LiveArmingResult> ArmAsync(
        LiveArmingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!HasRequiredConfirmation(request.ConfirmationText, armingOptions.Value.RequiredArmConfirmationText))
        {
            var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return new LiveArmingResult(
                false,
                "ConfirmationRequired",
                $"Type {armingOptions.Value.RequiredArmConfirmationText} to arm Live trading.",
                status);
        }

        var readiness = await EvaluateReadinessAsync(cancellationToken).ConfigureAwait(false);
        var blockers = readiness.Blockers.ToList();
        AddConfigurationBlockers(blockers);
        AddComplianceBlockers(blockers);

        if (blockers.Count == 0)
        {
            await RefreshAndValidateAccountSyncAsync(blockers, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            AddAccountSyncConfigurationBlockers(blockers);
        }

        var riskSummary = BuildRiskSummary();
        AddRiskBlockers(blockers, riskSummary);

        if (blockers.Count > 0)
        {
            var status = new LiveArmingStatus(
                false,
                "Blocked",
                "Live arming prerequisites are not satisfied.",
                ResolveConfigVersion(),
                DateTimeOffset.UtcNow,
                await stateStore.LoadAsync(cancellationToken).ConfigureAwait(false),
                blockers);

            return new LiveArmingResult(false, "Blocked", string.Join("; ", blockers), status);
        }

        var now = DateTimeOffset.UtcNow;
        var evidence = new LiveArmingEvidence(
            Guid.NewGuid().ToString("N"),
            ResolveActor(request.Actor),
            request.Reason,
            now,
            now.AddMinutes(Math.Max(1, armingOptions.Value.ExpirationMinutes)),
            ResolveConfigVersion(),
            BuildConfigFingerprint(),
            riskSummary,
            readiness.PassedCheckIds);

        await stateStore.SaveAsync(evidence, cancellationToken).ConfigureAwait(false);
        var armedStatus = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return new LiveArmingResult(true, "Accepted", "Live trading armed.", armedStatus);
    }

    public async Task<LiveArmingResult> DisarmAsync(
        LiveDisarmingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!HasRequiredConfirmation(request.ConfirmationText, armingOptions.Value.RequiredDisarmConfirmationText))
        {
            var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return new LiveArmingResult(
                false,
                "ConfirmationRequired",
                $"Type {armingOptions.Value.RequiredDisarmConfirmationText} to disarm Live trading.",
                status);
        }

        await stateStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        var currentStatus = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return new LiveArmingResult(true, "Accepted", "Live trading disarmed.", currentStatus);
    }

    public Task<LiveArmingStatus> RequireArmedAsync(CancellationToken cancellationToken = default)
        => GetStatusAsync(cancellationToken);

    private async Task<(IReadOnlyList<string> Blockers, IReadOnlyList<string> PassedCheckIds)> EvaluateReadinessAsync(
        CancellationToken cancellationToken)
    {
        var reportService = serviceProvider.GetService<IReadinessReportService>();
        if (reportService is null)
        {
            return (["Readiness report service is unavailable."], []);
        }

        var report = await reportService.GetReportAsync(cancellationToken).ConfigureAwait(false);
        var liveRequiredCheckIds = FirstRunReadinessContract.Create()
            .Capabilities
            .Single(capability => capability.Capability == ReadinessCapability.LiveTrading)
            .RequiredCheckIds
            .Where(id => !string.Equals(id, SelfCheckId, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var blockers = report.Checks
            .Where(check => liveRequiredCheckIds.Contains(check.Id)
                            && check.Status != ReadinessCheckStatus.Ready)
            .Select(check => $"{check.Id}: {check.Status} - {check.Summary}")
            .ToArray();

        var passed = report.Checks
            .Where(check => liveRequiredCheckIds.Contains(check.Id)
                            && check.Status == ReadinessCheckStatus.Ready)
            .Select(check => check.Id)
            .ToArray();

        return (blockers, passed);
    }

    private void AddConfigurationBlockers(ICollection<string> blockers)
    {
        if (executionOptions.Value.Mode != ExecutionMode.Live)
        {
            blockers.Add("Execution.Mode must be Live before Live arming.");
        }
    }

    private void AddComplianceBlockers(ICollection<string> blockers)
    {
        var check = complianceGuard.CheckConfiguration(ExecutionMode.Live);
        if (!check.Enabled)
        {
            blockers.Add("Compliance guard is disabled.");
            return;
        }

        foreach (var issue in check.Issues.Where(issue => issue.Severity == ComplianceSeverity.Error || issue.BlocksLiveOrders))
        {
            blockers.Add($"{issue.Code}: {issue.Message}");
        }
    }

    private void AddAccountSyncConfigurationBlockers(ICollection<string> blockers)
    {
        if (!accountSyncOptions.Value.Enabled)
        {
            blockers.Add("AccountSync.Enabled must be true before Live arming.");
        }
    }

    private async Task RefreshAndValidateAccountSyncAsync(
        ICollection<string> blockers,
        CancellationToken cancellationToken)
    {
        if (!accountSyncOptions.Value.Enabled)
        {
            blockers.Add("AccountSync.Enabled must be true before Live arming.");
            return;
        }

        var bootstrapper = serviceProvider.GetService<TradingAccountBootstrapper>();
        try
        {
            if (bootstrapper is not null)
            {
                await bootstrapper.BootstrapAsync(cancellationToken).ConfigureAwait(false);
            }

            var sync = await accountSyncService.SyncAllAsync(cancellationToken).ConfigureAwait(false);
            if (!sync.IsSuccess)
            {
                blockers.Add(
                    $"Account sync failed before Live arming: {SecretRedactor.Redact(sync.ErrorMessage ?? "unknown error")}.");
                return;
            }

            if (sync.HasDrift)
            {
                blockers.Add(
                    "Account sync detected external account drift before Live arming: " +
                    $"PositionDrifts={sync.Positions?.DriftCount ?? 0}, " +
                    $"UnknownOrders={sync.OpenOrders?.UnknownExternalOrderCount ?? 0}, " +
                    $"MissingOrders={sync.OpenOrders?.MissingInternalOrderCount ?? 0}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            blockers.Add($"Account sync failed before Live arming: {SecretRedactor.Redact(ex.Message)}.");
            return;
        }

        var lastSync = accountSyncService.LastSyncTime;
        if (lastSync is null)
        {
            blockers.Add("Account sync has not completed in this process.");
            return;
        }

        var maxAge = TimeSpan.FromSeconds(Math.Max(1, armingOptions.Value.MaxAccountSyncAgeSeconds));
        if (DateTimeOffset.UtcNow - lastSync.Value > maxAge)
        {
            blockers.Add($"Account sync is stale. Last sync: {lastSync.Value:O}.");
        }
    }

    private void AddRiskBlockers(ICollection<string> blockers, LiveArmingRiskSummary riskSummary)
    {
        var risk = riskOptions.Value;
        if (!risk.KillSwitchEnabled)
        {
            blockers.Add("Risk.KillSwitchEnabled must be true before Live arming.");
        }

        if (riskSummary.KillSwitchActive)
        {
            blockers.Add("Global kill switch is active.");
        }

        if (risk.MaxOpenOrders <= 0
            || risk.MaxCapitalPerMarket <= 0
            || risk.MaxTotalCapitalUtilization <= 0)
        {
            blockers.Add("Core risk limits must be positive before Live arming.");
        }

        if (riskSummary.TotalCapital <= 0 || riskSummary.AvailableCapital <= 0)
        {
            blockers.Add("Risk capital snapshot must have positive total and available capital.");
        }
    }

    private LiveArmingRiskSummary BuildRiskSummary()
    {
        var snapshot = riskManager.GetStateSnapshot();
        return new LiveArmingRiskSummary(
            snapshot.TotalCapital,
            snapshot.AvailableCapital,
            snapshot.CapitalUtilizationPct,
            snapshot.TotalOpenNotional,
            snapshot.TotalOpenOrders,
            snapshot.UnhedgedExposures.Count,
            riskManager.IsKillSwitchActive);
    }

    private string ResolveConfigVersion()
        => string.IsNullOrWhiteSpace(armingOptions.Value.ConfigVersion)
            ? "local"
            : armingOptions.Value.ConfigVersion.Trim();

    private string BuildConfigFingerprint()
    {
        var critical = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AccountSync.DetectExternalOpenOrders"] = accountSyncOptions.Value.DetectExternalOpenOrders.ToString(),
            ["AccountSync.Enabled"] = accountSyncOptions.Value.Enabled.ToString(),
            ["AccountSync.FailFastOnStartupDrift"] = accountSyncOptions.Value.FailFastOnStartupDrift.ToString(),
            ["AccountSync.TriggerKillSwitchOnDrift"] = accountSyncOptions.Value.TriggerKillSwitchOnDrift.ToString(),
            ["Compliance.AllowUnsafeLiveParameters"] = complianceOptions.Value.AllowUnsafeLiveParameters.ToString(),
            ["Compliance.Enabled"] = complianceOptions.Value.Enabled.ToString(),
            ["Compliance.GeoKycAllowed"] = complianceOptions.Value.GeoKycAllowed.ToString(),
            ["Compliance.MaxLiveCapitalPerMarket"] = complianceOptions.Value.MaxLiveCapitalPerMarket.ToString("0.########"),
            ["Compliance.MaxLiveCapitalPerStrategy"] = complianceOptions.Value.MaxLiveCapitalPerStrategy.ToString("0.########"),
            ["Compliance.MaxLiveOpenOrders"] = complianceOptions.Value.MaxLiveOpenOrders.ToString(),
            ["Compliance.MaxLiveOpenOrdersPerMarket"] = complianceOptions.Value.MaxLiveOpenOrdersPerMarket.ToString(),
            ["Compliance.MaxLiveOrdersPerCycle"] = complianceOptions.Value.MaxLiveOrdersPerCycle.ToString(),
            ["Compliance.MaxLiveTotalCapitalUtilization"] = complianceOptions.Value.MaxLiveTotalCapitalUtilization.ToString("0.########"),
            ["Compliance.MinLiveEvaluationIntervalSeconds"] = complianceOptions.Value.MinLiveEvaluationIntervalSeconds.ToString(),
            ["Compliance.MinLiveReconciliationIntervalSeconds"] = complianceOptions.Value.MinLiveReconciliationIntervalSeconds.ToString(),
            ["Execution.MaxBatchOrderSize"] = executionOptions.Value.MaxBatchOrderSize.ToString(),
            ["Execution.MaxOpenOrdersPerMarket"] = executionOptions.Value.MaxOpenOrdersPerMarket.ToString(),
            ["Execution.Mode"] = executionOptions.Value.Mode.ToString(),
            ["Execution.UseBatchOrders"] = executionOptions.Value.UseBatchOrders.ToString(),
            ["LiveArming.ConfigVersion"] = ResolveConfigVersion(),
            ["LiveArming.ExpirationMinutes"] = armingOptions.Value.ExpirationMinutes.ToString(),
            ["Polymarket.Clob.Address"] = configuration["Polymarket:Clob:Address"],
            ["Polymarket.Clob.ApiKeyHash"] = HashOptionalSecret(configuration["Polymarket:Clob:ApiKey"]),
            ["Polymarket.Clob.ApiSecretHash"] = HashOptionalSecret(configuration["Polymarket:Clob:ApiSecret"]),
            ["Polymarket.Clob.PrivateKeyHash"] = HashOptionalSecret(configuration["Polymarket:Clob:PrivateKey"]),
            ["Risk.DailyLossLimitPct"] = riskOptions.Value.DailyLossLimitPct?.ToString("0.########"),
            ["Risk.KillSwitchEnabled"] = riskOptions.Value.KillSwitchEnabled.ToString(),
            ["Risk.MaxCapitalPerMarket"] = riskOptions.Value.MaxCapitalPerMarket.ToString("0.########"),
            ["Risk.MaxCapitalPerStrategy"] = riskOptions.Value.MaxCapitalPerStrategy.ToString("0.########"),
            ["Risk.MaxConcurrentMarketsPerStrategy"] = riskOptions.Value.MaxConcurrentMarketsPerStrategy.ToString(),
            ["Risk.MaxConsecutiveOrderErrors"] = riskOptions.Value.MaxConsecutiveOrderErrors?.ToString(),
            ["Risk.MaxOpenOrders"] = riskOptions.Value.MaxOpenOrders.ToString(),
            ["Risk.MaxTotalCapitalUtilization"] = riskOptions.Value.MaxTotalCapitalUtilization.ToString("0.########"),
            ["Risk.MaxUnhedgedCapitalPerMarket"] = riskOptions.Value.MaxUnhedgedCapitalPerMarket.ToString("0.########")
        };

        var json = JsonSerializer.Serialize(critical);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static string HashOptionalSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "missing";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes);
    }

    private static LiveArmingStatus NotArmed(string reason, string configVersion, DateTimeOffset now)
    {
        return new LiveArmingStatus(
            false,
            "NotArmed",
            reason,
            configVersion,
            now,
            null,
            [reason]);
    }

    private static bool HasRequiredConfirmation(string? actual, string expected)
        => string.Equals(actual?.Trim(), expected.Trim(), StringComparison.Ordinal);

    private static string ResolveActor(string actor)
        => string.IsNullOrWhiteSpace(actor)
            ? Environment.UserName
            : actor.Trim();
}
