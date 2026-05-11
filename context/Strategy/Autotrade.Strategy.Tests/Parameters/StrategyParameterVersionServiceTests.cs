using Autotrade.Strategy.Application.Audit;
using Autotrade.Strategy.Application.Parameters;
using Autotrade.Strategy.Application.Persistence;
using Autotrade.Strategy.Application.Strategies.DualLeg;
using Autotrade.Strategy.Application.Strategies.Endgame;
using Autotrade.Strategy.Application.Strategies.LiquidityMaking;
using Autotrade.Strategy.Application.Strategies.LiquidityPulse;
using Autotrade.Strategy.Application.Strategies.RepricingLag;
using Autotrade.Strategy.Application.Strategies.Volatility;
using Autotrade.Strategy.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Autotrade.Strategy.Tests.Parameters;

public sealed class StrategyParameterVersionServiceTests
{
    [Fact]
    public async Task UpdateAsync_ValidatesPersistsDiffAndAppliesRuntimeOptions()
    {
        var harness = CreateHarness();

        var result = await harness.Service.UpdateAsync(
            "liquidity_pulse",
            new StrategyParameterMutationRequest(
                new Dictionary<string, string>
                {
                    ["MaxMarkets"] = "12",
                    ["MaxSpread"] = "0.04"
                },
                "operator-1",
                "test",
                "tighten liquidity pulse"),
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("Accepted", result.Status);
        Assert.Single(harness.Repository.Versions);
        Assert.Equal(12, harness.LiquidityPulseMonitor.CurrentValue.MaxMarkets);
        Assert.Equal(0.04m, harness.LiquidityPulseMonitor.CurrentValue.MaxSpread);
        Assert.NotEqual("v1", harness.LiquidityPulseMonitor.CurrentValue.ConfigVersion);
        Assert.Contains(result.Version!.Diff, item => item.Name == "MaxMarkets"
            && item.PreviousValue == "40"
            && item.NextValue == "12");
        Assert.Contains(result.Version.Diff, item => item.Name == "MaxSpread"
            && item.PreviousValue == "0.05"
            && item.NextValue == "0.04");
        var audit = Assert.Single(harness.AuditLogger.Entries);
        Assert.Equal("control-room strategy parameters update", audit.CommandName);
        Assert.True(audit.Success);
        Assert.Equal("operator-1", audit.Actor);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("liquidity_pulse", payload.RootElement.GetProperty("strategyId").GetString());
        Assert.Equal("Update", payload.RootElement.GetProperty("changeType").GetString());
        Assert.Equal("test", payload.RootElement.GetProperty("source").GetString());
        Assert.Equal("tighten liquidity pulse", payload.RootElement.GetProperty("reason").GetString());
        Assert.Contains(payload.RootElement.GetProperty("diff").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "MaxMarkets"
            && item.GetProperty("previousValue").GetString() == "40"
            && item.GetProperty("nextValue").GetString() == "12");
    }

    [Fact]
    public async Task UpdateAsync_InvalidValueRejectsWithoutPersistenceOrRuntimeChange()
    {
        var harness = CreateHarness();

        var result = await harness.Service.UpdateAsync(
            "liquidity_pulse",
            new StrategyParameterMutationRequest(
                new Dictionary<string, string>
                {
                    ["MaxSpread"] = "1.40"
                },
                "operator-1",
                "test",
                "invalid"),
            CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal("InvalidRequest", result.Status);
        Assert.Empty(harness.Repository.Versions);
        Assert.Equal(0.05m, harness.LiquidityPulseMonitor.CurrentValue.MaxSpread);
        Assert.Empty(harness.AuditLogger.Entries);
    }

    [Fact]
    public async Task UpdateAsync_CommitFailureRevertsRuntimeOptionsAndSkipsAudit()
    {
        var harness = CreateHarness(failCommit: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.UpdateAsync(
                "liquidity_pulse",
                new StrategyParameterMutationRequest(
                    new Dictionary<string, string>
                    {
                        ["MaxMarkets"] = "12",
                        ["MaxSpread"] = "0.04"
                    },
                    "operator-1",
                    "test",
                    "commit failure regression"),
                CancellationToken.None));

        Assert.Equal("Commit failed.", exception.Message);
        Assert.Equal(1, harness.UnitOfWork.CommitAttempts);
        Assert.Equal(40, harness.LiquidityPulseMonitor.CurrentValue.MaxMarkets);
        Assert.Equal(0.05m, harness.LiquidityPulseMonitor.CurrentValue.MaxSpread);
        Assert.Empty(harness.AuditLogger.Entries);
    }

    [Fact]
    public async Task RollbackAsync_RestoresPriorVersionAndRecordsRollbackDiff()
    {
        var harness = CreateHarness();
        var first = await harness.Service.UpdateAsync(
            "liquidity_pulse",
            new StrategyParameterMutationRequest(
                new Dictionary<string, string> { ["MaxMarkets"] = "12" },
                "operator-1",
                "test",
                "first"),
            CancellationToken.None);
        await harness.Service.UpdateAsync(
            "liquidity_pulse",
            new StrategyParameterMutationRequest(
                new Dictionary<string, string> { ["MaxMarkets"] = "20" },
                "operator-1",
                "test",
                "second"),
            CancellationToken.None);

        var rollback = await harness.Service.RollbackAsync(
            "liquidity_pulse",
            new StrategyParameterRollbackRequest(
                first.Version!.VersionId,
                "operator-2",
                "test",
                "rollback"),
            CancellationToken.None);

        Assert.True(rollback.Accepted);
        Assert.Equal("Rollback", rollback.Version!.ChangeType);
        Assert.Equal(first.Version.VersionId, rollback.Version.RollbackSourceVersionId);
        Assert.Equal(12, harness.LiquidityPulseMonitor.CurrentValue.MaxMarkets);
        Assert.Contains(rollback.Version.Diff, item => item.Name == "MaxMarkets"
            && item.PreviousValue == "20"
            && item.NextValue == "12");
        Assert.Equal(3, harness.Repository.Versions.Count);
        Assert.Equal(3, harness.AuditLogger.Entries.Count);
        var audit = harness.AuditLogger.Entries[^1];
        Assert.Equal("control-room strategy parameters rollback", audit.CommandName);
        Assert.True(audit.Success);
        using var payload = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("Rollback", payload.RootElement.GetProperty("changeType").GetString());
        Assert.Equal(first.Version.VersionId, payload.RootElement.GetProperty("rollbackSourceVersionId").GetGuid());
        Assert.Contains(payload.RootElement.GetProperty("diff").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "MaxMarkets"
            && item.GetProperty("previousValue").GetString() == "20"
            && item.GetProperty("nextValue").GetString() == "12");
    }

    [Fact]
    public async Task UpdateAsync_RepricingLagSupportsArrayParameters()
    {
        var harness = CreateHarness();

        var result = await harness.Service.UpdateAsync(
            "repricing_lag_arbitrage",
            new StrategyParameterMutationRequest(
                new Dictionary<string, string>
                {
                    ["AllowedSpotSources"] = "rtds:crypto_prices",
                    ["MinEdge"] = "0.05"
                },
                "operator-1",
                "test",
                "restrict spot sources"),
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(0.05m, harness.RepricingLagMonitor.CurrentValue.MinEdge);
        Assert.Equal(["rtds:crypto_prices"], harness.RepricingLagMonitor.CurrentValue.AllowedSpotSources);
        Assert.Contains(result.Version!.Diff, item => item.Name == "AllowedSpotSources"
            && item.PreviousValue == "rtds:crypto_prices,rtds:crypto_prices_chainlink"
            && item.NextValue == "rtds:crypto_prices");
    }

    private static Harness CreateHarness(bool failCommit = false)
    {
        var repository = new InMemoryStrategyParameterVersionRepository();
        var unitOfWork = new InMemoryStrategyUnitOfWork(failCommit);
        var auditLogger = new CapturingCommandAuditLogger();
        var dualLeg = CreateMonitor(new DualLegArbitrageOptions { Enabled = false, ConfigVersion = "v1" });
        var endgame = CreateMonitor(new EndgameSweepOptions { Enabled = false, ConfigVersion = "v1" });
        var liquidityPulse = CreateMonitor(new LiquidityPulseOptions
        {
            Enabled = true,
            ConfigVersion = "v1",
            MaxMarkets = 40,
            MaxSpread = 0.05m
        });
        var liquidityMaker = CreateMonitor(new LiquidityMakerOptions { Enabled = false, ConfigVersion = "v1" });
        var microVolatility = CreateMonitor(new MicroVolatilityScalperOptions { Enabled = false, ConfigVersion = "v1" });
        var repricingLag = CreateMonitor(new RepricingLagArbitrageOptions { Enabled = false, ConfigVersion = "v1" });

        var service = new StrategyParameterVersionService(
            repository,
            unitOfWork,
            auditLogger,
            NullLogger<StrategyParameterVersionService>.Instance,
            dualLeg,
            dualLeg.Cache,
            endgame,
            endgame.Cache,
            liquidityPulse,
            liquidityPulse.Cache,
            liquidityMaker,
            liquidityMaker.Cache,
            microVolatility,
            microVolatility.Cache,
            repricingLag,
            repricingLag.Cache);

        return new Harness(service, repository, unitOfWork, auditLogger, liquidityPulse, repricingLag);
    }

    private static MutableOptionsMonitor<T> CreateMonitor<T>(T options)
        where T : class, new()
    {
        return new MutableOptionsMonitor<T>(options);
    }

    private sealed record Harness(
        StrategyParameterVersionService Service,
        InMemoryStrategyParameterVersionRepository Repository,
        InMemoryStrategyUnitOfWork UnitOfWork,
        CapturingCommandAuditLogger AuditLogger,
        MutableOptionsMonitor<LiquidityPulseOptions> LiquidityPulseMonitor,
        MutableOptionsMonitor<RepricingLagArbitrageOptions> RepricingLagMonitor);

    private sealed class MutableOptionsMonitor<T> : IOptionsMonitor<T>
        where T : class, new()
    {
        private readonly T _initial;

        public MutableOptionsMonitor(T initial)
        {
            _initial = initial;
            Cache.TryAdd(Options.DefaultName, initial);
        }

        public IOptionsMonitorCache<T> Cache { get; } = new OptionsCache<T>();

        public T CurrentValue => Get(Options.DefaultName);

        public T Get(string? name)
        {
            return Cache.GetOrAdd(name ?? Options.DefaultName, () => _initial);
        }

        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class InMemoryStrategyParameterVersionRepository : IStrategyParameterVersionRepository
    {
        public List<StrategyParameterVersion> Versions { get; } = [];

        public Task AddAsync(StrategyParameterVersion version, CancellationToken cancellationToken = default)
        {
            Versions.Add(version);
            return Task.CompletedTask;
        }

        public Task<StrategyParameterVersion?> GetAsync(Guid versionId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Versions.FirstOrDefault(version => version.Id == versionId));
        }

        public Task<StrategyParameterVersion?> GetLatestAsync(
            string strategyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Versions
                .Where(version => version.StrategyId == strategyId)
                .OrderByDescending(version => version.CreatedAtUtc)
                .FirstOrDefault());
        }

        public Task<IReadOnlyList<StrategyParameterVersion>> GetLatestByStrategyIdsAsync(
            IReadOnlyCollection<string> strategyIds,
            CancellationToken cancellationToken = default)
        {
            var latest = Versions
                .Where(version => strategyIds.Contains(version.StrategyId))
                .GroupBy(version => version.StrategyId)
                .Select(group => group.OrderByDescending(version => version.CreatedAtUtc).First())
                .ToArray();
            return Task.FromResult<IReadOnlyList<StrategyParameterVersion>>(latest);
        }

        public Task<IReadOnlyList<StrategyParameterVersion>> GetRecentAsync(
            string strategyId,
            int limit,
            CancellationToken cancellationToken = default)
        {
            var recent = Versions
                .Where(version => version.StrategyId == strategyId)
                .OrderByDescending(version => version.CreatedAtUtc)
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<StrategyParameterVersion>>(recent);
        }
    }

    private sealed class InMemoryStrategyUnitOfWork(bool failCommit) : IStrategyUnitOfWork
    {
        public int CommitAttempts { get; private set; }

        public Task<bool> CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitAttempts++;
            if (failCommit)
            {
                throw new InvalidOperationException("Commit failed.");
            }

            return Task.FromResult(true);
        }
    }

    private sealed class CapturingCommandAuditLogger : ICommandAuditLogger
    {
        public List<CommandAuditEntry> Entries { get; } = [];

        public Task LogAsync(CommandAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
