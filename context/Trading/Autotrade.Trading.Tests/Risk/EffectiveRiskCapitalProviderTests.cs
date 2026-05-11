using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Execution;
using Autotrade.Trading.Application.Risk;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Autotrade.Trading.Tests.Risk;

public sealed class EffectiveRiskCapitalProviderTests
{
    [Fact]
    public void GetSnapshot_live_mode_uses_min_of_risklimit_balance_allowance()
    {
        var risk = Options.Create(new RiskCapitalOptions
        {
            TotalCapital = 100m,
            AvailableCapital = 80m,
            RealizedDailyPnl = 0m
        });

        var execution = Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live });
        var store = new ExternalAccountSnapshotStore();
        store.SetBalanceSnapshot(new ExternalBalanceSnapshot(50m, 200m, DateTimeOffset.UtcNow));

        var provider = new EffectiveRiskCapitalProvider(risk, store, execution, NullLogger<EffectiveRiskCapitalProvider>.Instance);

        var snap = provider.GetSnapshot();
        Assert.Equal(50m, snap.TotalCapital);
        Assert.Equal(50m, snap.AvailableCapital);
    }

    [Fact]
    public void GetSnapshot_live_mode_limits_available_to_effective_total()
    {
        var risk = Options.Create(new RiskCapitalOptions
        {
            TotalCapital = 100m,
            AvailableCapital = 999m,
            RealizedDailyPnl = 0m
        });

        var execution = Options.Create(new ExecutionOptions { Mode = ExecutionMode.Live });
        var store = new ExternalAccountSnapshotStore();
        store.SetBalanceSnapshot(new ExternalBalanceSnapshot(60m, 60m, DateTimeOffset.UtcNow));

        var provider = new EffectiveRiskCapitalProvider(risk, store, execution, NullLogger<EffectiveRiskCapitalProvider>.Instance);

        var snap = provider.GetSnapshot();
        Assert.Equal(60m, snap.TotalCapital);
        Assert.Equal(60m, snap.AvailableCapital);
    }

    [Fact]
    public void GetSnapshot_paper_mode_returns_risklimit()
    {
        var risk = Options.Create(new RiskCapitalOptions
        {
            TotalCapital = 100m,
            AvailableCapital = 80m,
            RealizedDailyPnl = 1.23m
        });

        var execution = Options.Create(new ExecutionOptions { Mode = ExecutionMode.Paper });
        var store = new ExternalAccountSnapshotStore();

        var provider = new EffectiveRiskCapitalProvider(risk, store, execution, NullLogger<EffectiveRiskCapitalProvider>.Instance);

        var snap = provider.GetSnapshot();
        Assert.Equal(100m, snap.TotalCapital);
        Assert.Equal(80m, snap.AvailableCapital);
        Assert.Equal(1.23m, snap.RealizedDailyPnl);
    }
}

