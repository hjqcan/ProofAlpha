using Autotrade.Application.Readiness;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Execution;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Autotrade.Trading.Tests.Execution;

public sealed class LiveArmingServiceTests
{
    [Fact]
    public async Task ArmAsync_WithPassingPrerequisites_WritesEvidenceAndStatusIsArmed()
    {
        var evidencePath = NewEvidencePath();
        try
        {
            var service = NewService(evidencePath);

            var result = await service.ArmAsync(new LiveArmingRequest("operator", "regression", "ARM LIVE"));
            var status = await service.GetStatusAsync();

            Assert.True(result.Accepted);
            Assert.Equal("Accepted", result.Status);
            Assert.True(File.Exists(evidencePath));
            Assert.True(status.IsArmed);
            Assert.Equal("Armed", status.State);
            Assert.NotNull(status.Evidence);
            Assert.Equal("operator", status.Evidence!.Operator);
            Assert.Equal("regression", status.Evidence.Reason);
            Assert.Equal("test-config", status.Evidence.ConfigVersion);
            Assert.DoesNotContain("execution.live_armed", status.Evidence.PassedReadinessCheckIds);
            Assert.Contains("risk.limits.configured", status.Evidence.PassedReadinessCheckIds);
        }
        finally
        {
            DeleteIfExists(evidencePath);
        }
    }

    [Fact]
    public async Task ArmAsync_WithWrongConfirmation_DoesNotWriteEvidence()
    {
        var evidencePath = NewEvidencePath();
        try
        {
            var service = NewService(evidencePath);

            var result = await service.ArmAsync(new LiveArmingRequest("operator", "regression", "confirm"));

            Assert.False(result.Accepted);
            Assert.Equal("ConfirmationRequired", result.Status);
            Assert.False(File.Exists(evidencePath));
        }
        finally
        {
            DeleteIfExists(evidencePath);
        }
    }

    [Fact]
    public async Task ArmAsync_RefreshesAccountSyncBeforeWritingEvidence()
    {
        var evidencePath = NewEvidencePath();
        try
        {
            var accountSync = new RecordingAccountSyncService();
            var service = NewService(evidencePath, accountSyncService: accountSync);

            var result = await service.ArmAsync(new LiveArmingRequest("operator", "fresh-sync", "ARM LIVE"));

            Assert.True(result.Accepted);
            Assert.Equal(1, accountSync.SyncAllCalls);
            Assert.NotNull(accountSync.LastSyncTime);
            Assert.True(File.Exists(evidencePath));
        }
        finally
        {
            DeleteIfExists(evidencePath);
        }
    }

    [Fact]
    public async Task ArmAsync_WhenAccountSyncDetectsDrift_DoesNotWriteEvidence()
    {
        var evidencePath = NewEvidencePath();
        try
        {
            var accountSync = new RecordingAccountSyncService
            {
                SyncResult = new FullSyncResult(
                    true,
                    new BalanceSyncResult(true, 100m, 100m),
                    new PositionsSyncResult(
                        true,
                        1,
                        1,
                        [new PositionDrift("market", "Yes", 0m, 1m, 1m, null, 0.50m, null, "UnknownExternal")]),
                    new OpenOrdersSyncResult(true, 0, 0, 0))
            };
            var service = NewService(evidencePath, accountSyncService: accountSync);

            var result = await service.ArmAsync(new LiveArmingRequest("operator", "drift", "ARM LIVE"));

            Assert.False(result.Accepted);
            Assert.Equal("Blocked", result.Status);
            Assert.Contains("external account drift", result.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(evidencePath));
        }
        finally
        {
            DeleteIfExists(evidencePath);
        }
    }

    [Fact]
    public async Task GetStatusAsync_WhenCriticalConfigurationChanges_InvalidatesEvidence()
    {
        var evidencePath = NewEvidencePath();
        try
        {
            var initialService = NewService(
                evidencePath,
                riskOptions: new RiskOptions { MaxOpenOrders = 20 });
            var armResult = await initialService.ArmAsync(
                new LiveArmingRequest("operator", "regression", "ARM LIVE"));
            Assert.True(armResult.Accepted);

            var changedService = NewService(
                evidencePath,
                riskOptions: new RiskOptions { MaxOpenOrders = 21 });

            var status = await changedService.GetStatusAsync();

            Assert.False(status.IsArmed);
            Assert.Equal("ConfigChanged", status.State);
            Assert.NotNull(status.Evidence);
        }
        finally
        {
            DeleteIfExists(evidencePath);
        }
    }

    [Fact]
    public async Task DisarmAsync_WithConfirmation_ClearsEvidence()
    {
        var evidencePath = NewEvidencePath();
        try
        {
            var service = NewService(evidencePath);
            var armResult = await service.ArmAsync(new LiveArmingRequest("operator", "regression", "ARM LIVE"));
            Assert.True(armResult.Accepted);

            var disarm = await service.DisarmAsync(new LiveDisarmingRequest("operator", "risk-off", "DISARM LIVE"));
            var status = await service.GetStatusAsync();

            Assert.True(disarm.Accepted);
            Assert.Equal("Accepted", disarm.Status);
            Assert.False(File.Exists(evidencePath));
            Assert.False(status.IsArmed);
            Assert.Equal("NotArmed", status.State);
        }
        finally
        {
            DeleteIfExists(evidencePath);
        }
    }

    private static LiveArmingService NewService(
        string evidencePath,
        ExecutionOptions? executionOptions = null,
        RiskOptions? riskOptions = null,
        ComplianceOptions? complianceOptions = null,
        AccountSyncOptions? accountSyncOptions = null,
        IAccountSyncService? accountSyncService = null,
        IReadinessReportService? readinessReportService = null)
    {
        var armingOptions = new LiveArmingOptions
        {
            EvidenceFilePath = evidencePath,
            ExpirationMinutes = 240,
            MaxAccountSyncAgeSeconds = 300,
            ConfigVersion = "test-config"
        };

        var serviceProvider = new ServiceCollection()
            .AddSingleton(readinessReportService ?? new PassingReadinessReportService())
            .BuildServiceProvider();
        var stateStore = new FileLiveArmingStateStore(
            Options.Create(armingOptions),
            NullLogger<FileLiveArmingStateStore>.Instance);

        var risk = CreateRiskManager();
        return new LiveArmingService(
            serviceProvider,
            stateStore,
            new AllowComplianceGuard(),
            accountSyncService ?? new FreshAccountSyncService(),
            risk,
            CreateConfiguration(),
            Options.Create(executionOptions ?? new ExecutionOptions
            {
                Mode = ExecutionMode.Live,
                MaxOpenOrdersPerMarket = 10,
                UseBatchOrders = true,
                MaxBatchOrderSize = 15
            }),
            Options.Create(armingOptions),
            Options.Create(riskOptions ?? new RiskOptions()),
            Options.Create(complianceOptions ?? new ComplianceOptions
            {
                Enabled = true,
                GeoKycAllowed = true,
                AllowUnsafeLiveParameters = false
            }),
            Options.Create(accountSyncOptions ?? new AccountSyncOptions
            {
                Enabled = true,
                DetectExternalOpenOrders = true,
                TriggerKillSwitchOnDrift = true
            }));
    }

    private static IRiskManager CreateRiskManager()
    {
        var riskManager = new Mock<IRiskManager>();
        riskManager.SetupGet(manager => manager.IsKillSwitchActive).Returns(false);
        riskManager.Setup(manager => manager.GetStateSnapshot()).Returns(new RiskStateSnapshot(
            TotalOpenNotional: 10m,
            TotalOpenOrders: 1,
            TotalCapital: 100m,
            AvailableCapital: 90m,
            CapitalUtilizationPct: 10m,
            NotionalByStrategy: new Dictionary<string, decimal>(),
            NotionalByMarket: new Dictionary<string, decimal>(),
            OpenOrdersByStrategy: new Dictionary<string, int>(),
            UnhedgedExposures: []));
        return riskManager.Object;
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Polymarket:Clob:Address"] = "0x1234567890abcdef1234567890abcdef12345678",
                ["Polymarket:Clob:ApiKey"] = "api-key",
                ["Polymarket:Clob:ApiSecret"] = "api-secret",
                ["Polymarket:Clob:PrivateKey"] = "private-key"
            })
            .Build();
    }

    private static string NewEvidencePath()
    {
        return Path.Combine(Path.GetTempPath(), $"autotrade-live-arming-{Guid.NewGuid():N}.json");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class PassingReadinessReportService : IReadinessReportService
    {
        public Task<ReadinessReport> GetReportAsync(CancellationToken cancellationToken = default)
        {
            var now = new DateTimeOffset(2026, 5, 3, 8, 30, 0, TimeSpan.Zero);
            var liveRequired = FirstRunReadinessContract.Create()
                .Capabilities
                .Single(capability => capability.Capability == ReadinessCapability.LiveTrading)
                .RequiredCheckIds;

            var probes = liveRequired.ToDictionary(
                id => id,
                id => string.Equals(id, "execution.live_armed", StringComparison.Ordinal)
                    ? new ReadinessCheckProbe(
                        ReadinessCheckStatus.Blocked,
                        "test",
                        "Self check is excluded by Live arming.",
                        now,
                        "Self check.")
                    : new ReadinessCheckProbe(
                        ReadinessCheckStatus.Ready,
                        "test",
                        "Ready.",
                        now),
                StringComparer.Ordinal);

            return Task.FromResult(ReadinessReportFactory.Create(now, probes));
        }
    }

    private sealed class AllowComplianceGuard : IComplianceGuard
    {
        private static readonly ComplianceCheckResult Allowed = new(
            Enabled: true,
            IsCompliant: true,
            BlocksOrders: false,
            Issues: []);

        public ComplianceCheckResult CheckConfiguration(ExecutionMode executionMode) => Allowed;

        public ComplianceCheckResult CheckOrderPlacement(ExecutionMode executionMode) => Allowed;
    }

    private sealed class FreshAccountSyncService : IAccountSyncService
    {
        public DateTimeOffset? LastSyncTime { get; } = DateTimeOffset.UtcNow;

        public ExternalBalanceSnapshot? LastBalanceSnapshot { get; } = new(100m, 100m, DateTimeOffset.UtcNow);

        public IReadOnlyList<ExternalPositionSnapshot>? LastPositionsSnapshot { get; } = [];

        public Task<BalanceSyncResult> SyncBalanceAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new BalanceSyncResult(true, 100m, 100m));

        public Task<PositionsSyncResult> SyncPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PositionsSyncResult(true, 0, 0, []));

        public Task<OpenOrdersSyncResult> SyncOpenOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new OpenOrdersSyncResult(true, 0, 0, 0));

        public Task<FullSyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new FullSyncResult(
                true,
                new BalanceSyncResult(true, 100m, 100m),
                new PositionsSyncResult(true, 0, 0, []),
                new OpenOrdersSyncResult(true, 0, 0, 0)));
    }

    private sealed class RecordingAccountSyncService : IAccountSyncService
    {
        public int SyncAllCalls { get; private set; }

        public DateTimeOffset? LastSyncTime { get; private set; }

        public ExternalBalanceSnapshot? LastBalanceSnapshot { get; private set; }

        public IReadOnlyList<ExternalPositionSnapshot>? LastPositionsSnapshot { get; private set; }

        public FullSyncResult SyncResult { get; init; } = new(
            true,
            new BalanceSyncResult(true, 100m, 100m),
            new PositionsSyncResult(true, 0, 0, []),
            new OpenOrdersSyncResult(true, 0, 0, 0));

        public Task<BalanceSyncResult> SyncBalanceAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(SyncResult.Balance ?? new BalanceSyncResult(false, null, null, "Balance sync was not configured."));

        public Task<PositionsSyncResult> SyncPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(SyncResult.Positions ?? new PositionsSyncResult(false, 0, 0, null, "Position sync was not configured."));

        public Task<OpenOrdersSyncResult> SyncOpenOrdersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(SyncResult.OpenOrders ?? new OpenOrdersSyncResult(false, 0, 0, 0, "Open-order sync was not configured."));

        public Task<FullSyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
        {
            SyncAllCalls++;
            if (SyncResult.IsSuccess)
            {
                var syncedAtUtc = DateTimeOffset.UtcNow;
                LastSyncTime = syncedAtUtc;
                LastBalanceSnapshot = new ExternalBalanceSnapshot(
                    SyncResult.Balance?.BalanceUsdc ?? 0m,
                    SyncResult.Balance?.AllowanceUsdc ?? 0m,
                    syncedAtUtc);
                LastPositionsSnapshot = [];
            }

            return Task.FromResult(SyncResult);
        }
    }
}
