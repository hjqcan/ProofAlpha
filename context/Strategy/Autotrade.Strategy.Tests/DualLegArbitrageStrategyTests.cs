using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Strategies.DualLeg;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Autotrade.Strategy.Tests;

public sealed class DualLegArbitrageStrategyTests
{
    [Fact]
    public async Task EvaluateEntry_ReturnsSignal_WhenPairCostBelowThreshold()
    {
        var context = BuildContext();
        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            PairCostThreshold = 0.9m,
            DefaultOrderQuantity = 2m,
            MinOrderQuantity = 1m,
            MaxNotionalPerOrder = 10m,
            SequentialOrderMode = false // 同时下单模式
        };

        var strategy = new DualLegArbitrageStrategy(context, new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        var snapshot = CreateSnapshot(0.4m, 0.4m, 10m, 10m);

        var signal = await strategy.EvaluateEntryAsync(snapshot, CancellationToken.None);

        Assert.NotNull(signal);
        Assert.Equal(StrategySignalType.Entry, signal!.Type);
        Assert.Equal(2, signal.Orders.Count);
    }

    [Fact]
    public async Task EvaluateEntry_ReturnsNull_WhenPairCostAboveThreshold()
    {
        var context = BuildContext();
        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            PairCostThreshold = 0.7m
        };

        var strategy = new DualLegArbitrageStrategy(context, new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        var snapshot = CreateSnapshot(0.4m, 0.4m, 10m, 10m);

        var signal = await strategy.EvaluateEntryAsync(snapshot, CancellationToken.None);

        Assert.Null(signal);
    }

    [Fact]
    public async Task EvaluateExit_ReturnsSignal_WhenPairValueAboveExitThreshold()
    {
        var context = BuildContext();
        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            PairCostThreshold = 0.9m,
            ExitPairValueThreshold = 0.8m,
            DefaultOrderQuantity = 2m,
            MinOrderQuantity = 1m,
            MaxNotionalPerOrder = 10m
        };

        var strategy = new DualLegArbitrageStrategy(context, new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        var entryUpdateYes = CreateOrderUpdate(OutcomeSide.Yes, StrategySignalType.Entry, 2m, 2m, ExecutionStatus.Filled);
        var entryUpdateNo = CreateOrderUpdate(OutcomeSide.No, StrategySignalType.Entry, 2m, 2m, ExecutionStatus.Filled);

        await strategy.OnOrderUpdateAsync(entryUpdateYes, CancellationToken.None);
        await strategy.OnOrderUpdateAsync(entryUpdateNo, CancellationToken.None);

        var snapshot = CreateSnapshot(0.6m, 0.6m, 10m, 10m, bidYes: 0.45m, bidNo: 0.45m);
        var exitSignal = await strategy.EvaluateExitAsync(snapshot, CancellationToken.None);

        Assert.NotNull(exitSignal);
        Assert.Equal(StrategySignalType.Exit, exitSignal!.Type);
    }

    [Fact]
    public async Task SelectMarkets_FiltersExpiringMarkets()
    {
        // 创建一个即将到期的市场（30分钟后到期）和一个正常市场（2小时后到期）
        var expiringMarket = new MarketInfoDto
        {
            MarketId = "expiring-market",
            ConditionId = "cond-expiring",
            Name = "Expiring Market",
            Status = "Active",
            Liquidity = 5000m,
            Volume24h = 1000m,
            TokenIds = new[] { "yes-1", "no-1" },
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30) // 30分钟后到期
        };

        var normalMarket = new MarketInfoDto
        {
            MarketId = "normal-market",
            ConditionId = "cond-normal",
            Name = "Normal Market",
            Status = "Active",
            Liquidity = 5000m,
            Volume24h = 1000m,
            TokenIds = new[] { "yes-2", "no-2" },
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2) // 2小时后到期
        };

        var catalog = new FakeMarketCatalogReader(new[] { expiringMarket, normalMarket });
        var context = new StrategyContext
        {
            StrategyId = "dual_leg_arbitrage",
            ExecutionService = new NullExecutionService(),
            OrderBookReader = new FakeOrderBookReader(),
            MarketCatalog = catalog,
            RiskManager = new NullRiskManager(),
            DecisionLogger = new NullDecisionLogger()
        };

        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            MinTimeToExpiryMinutes = 60 // 需要至少60分钟到期时间
        };

        var strategy = new DualLegArbitrageStrategy(context, new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        var markets = await strategy.SelectMarketsAsync(CancellationToken.None);
        var marketList = markets.ToList();

        Assert.Single(marketList);
        Assert.Equal("normal-market", marketList[0]);
    }

    [Fact]
    public async Task SelectMarkets_ForceIncludesUnhedgedMarket_EvenWhenExpiringAndMaxMarketsIsOne()
    {
        // expiring-market：会被 MinTimeToExpiryMinutes 过滤掉
        var expiringMarket = new MarketInfoDto
        {
            MarketId = "expiring-market",
            ConditionId = "cond-expiring",
            Name = "Expiring Market",
            Status = "Active",
            Liquidity = 5000m,
            Volume24h = 1000m,
            TokenIds = new[] { "yes-1", "no-1" },
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        var normalMarket = new MarketInfoDto
        {
            MarketId = "normal-market",
            ConditionId = "cond-normal",
            Name = "Normal Market",
            Status = "Active",
            Liquidity = 5000m,
            Volume24h = 1000m,
            TokenIds = new[] { "yes-2", "no-2" },
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2)
        };

        var catalog = new FakeMarketCatalogReader(new[] { expiringMarket, normalMarket });
        var context = new StrategyContext
        {
            StrategyId = "dual_leg_arbitrage",
            ExecutionService = new NullExecutionService(),
            OrderBookReader = new FakeOrderBookReader(),
            MarketCatalog = catalog,
            RiskManager = new NullRiskManager(),
            DecisionLogger = new NullDecisionLogger()
        };

        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            MinTimeToExpiryMinutes = 60,
            MaxMarkets = 1
        };

        var strategy = new DualLegArbitrageStrategy(
            context,
            new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        // 模拟 expiring-market 上出现未对冲敞口（只成交一腿）
        await strategy.OnOrderUpdateAsync(
            CreateOrderUpdate(
                "expiring-market",
                "yes-1",
                OutcomeSide.Yes,
                StrategySignalType.Entry,
                filled: 2m,
                original: 2m,
                status: ExecutionStatus.Filled),
            CancellationToken.None);

        var markets = await strategy.SelectMarketsAsync(CancellationToken.None);
        var marketList = markets.ToList();

        // 必须强制纳入未对冲敞口市场（即使会被筛选条件排除，且 MaxMarkets=1）
        Assert.Single(marketList);
        Assert.Equal("expiring-market", marketList[0]);
    }

    [Fact]
    public async Task SelectMarkets_ForceIncludesAllUnhedgedMarkets_EvenWhenExceedingMaxMarkets()
    {
        var m1 = new MarketInfoDto
        {
            MarketId = "m1",
            ConditionId = "c1",
            Name = "M1",
            Status = "Active",
            Liquidity = 5000m,
            Volume24h = 1000m,
            TokenIds = new[] { "m1-yes", "m1-no" },
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        var m2 = new MarketInfoDto
        {
            MarketId = "m2",
            ConditionId = "c2",
            Name = "M2",
            Status = "Active",
            Liquidity = 5000m,
            Volume24h = 1000m,
            TokenIds = new[] { "m2-yes", "m2-no" },
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        var catalog = new FakeMarketCatalogReader(new[] { m1, m2 });
        var context = new StrategyContext
        {
            StrategyId = "dual_leg_arbitrage",
            ExecutionService = new NullExecutionService(),
            OrderBookReader = new FakeOrderBookReader(),
            MarketCatalog = catalog,
            RiskManager = new NullRiskManager(),
            DecisionLogger = new NullDecisionLogger()
        };

        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            MinTimeToExpiryMinutes = 60,
            MaxMarkets = 1
        };

        var strategy = new DualLegArbitrageStrategy(
            context,
            new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        // 两个市场都出现未对冲敞口：强制集数量 > MaxMarkets
        await strategy.OnOrderUpdateAsync(
            CreateOrderUpdate("m1", "m1-yes", OutcomeSide.Yes, StrategySignalType.Entry, 1m, 1m, ExecutionStatus.Filled),
            CancellationToken.None);

        await strategy.OnOrderUpdateAsync(
            CreateOrderUpdate("m2", "m2-yes", OutcomeSide.Yes, StrategySignalType.Entry, 1m, 1m, ExecutionStatus.Filled),
            CancellationToken.None);

        var markets = await strategy.SelectMarketsAsync(CancellationToken.None);
        var marketList = markets.ToList();

        Assert.Equal(2, marketList.Count);
        Assert.Contains("m1", marketList);
        Assert.Contains("m2", marketList);
    }

    [Fact]
    public async Task EvaluateEntry_SequentialMode_OnlySubmitsFirstLeg()
    {
        var context = BuildContext();
        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            PairCostThreshold = 0.9m,
            DefaultOrderQuantity = 2m,
            MinOrderQuantity = 1m,
            MaxNotionalPerOrder = 10m,
            SequentialOrderMode = true // 顺序模式
        };

        var strategy = new DualLegArbitrageStrategy(context, new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        var snapshot = CreateSnapshot(0.4m, 0.4m, 10m, 10m);

        var signal = await strategy.EvaluateEntryAsync(snapshot, CancellationToken.None);

        Assert.NotNull(signal);
        Assert.Equal(StrategySignalType.Entry, signal!.Type);
        Assert.Single(signal.Orders); // 顺序模式只下第一腿
    }

    [Fact]
    public async Task EvaluateEntry_SequentialMode_SubmitsSecondLegAfterFirstLegFilled()
    {
        var context = BuildContext();
        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            PairCostThreshold = 0.9m,
            DefaultOrderQuantity = 2m,
            MinOrderQuantity = 1m,
            MaxNotionalPerOrder = 10m,
            SequentialOrderMode = true
        };

        var strategy = new DualLegArbitrageStrategy(context, new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        var snapshot = CreateSnapshot(0.4m, 0.4m, 10m, 10m);

        // 第一次评估：下第一腿
        var signal1 = await strategy.EvaluateEntryAsync(snapshot, CancellationToken.None);
        Assert.NotNull(signal1);
        Assert.Single(signal1!.Orders);

        // 模拟第一腿成交
        var firstLegOutcome = signal1.Orders[0].Outcome;
        var firstLegUpdate = CreateOrderUpdate(firstLegOutcome, StrategySignalType.Entry, 2m, 2m, ExecutionStatus.Filled);
        await strategy.OnOrderUpdateAsync(firstLegUpdate, CancellationToken.None);

        // 第二次评估：下第二腿
        var signal2 = await strategy.EvaluateEntryAsync(snapshot, CancellationToken.None);
        Assert.NotNull(signal2);
        Assert.Single(signal2!.Orders);
        Assert.NotEqual(firstLegOutcome, signal2.Orders[0].Outcome); // 第二腿方向相反
    }

    [Fact]
    public async Task EvaluateEntry_SequentialMode_HedgesPartialFillQuantity()
    {
        var context = BuildContext();
        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            PairCostThreshold = 0.9m,
            DefaultOrderQuantity = 5m,
            MinOrderQuantity = 1m,
            MaxNotionalPerOrder = 10m,
            SequentialOrderMode = true
        };

        var strategy = new DualLegArbitrageStrategy(context, new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        var snapshot = CreateSnapshot(0.4m, 0.4m, 10m, 10m);

        var signal1 = await strategy.EvaluateEntryAsync(snapshot, CancellationToken.None);
        Assert.NotNull(signal1);

        var firstLegOutcome = signal1!.Orders[0].Outcome;
        var firstLegUpdate = CreateOrderUpdate(firstLegOutcome, StrategySignalType.Entry, 1m, 2m, ExecutionStatus.PartiallyFilled);
        await strategy.OnOrderUpdateAsync(firstLegUpdate, CancellationToken.None);

        var hedgeSignal = await strategy.EvaluateEntryAsync(snapshot, CancellationToken.None);

        Assert.NotNull(hedgeSignal);
        Assert.Single(hedgeSignal!.Orders);
        Assert.NotEqual(firstLegOutcome, hedgeSignal.Orders[0].Outcome);
        Assert.Equal(1m, hedgeSignal.Orders[0].Quantity);
    }

    [Fact]
    public async Task EvaluateEntry_RespectsMaxNotionalPerMarket()
    {
        var context = BuildContext();
        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            PairCostThreshold = 0.9m,
            DefaultOrderQuantity = 100m, // 大数量
            MinOrderQuantity = 1m,
            MaxNotionalPerOrder = 1000m, // 高单笔限制
            MaxNotionalPerMarket = 10m, // 低市场限制
            SequentialOrderMode = false
        };

        var strategy = new DualLegArbitrageStrategy(context, new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        var snapshot = CreateSnapshot(0.4m, 0.4m, 100m, 100m);

        var signal = await strategy.EvaluateEntryAsync(snapshot, CancellationToken.None);

        Assert.NotNull(signal);
        // 数量应该受 MaxNotionalPerMarket 限制
        // MaxNotionalPerMarket / (yesPrice + noPrice) = 10 / 0.8 = 12.5
        var qty = signal!.Orders[0].Quantity;
        Assert.True(qty <= 12.5m, $"Quantity {qty} should be limited by MaxNotionalPerMarket");
    }

    [Fact]
    public async Task EvaluateExit_HedgeTimeout_ReturnsExitSignal_WhenCancelAndExit()
    {
        var context = BuildContext();
        var options = new DualLegArbitrageOptions
        {
            Enabled = true,
            PairCostThreshold = 0.9m,
            DefaultOrderQuantity = 2m,
            MinOrderQuantity = 1m,
            MaxNotionalPerOrder = 10m,
            HedgeTimeoutSeconds = 0, // 立即超时
            HedgeTimeoutAction = UnhedgedExitAction.CancelAndExit,
            SequentialOrderMode = false
        };

        var strategy = new DualLegArbitrageStrategy(context, new StaticOptionsMonitor<DualLegArbitrageOptions>(options),
            NullLogger<DualLegArbitrageStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        // 触发入场
        var entrySnapshot = CreateSnapshot(0.4m, 0.4m, 10m, 10m);
        await strategy.EvaluateEntryAsync(entrySnapshot, CancellationToken.None);

        // 只有 Yes 腿成交（未对冲敞口）
        var yesUpdate = CreateOrderUpdate(OutcomeSide.Yes, StrategySignalType.Entry, 2m, 2m, ExecutionStatus.Filled);
        await strategy.OnOrderUpdateAsync(yesUpdate, CancellationToken.None);

        // 评估出场时应触发超时处理
        var exitSnapshot = CreateSnapshot(0.4m, 0.4m, 10m, 10m, bidYes: 0.35m, bidNo: 0.35m);
        var exitSignal = await strategy.EvaluateExitAsync(exitSnapshot, CancellationToken.None);

        Assert.NotNull(exitSignal);
        Assert.Equal(StrategySignalType.Exit, exitSignal!.Type);
        Assert.Single(exitSignal.Orders);
        Assert.Equal(OutcomeSide.Yes, exitSignal.Orders[0].Outcome); // 退出已成交的 Yes 腿
        Assert.Equal(OrderSide.Sell, exitSignal.Orders[0].Side);
    }

    private static StrategyContext BuildContext()
    {
        var market = new MarketInfoDto
        {
            MarketId = "mkt-1",
            ConditionId = "cond-1",
            Name = "Test Market",
            Status = "Active",
            TokenIds = new[] { "yes-token", "no-token" }
        };

        var catalog = new FakeMarketCatalogReader(market);
        var orderBook = new FakeOrderBookReader();
        var context = new StrategyContext
        {
            StrategyId = "dual_leg_arbitrage",
            ExecutionService = new NullExecutionService(),
            OrderBookReader = orderBook,
            MarketCatalog = catalog,
            RiskManager = new NullRiskManager(),
            DecisionLogger = new NullDecisionLogger()
        };

        return context;
    }

    private static MarketSnapshot CreateSnapshot(
        decimal askYes,
        decimal askNo,
        decimal askYesSize,
        decimal askNoSize,
        decimal? bidYes = null,
        decimal? bidNo = null)
    {
        var market = new MarketInfoDto
        {
            MarketId = "mkt-1",
            ConditionId = "cond-1",
            Name = "Test Market",
            Status = "Active",
            TokenIds = new[] { "yes-token", "no-token" }
        };

        var yesTop = new TopOfBookDto(
            "yes-token",
            bidYes.HasValue ? new Autotrade.MarketData.Domain.Shared.ValueObjects.Price(bidYes.Value) : null,
            null,
            new Autotrade.MarketData.Domain.Shared.ValueObjects.Price(askYes),
            new Autotrade.MarketData.Domain.Shared.ValueObjects.Quantity(askYesSize),
            null,
            DateTimeOffset.UtcNow);

        var noTop = new TopOfBookDto(
            "no-token",
            bidNo.HasValue ? new Autotrade.MarketData.Domain.Shared.ValueObjects.Price(bidNo.Value) : null,
            null,
            new Autotrade.MarketData.Domain.Shared.ValueObjects.Price(askNo),
            new Autotrade.MarketData.Domain.Shared.ValueObjects.Quantity(askNoSize),
            null,
            DateTimeOffset.UtcNow);

        return new MarketSnapshot(market, yesTop, noTop, DateTimeOffset.UtcNow);
    }

    private static StrategyOrderUpdate CreateOrderUpdate(
        OutcomeSide outcome,
        StrategySignalType signalType,
        decimal filled,
        decimal original,
        ExecutionStatus status)
    {
        return new StrategyOrderUpdate(
            "dual_leg_arbitrage",
            Guid.NewGuid().ToString("N"),
            "mkt-1",
            outcome == OutcomeSide.Yes ? "yes-token" : "no-token",
            outcome,
            outcome == OutcomeSide.Yes ? OrderLeg.First : OrderLeg.Second,
            signalType,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Gtc,
            0.5m,
            status,
            filled,
            original,
            DateTimeOffset.UtcNow);
    }

    private static StrategyOrderUpdate CreateOrderUpdate(
        string marketId,
        string tokenId,
        OutcomeSide outcome,
        StrategySignalType signalType,
        decimal filled,
        decimal original,
        ExecutionStatus status)
    {
        return new StrategyOrderUpdate(
            "dual_leg_arbitrage",
            Guid.NewGuid().ToString("N"),
            marketId,
            tokenId,
            outcome,
            outcome == OutcomeSide.Yes ? OrderLeg.First : OrderLeg.Second,
            signalType,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Gtc,
            0.5m,
            status,
            filled,
            original,
            DateTimeOffset.UtcNow);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string?> listener) => new NullDisposable();

        private sealed class NullDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class FakeMarketCatalogReader : IMarketCatalogReader
    {
        private readonly IReadOnlyList<MarketInfoDto> _markets;

        public FakeMarketCatalogReader(MarketInfoDto market) : this(new[] { market })
        {
        }

        public FakeMarketCatalogReader(IEnumerable<MarketInfoDto> markets)
        {
            _markets = markets.ToArray();
        }

        public MarketInfoDto? GetMarket(string marketId)
            => _markets.FirstOrDefault(m => string.Equals(m.MarketId, marketId, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<MarketInfoDto> GetAllMarkets() => _markets;

        public IReadOnlyList<MarketInfoDto> GetActiveMarkets() => _markets;

        public IReadOnlyList<MarketInfoDto> GetLiquidMarkets(decimal minVolume)
            => _markets.Where(m => m.Volume24h >= minVolume).ToArray();

        public IReadOnlyList<MarketInfoDto> GetExpiringMarkets(TimeSpan within)
            => _markets.Where(m => m.ExpiresAtUtc.HasValue && m.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow.Add(within)).ToArray();
    }

    private sealed class FakeOrderBookReader : IOrderBookReader
    {
        public TopOfBookDto? GetTopOfBook(string assetId) => null;

        public IReadOnlyList<PriceLevelDto> GetDepth(string assetId, int levels = 10)
            => Array.Empty<PriceLevelDto>();
    }

    private sealed class NullExecutionService : IExecutionService
    {
        public Task<ExecutionResult> PlaceOrderAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionResult.Fail(request.ClientOrderId, "NOT_IMPLEMENTED", "Not implemented"));

        public Task<IReadOnlyList<ExecutionResult>> PlaceOrdersAsync(
            IReadOnlyList<ExecutionRequest> requests,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ExecutionResult>(requests.Count);
            foreach (var request in requests)
            {
                results.Add(ExecutionResult.Fail(request.ClientOrderId, "NOT_IMPLEMENTED", "Not implemented"));
            }

            return Task.FromResult<IReadOnlyList<ExecutionResult>>(results);
        }

        public Task<ExecutionResult> CancelOrderAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionResult.Fail(clientOrderId, "NOT_IMPLEMENTED", "Not implemented"));

        public Task<OrderStatusResult> GetOrderStatusAsync(string clientOrderId, CancellationToken cancellationToken = default)
            => Task.FromResult(OrderStatusResult.NotFound(clientOrderId));
    }

    private sealed class NullDecisionLogger : IStrategyDecisionLogger
    {
        public Task LogAsync(StrategyDecision decision, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NullRiskManager : IRiskManager
    {
        public Task<RiskCheckResult> ValidateOrderAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(RiskCheckResult.Allow());

        public Task RecordOrderAcceptedAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordOrderUpdateAsync(RiskOrderUpdate update, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordOrderErrorAsync(string strategyId, string clientOrderId, string errorCode, string message,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateKillSwitchAsync(string reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateKillSwitchAsync(KillSwitchLevel level, string reasonCode, string reason, string? contextJson = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateStrategyKillSwitchAsync(string strategyId, KillSwitchLevel level, string reasonCode, string reason,
            string? marketId = null, string? contextJson = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResetKillSwitchAsync(string? strategyId = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public bool IsKillSwitchActive => false;

        public KillSwitchState GetKillSwitchState() => KillSwitchState.Inactive;

        public KillSwitchState GetStrategyKillSwitchState(string strategyId) => KillSwitchState.Inactive;

        public bool IsStrategyBlocked(string strategyId) => false;

        public IReadOnlyList<KillSwitchState> GetAllActiveKillSwitches() => Array.Empty<KillSwitchState>();

        public IReadOnlyList<string> GetOpenOrderIds() => Array.Empty<string>();

        public IReadOnlyList<string> GetOpenOrderIds(string strategyId) => Array.Empty<string>();

        public IReadOnlyList<UnhedgedExposureSnapshot> GetExpiredUnhedgedExposures(DateTimeOffset nowUtc)
            => Array.Empty<UnhedgedExposureSnapshot>();

        public Task RecordUnhedgedExposureAsync(
            string strategyId,
            string marketId,
            string tokenId,
            string hedgeTokenId,
            OutcomeSide outcome,
            OrderSide side,
            decimal quantity,
            decimal price,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearUnhedgedExposureAsync(string strategyId, string marketId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public RiskStateSnapshot GetStateSnapshot() => new(0m, 0, 0m, 0m, 0m,
            new Dictionary<string, decimal>(),
            new Dictionary<string, decimal>(),
            new Dictionary<string, int>(),
            Array.Empty<UnhedgedExposureSnapshot>());
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public static readonly NullLogger<T> Instance = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NullDisposable();

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NullDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
