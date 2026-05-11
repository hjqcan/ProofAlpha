using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Tests.Risk;

public class RiskManagerTests
{
    #region ValidateOrderAsync Tests

    [Fact]
    public async Task ValidateOrder_MaxOpenOrders_应拒绝()
    {
        var options = Options.Create(new RiskOptions { MaxOpenOrders = 1, KillSwitchEnabled = false });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        var request = CreateRequest("strategy-1", "order-1", 0.5m, 10m);
        var allow = await riskManager.ValidateOrderAsync(request);
        Assert.True(allow.Allowed);

        await riskManager.RecordOrderAcceptedAsync(request);

        var reject = await riskManager.ValidateOrderAsync(CreateRequest("strategy-1", "order-2", 0.5m, 10m));
        Assert.False(reject.Allowed);
        Assert.Equal("RISK_MAX_OPEN_ORDERS", reject.Code);
    }

    [Fact]
    public async Task ValidateOrder_资本占用超限_应拒绝()
    {
        var options = Options.Create(new RiskOptions { MaxCapitalPerMarket = 0.05m, KillSwitchEnabled = false });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        var request = CreateRequest("strategy-1", "order-1", 0.5m, 20m); // notional = 10
        var result = await riskManager.ValidateOrderAsync(request);

        Assert.False(result.Allowed);
        Assert.Equal("RISK_MAX_CAPITAL_PER_MARKET", result.Code);
    }

    [Fact]
    public async Task ValidateOrder_策略资本超限_应拒绝()
    {
        var options = Options.Create(new RiskOptions
        {
            MaxCapitalPerMarket = 0.10m, // 每市场 10
            MaxCapitalPerStrategy = 0.10m, // 每策略 10
            MaxTotalCapitalUtilization = 0.50m,
            MaxConcurrentMarketsPerStrategy = 10, // 允许多市场
            CapitalUtilizationWarningThreshold = 0.40m,
            KillSwitchEnabled = false
        });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        // 先添加一个订单到 market-1，notional = 5
        var request1 = CreateRequest("strategy-1", "order-1", 0.5m, 10m); // notional = 5
        await riskManager.RecordOrderAcceptedAsync(request1);

        // 再添加一个订单到 market-2，notional = 5
        var request2 = CreateRequest("strategy-1", "order-2", 0.5m, 10m) with { MarketId = "market-2" };
        await riskManager.RecordOrderAcceptedAsync(request2);

        // 策略总 notional = 10，再添加一个小订单也会超限
        var request3 = CreateRequest("strategy-1", "order-3", 0.5m, 2m) with { MarketId = "market-3" }; // notional = 1
        var result = await riskManager.ValidateOrderAsync(request3);

        Assert.False(result.Allowed);
        Assert.Equal("RISK_MAX_CAPITAL_PER_STRATEGY", result.Code);
    }

    [Fact]
    public async Task ValidateOrder_全局资本利用率超限_应拒绝()
    {
        var options = Options.Create(new RiskOptions
        {
            MaxCapitalPerMarket = 0.05m, // 每市场 5
            MaxCapitalPerStrategy = 0.10m, // 每策略 10
            MaxTotalCapitalUtilization = 0.10m, // 全局限制 = 10
            MaxConcurrentMarketsPerStrategy = 10,
            CapitalUtilizationWarningThreshold = 0.08m,
            KillSwitchEnabled = false
        });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        // 添加两个不同策略的订单，共 notional = 10
        var request1 = CreateRequest("strategy-1", "order-1", 0.5m, 10m); // notional = 5
        await riskManager.RecordOrderAcceptedAsync(request1);

        var request2 = CreateRequest("strategy-2", "order-2", 0.5m, 10m) with { MarketId = "market-2" }; // notional = 5
        await riskManager.RecordOrderAcceptedAsync(request2);

        // 全局 notional = 10，再添加一个小订单会超限（第三个策略）
        var request3 = CreateRequest("strategy-3", "order-3", 0.5m, 2m) with { MarketId = "market-3" }; // notional = 1
        var result = await riskManager.ValidateOrderAsync(request3);

        Assert.False(result.Allowed);
        Assert.Equal("RISK_MAX_TOTAL_UTILIZATION", result.Code);
    }

    [Fact]
    public async Task ValidateOrder_未对冲敞口超限_应拒绝()
    {
        var options = Options.Create(new RiskOptions
        {
            MaxCapitalPerMarket = 0.05m, // 必须 >= MaxUnhedgedCapitalPerMarket
            MaxCapitalPerStrategy = 0.30m,
            MaxTotalCapitalUtilization = 0.50m,
            CapitalUtilizationWarningThreshold = 0.40m,
            MaxUnhedgedCapitalPerMarket = 0.01m,
            KillSwitchEnabled = false
        });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        // 第一腿订单，未对冲敞口最大 = 100 * 0.01 = 1，订单 notional = 2.5
        var request = CreateRequest("strategy-1", "order-1", 0.5m, 5m) with { Leg = OrderLeg.First };
        var result = await riskManager.ValidateOrderAsync(request);

        Assert.False(result.Allowed);
        Assert.Equal("RISK_MAX_UNHEDGED_CAPITAL", result.Code);
    }

    [Fact]
    public async Task ValidateOrder_未对冲敞口Sell_应允许()
    {
        var options = Options.Create(new RiskOptions
        {
            MaxCapitalPerMarket = 1.0m,
            MaxCapitalPerStrategy = 1.0m,
            MaxTotalCapitalUtilization = 1.0m,
            CapitalUtilizationWarningThreshold = 0.95m,
            MaxUnhedgedCapitalPerMarket = 0.01m,
            KillSwitchEnabled = false
        });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.RecordUnhedgedExposureAsync(
            "strategy-1",
            "market-1",
            "token-yes",
            "token-no",
            OutcomeSide.Yes,
            OrderSide.Buy,
            2m,
            0.4m,
            DateTimeOffset.UtcNow);

        var request = CreateRequest("strategy-1", "order-1", 0.5m, 5m) with
        {
            Leg = OrderLeg.First,
            Side = OrderSide.Sell
        };

        var result = await riskManager.ValidateOrderAsync(request);

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task ValidateOrder_KillSwitch开启_应拒绝()
    {
        var options = Options.Create(new RiskOptions { KillSwitchEnabled = true });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.ActivateKillSwitchAsync("test");

        var result = await riskManager.ValidateOrderAsync(CreateRequest("strategy-1", "order-1", 0.5m, 1m));
        Assert.False(result.Allowed);
        Assert.Equal(RiskAction.KillSwitch, result.Action);
    }

    [Fact]
    public async Task ValidateOrder_策略级KillSwitch_应拒绝该策略()
    {
        var options = Options.Create(new RiskOptions { KillSwitchEnabled = true });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.ActivateStrategyKillSwitchAsync(
            "strategy-1",
            KillSwitchLevel.SoftStop,
            "TEST",
            "Test reason");

        // strategy-1 应被拒绝
        var result1 = await riskManager.ValidateOrderAsync(CreateRequest("strategy-1", "order-1", 0.5m, 1m));
        Assert.False(result1.Allowed);
        Assert.Equal("RISK_STRATEGY_KILL_SWITCH", result1.Code);

        // strategy-2 应被允许
        var result2 = await riskManager.ValidateOrderAsync(CreateRequest("strategy-2", "order-2", 0.5m, 1m));
        Assert.True(result2.Allowed);
    }

    [Fact]
    public async Task ValidateOrder_日亏损超限_应触发KillSwitch()
    {
        var options = Options.Create(new RiskOptions
        {
            DailyLossLimitPct = 0.02m,
            KillSwitchEnabled = true
        });
        var capitalOptions = Options.Create(new RiskCapitalOptions
        {
            TotalCapital = 100m,
            AvailableCapital = 100m,
            RealizedDailyPnl = -3m // 亏损 3%，超过 2% 限制
        });
        var riskManager = CreateManager(options, capitalOptions);

        var result = await riskManager.ValidateOrderAsync(CreateRequest("strategy-1", "order-1", 0.5m, 1m));
        Assert.False(result.Allowed);
        Assert.Equal("RISK_DAILY_LOSS_LIMIT", result.Code);
        Assert.True(riskManager.IsKillSwitchActive);
    }

    #endregion

    #region Kill Switch Tests

    [Fact]
    public async Task RecordOrderError_超过阈值_触发策略级KillSwitch()
    {
        var options = Options.Create(new RiskOptions { MaxConsecutiveOrderErrors = 1, KillSwitchEnabled = true });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.RecordOrderErrorAsync("strategy-1", "order-1", "ERROR", "boom");

        // 连续错误触发的是策略级别 Kill Switch
        var strategyState = riskManager.GetStrategyKillSwitchState("strategy-1");
        Assert.True(strategyState.IsActive);
        Assert.Equal("RISK_CONSECUTIVE_ERRORS", strategyState.ReasonCode);

        // 策略被阻止
        Assert.True(riskManager.IsStrategyBlocked("strategy-1"));

        // 全局 Kill Switch 不应被激活
        Assert.False(riskManager.IsKillSwitchActive);
    }

    [Fact]
    public async Task ActivateKillSwitch_SoftStop_应记录级别()
    {
        var options = Options.Create(new RiskOptions { KillSwitchEnabled = true });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.ActivateKillSwitchAsync(KillSwitchLevel.SoftStop, "TEST_SOFT", "Soft stop test");

        var state = riskManager.GetKillSwitchState();
        Assert.True(state.IsActive);
        Assert.Equal(KillSwitchLevel.SoftStop, state.Level);
        Assert.Equal("TEST_SOFT", state.ReasonCode);
    }

    [Fact]
    public async Task ActivateKillSwitch_HardStop_应覆盖SoftStop()
    {
        var options = Options.Create(new RiskOptions { KillSwitchEnabled = true });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.ActivateKillSwitchAsync(KillSwitchLevel.SoftStop, "SOFT", "Soft");
        await riskManager.ActivateKillSwitchAsync(KillSwitchLevel.HardStop, "HARD", "Hard");

        var state = riskManager.GetKillSwitchState();
        Assert.Equal(KillSwitchLevel.HardStop, state.Level);
        Assert.Equal("HARD", state.ReasonCode);
    }

    [Fact]
    public async Task ActivateKillSwitch_SoftStop不应覆盖HardStop()
    {
        var options = Options.Create(new RiskOptions { KillSwitchEnabled = true });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.ActivateKillSwitchAsync(KillSwitchLevel.HardStop, "HARD", "Hard");
        await riskManager.ActivateKillSwitchAsync(KillSwitchLevel.SoftStop, "SOFT", "Soft");

        var state = riskManager.GetKillSwitchState();
        Assert.Equal(KillSwitchLevel.HardStop, state.Level);
        Assert.Equal("HARD", state.ReasonCode);
    }

    [Fact]
    public async Task ResetKillSwitch_应清除状态()
    {
        var options = Options.Create(new RiskOptions { KillSwitchEnabled = true });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.ActivateKillSwitchAsync(KillSwitchLevel.HardStop, "TEST", "Test");
        Assert.True(riskManager.IsKillSwitchActive);

        await riskManager.ResetKillSwitchAsync();
        Assert.False(riskManager.IsKillSwitchActive);
    }

    [Fact]
    public async Task ResetStrategyKillSwitch_应只清除指定策略()
    {
        var options = Options.Create(new RiskOptions { KillSwitchEnabled = true });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.ActivateStrategyKillSwitchAsync("strategy-1", KillSwitchLevel.SoftStop, "TEST1", "Test 1");
        await riskManager.ActivateStrategyKillSwitchAsync("strategy-2", KillSwitchLevel.SoftStop, "TEST2", "Test 2");

        Assert.True(riskManager.IsStrategyBlocked("strategy-1"));
        Assert.True(riskManager.IsStrategyBlocked("strategy-2"));

        await riskManager.ResetKillSwitchAsync("strategy-1");

        Assert.False(riskManager.IsStrategyBlocked("strategy-1"));
        Assert.True(riskManager.IsStrategyBlocked("strategy-2"));
    }

    [Fact]
    public async Task GetAllActiveKillSwitches_应返回所有激活状态()
    {
        var options = Options.Create(new RiskOptions { KillSwitchEnabled = true });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.ActivateKillSwitchAsync(KillSwitchLevel.SoftStop, "GLOBAL", "Global");
        await riskManager.ActivateStrategyKillSwitchAsync("strategy-1", KillSwitchLevel.SoftStop, "S1", "Strategy 1");
        await riskManager.ActivateStrategyKillSwitchAsync("strategy-2", KillSwitchLevel.HardStop, "S2", "Strategy 2");

        var all = riskManager.GetAllActiveKillSwitches();
        Assert.Equal(3, all.Count);
    }

    #endregion

    #region Unhedged Exposure Tests

    [Fact]
    public async Task UnhedgedExposure_超时_应返回()
    {
        var options = Options.Create(new RiskOptions { MaxFirstLegExposureSeconds = 1, KillSwitchEnabled = false });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.RecordUnhedgedExposureAsync(
            "strategy-1", "market-1", "token-yes", "token-no", OutcomeSide.Yes, OrderSide.Buy, 10m, 0.50m, DateTimeOffset.UtcNow.AddSeconds(-5));

        var expired = riskManager.GetExpiredUnhedgedExposures(DateTimeOffset.UtcNow);
        Assert.Single(expired);
        Assert.Equal("market-1", expired[0].MarketId);
        Assert.Equal("token-yes", expired[0].TokenId);
        Assert.Equal("token-no", expired[0].HedgeTokenId);
        Assert.Equal(OutcomeSide.Yes, expired[0].Outcome);
        Assert.Equal(10m, expired[0].Quantity);
    }

    [Fact]
    public async Task UnhedgedExposure_未超时_不应返回()
    {
        var options = Options.Create(new RiskOptions { MaxFirstLegExposureSeconds = 120, KillSwitchEnabled = false });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.RecordUnhedgedExposureAsync(
            "strategy-1", "market-1", "token-yes", "token-no", OutcomeSide.Yes, OrderSide.Buy, 10m, 0.50m, DateTimeOffset.UtcNow.AddSeconds(-5));

        var expired = riskManager.GetExpiredUnhedgedExposures(DateTimeOffset.UtcNow);
        Assert.Empty(expired);
    }

    [Fact]
    public async Task ClearUnhedgedExposure_应移除记录()
    {
        var options = Options.Create(new RiskOptions { MaxFirstLegExposureSeconds = 1, KillSwitchEnabled = false });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.RecordUnhedgedExposureAsync(
            "strategy-1", "market-1", "token-yes", "token-no", OutcomeSide.Yes, OrderSide.Buy, 10m, 0.50m, DateTimeOffset.UtcNow.AddSeconds(-5));
        await riskManager.ClearUnhedgedExposureAsync("strategy-1", "market-1");

        var expired = riskManager.GetExpiredUnhedgedExposures(DateTimeOffset.UtcNow);
        Assert.Empty(expired);
    }

    #endregion

    #region State Snapshot Tests

    [Fact]
    public async Task GetStateSnapshot_应返回正确的资本利用率()
    {
        var options = Options.Create(new RiskOptions { KillSwitchEnabled = false });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 80m });
        var riskManager = CreateManager(options, capitalOptions);

        var request = CreateRequest("strategy-1", "order-1", 0.5m, 20m); // notional = 10
        await riskManager.RecordOrderAcceptedAsync(request);

        var snapshot = riskManager.GetStateSnapshot();
        Assert.Equal(10m, snapshot.TotalOpenNotional);
        Assert.Equal(1, snapshot.TotalOpenOrders);
        Assert.Equal(100m, snapshot.TotalCapital);
        Assert.Equal(80m, snapshot.AvailableCapital);
        Assert.Equal(0.10m, snapshot.CapitalUtilizationPct); // 10 / 100 = 0.10
    }

    [Fact]
    public async Task GetOpenOrderIds_按策略筛选_应返回正确结果()
    {
        var options = Options.Create(new RiskOptions { KillSwitchEnabled = false });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-1", "order-1", 0.5m, 1m));
        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-1", "order-2", 0.5m, 1m));
        await riskManager.RecordOrderAcceptedAsync(CreateRequest("strategy-2", "order-3", 0.5m, 1m));

        var strategy1Orders = riskManager.GetOpenOrderIds("strategy-1");
        var strategy2Orders = riskManager.GetOpenOrderIds("strategy-2");
        var allOrders = riskManager.GetOpenOrderIds();

        Assert.Equal(2, strategy1Orders.Count);
        Assert.Single(strategy2Orders);
        Assert.Equal(3, allOrders.Count);
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void RiskOptions_无效配置_应抛出异常()
    {
        var options = new RiskOptions
        {
            MaxCapitalPerMarket = 0.50m, // > MaxCapitalPerStrategy
            MaxCapitalPerStrategy = 0.30m
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void RiskOptions_有效配置_应通过校验()
    {
        var options = new RiskOptions
        {
            MaxCapitalPerMarket = 0.05m,
            MaxCapitalPerStrategy = 0.30m,
            MaxTotalCapitalUtilization = 0.50m,
            MaxUnhedgedCapitalPerMarket = 0.02m,
            CapitalUtilizationWarningThreshold = 0.40m
        };

        options.Validate(); // Should not throw
    }

    [Fact]
    public void RiskOptions_范围外的值_应抛出异常()
    {
        var options = new RiskOptions
        {
            MaxCapitalPerMarket = 1.5m // > 1.0
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    #endregion

    #region Post-Trade Risk Check Tests

    [Fact]
    public async Task RecordOrderUpdate_启用PostTrade检查_应更新状态()
    {
        var options = Options.Create(new RiskOptions
        {
            KillSwitchEnabled = false,
            EnablePostTradeRiskCheck = true
        });
        var capitalOptions = Options.Create(new RiskCapitalOptions { TotalCapital = 100m, AvailableCapital = 100m });
        var riskManager = CreateManager(options, capitalOptions);

        var request = CreateRequest("strategy-1", "order-1", 0.5m, 10m);
        await riskManager.RecordOrderAcceptedAsync(request);

        var update = new RiskOrderUpdate
        {
            ClientOrderId = "order-1",
            StrategyId = "strategy-1",
            Status = ExecutionStatus.PartiallyFilled,
            FilledQuantity = 5m,
            OriginalQuantity = 10m
        };

        await riskManager.RecordOrderUpdateAsync(update);

        var snapshot = riskManager.GetStateSnapshot();
        // 剩余 5 * 0.5 = 2.5 notional
        Assert.Equal(2.5m, snapshot.TotalOpenNotional);
    }

    #endregion

    private static RiskManager CreateManager(IOptions<RiskOptions> options, IOptions<RiskCapitalOptions> capitalOptions)
    {
        return new RiskManager(
            options,
            new InMemoryRiskCapitalProvider(capitalOptions),
            new InMemoryRiskEventRepository(NullLogger<InMemoryRiskEventRepository>.Instance),
            new RiskStateStore(),
            new RiskMetrics(),
            NullLogger<RiskManager>.Instance);
    }

    private static RiskOrderRequest CreateRequest(string strategyId, string orderId, decimal price, decimal quantity)
        => new()
        {
            StrategyId = strategyId,
            ClientOrderId = orderId,
            MarketId = "market-1",
            TokenId = "token-1",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            TimeInForce = TimeInForce.Gtc,
            Price = price,
            Quantity = quantity,
            Leg = OrderLeg.Single
        };
}
