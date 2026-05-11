using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Application.Contract.Snapshots;
using Autotrade.MarketData.Application.Contract.Spot;
using Autotrade.MarketData.Application.Contract.Windows;
using Autotrade.MarketData.Domain.Shared.ValueObjects;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Strategies.RepricingLag;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Tests;

public sealed class RepricingLagArbitrageStrategyTests
{
    [Fact]
    public async Task EvaluateEntry_ReturnsSignal_WhenSpotMoveConfirmedAndMarketStillMispriced()
    {
        var market = CreateMarket();
        var snapshotReader = new FakeMarketDataSnapshotReader(CreateUnifiedSnapshot(
            market,
            MarketWindowOracleStatus.Configured,
            baselinePrice: 100m,
            latestPrice: 101m,
            yesAsk: 0.50m,
            noAsk: 0.70m));
        var context = CreateContext(snapshotReader, market);
        var options = new RepricingLagArbitrageOptions
        {
            Enabled = true,
            RequireConfirmedOracle = false,
            TriggerKillSwitchOnOracleMismatch = false,
            ConfirmWaitDurationSeconds = 0,
            MinMoveBps = 35m,
            MinEdge = 0.05m,
            DefaultOrderQuantity = 2m,
            MinOrderQuantity = 1m
        };
        var strategy = new RepricingLagArbitrageStrategy(
            context,
            new StaticOptionsMonitor<RepricingLagArbitrageOptions>(options),
            NullLogger<RepricingLagArbitrageStrategy>.Instance);

        await strategy.StartAsync();
        var signal = await strategy.EvaluateEntryAsync(ToMarketSnapshot(snapshotReader.Snapshot));

        Assert.NotNull(signal);
        Assert.Equal(StrategySignalType.Entry, signal.Type);
        var order = Assert.Single(signal.Orders);
        Assert.Equal(OutcomeSide.Yes, order.Outcome);
        Assert.Equal("yes-token", order.TokenId);
        Assert.Equal(OrderSide.Buy, order.Side);
        Assert.Equal(TimeInForce.Fok, order.TimeInForce);
        Assert.True(order.Price > 0.50m);
        Assert.Contains("moveBps", signal.ContextJson);
    }

    [Fact]
    public async Task EvaluateEntry_SafetyRejectsAndTriggersKillSwitch_WhenConfirmedOracleRequired()
    {
        var market = CreateMarket();
        var snapshotReader = new FakeMarketDataSnapshotReader(CreateUnifiedSnapshot(
            market,
            MarketWindowOracleStatus.Configured,
            baselinePrice: 100m,
            latestPrice: 101m,
            yesAsk: 0.50m,
            noAsk: 0.70m));
        var risk = new CapturingRiskManager();
        var decisionLogger = new CapturingDecisionLogger();
        var context = CreateContext(snapshotReader, market, risk, decisionLogger);
        var options = new RepricingLagArbitrageOptions
        {
            Enabled = true,
            RequireConfirmedOracle = true,
            TriggerKillSwitchOnOracleMismatch = true,
            ConfirmWaitDurationSeconds = 0
        };
        var strategy = new RepricingLagArbitrageStrategy(
            context,
            new StaticOptionsMonitor<RepricingLagArbitrageOptions>(options),
            NullLogger<RepricingLagArbitrageStrategy>.Instance);

        await strategy.StartAsync();
        var signal = await strategy.EvaluateEntryAsync(ToMarketSnapshot(snapshotReader.Snapshot));

        Assert.Null(signal);
        Assert.True(risk.StrategyKillSwitchActivated);
        var decision = Assert.Single(decisionLogger.Decisions);
        Assert.Equal("SafetyRejected", decision.Action);
    }

    [Fact]
    public async Task EvaluateEntry_SafetyRejects_WhenBaselineSpotSourceIsNotAllowed()
    {
        var market = CreateMarket();
        var snapshotReader = new FakeMarketDataSnapshotReader(CreateUnifiedSnapshot(
            market,
            MarketWindowOracleStatus.Confirmed,
            baselinePrice: 100m,
            latestPrice: 101m,
            yesAsk: 0.50m,
            noAsk: 0.70m,
            baselineSource: "rtds:untrusted"));
        var decisionLogger = new CapturingDecisionLogger();
        var context = CreateContext(snapshotReader, market, decisionLogger: decisionLogger);
        var options = new RepricingLagArbitrageOptions
        {
            Enabled = true,
            RequireConfirmedOracle = false,
            TriggerKillSwitchOnOracleMismatch = false,
            ConfirmWaitDurationSeconds = 0,
            AllowedSpotSources = ["rtds:crypto_prices"]
        };
        var strategy = new RepricingLagArbitrageStrategy(
            context,
            new StaticOptionsMonitor<RepricingLagArbitrageOptions>(options),
            NullLogger<RepricingLagArbitrageStrategy>.Instance);

        await strategy.StartAsync();
        var signal = await strategy.EvaluateEntryAsync(ToMarketSnapshot(snapshotReader.Snapshot));

        Assert.Null(signal);
        var decision = Assert.Single(decisionLogger.Decisions);
        Assert.Equal("SafetyRejected", decision.Action);
        Assert.Contains("Baseline spot source", decision.Reason);
    }

    [Fact]
    public async Task EvaluateExit_DoesNotExitFilledPosition_WhenOnlyEntryOrderAgeExpired()
    {
        var market = CreateMarket();
        var entrySnapshot = CreateUnifiedSnapshot(
            market,
            MarketWindowOracleStatus.Confirmed,
            baselinePrice: 100m,
            latestPrice: 101m,
            yesAsk: 0.50m,
            noAsk: 0.70m,
            timestampUtc: new DateTimeOffset(2026, 5, 4, 8, 5, 0, TimeSpan.Zero));
        var snapshotReader = new FakeMarketDataSnapshotReader(entrySnapshot);
        var context = CreateContext(snapshotReader, market);
        var options = new RepricingLagArbitrageOptions
        {
            Enabled = true,
            RequireConfirmedOracle = false,
            TriggerKillSwitchOnOracleMismatch = false,
            ConfirmWaitDurationSeconds = 0,
            MaxOrderAgeSeconds = 8,
            MaxHoldSeconds = 600,
            MinMoveBps = 35m,
            MinEdge = 0.05m
        };
        var strategy = new RepricingLagArbitrageStrategy(
            context,
            new StaticOptionsMonitor<RepricingLagArbitrageOptions>(options),
            NullLogger<RepricingLagArbitrageStrategy>.Instance);

        await strategy.StartAsync();
        var entrySignal = await strategy.EvaluateEntryAsync(ToMarketSnapshot(snapshotReader.Snapshot));
        var entryOrder = Assert.Single(entrySignal!.Orders);
        await strategy.OnOrderUpdateAsync(new StrategyOrderUpdate(
            "repricing_lag_arbitrage",
            "entry-1",
            market.MarketId,
            entryOrder.TokenId,
            entryOrder.Outcome,
            entryOrder.Leg,
            StrategySignalType.Entry,
            entryOrder.Side,
            entryOrder.OrderType,
            entryOrder.TimeInForce,
            entryOrder.Price,
            ExecutionStatus.Filled,
            entryOrder.Quantity,
            entryOrder.Quantity,
            entrySnapshot.TimestampUtc));

        snapshotReader.Snapshot = CreateUnifiedSnapshot(
            market,
            MarketWindowOracleStatus.Confirmed,
            baselinePrice: 100m,
            latestPrice: 101m,
            yesAsk: 0.90m,
            noAsk: 0.10m,
            timestampUtc: entrySnapshot.TimestampUtc.AddSeconds(options.MaxOrderAgeSeconds + 1));

        var exitSignal = await strategy.EvaluateExitAsync(ToMarketSnapshot(snapshotReader.Snapshot));

        Assert.Null(exitSignal);
    }

    [Fact]
    public void ReplayRunner_ProducesDeterministicLagSummary()
    {
        var market = CreateMarket();
        var spec = CreateSpec(market, MarketWindowOracleStatus.Confirmed);
        var options = new RepricingLagArbitrageOptions
        {
            ConfirmWaitDurationSeconds = 0,
            MinMoveBps = 35m,
            MinEdge = 0.05m
        };
        var runner = new RepricingLagReplayRunner();

        var summary = runner.Run(
            spec,
            new[]
            {
                new RepricingLagReplayFrame(spec.WindowStartUtc, 100m, 0.50m, 0.50m, 0.48m, 0.48m),
                new RepricingLagReplayFrame(spec.WindowStartUtc.AddSeconds(10), 101m, 0.52m, 0.70m, 0.50m, 0.68m),
                new RepricingLagReplayFrame(spec.WindowEndUtc.AddSeconds(-1), 101.2m, 0.90m, 0.10m, 0.88m, 0.08m)
            },
            options);

        Assert.Equal(3, summary.FrameCount);
        Assert.True(summary.DetectedSignals >= 1);
        Assert.Equal(1m, summary.RealizedWinRate);
        Assert.All(summary.Signals, signal => Assert.Equal(OutcomeSide.Yes, signal.Outcome));
    }

    private static StrategyContext CreateContext(
        IMarketDataSnapshotReader snapshotReader,
        MarketInfoDto market,
        IRiskManager? risk = null,
        IStrategyDecisionLogger? decisionLogger = null)
        => new()
        {
            StrategyId = "repricing_lag_arbitrage",
            ExecutionService = new NullExecutionService(),
            OrderBookReader = new FakeOrderBookReader(),
            MarketCatalog = new FakeMarketCatalogReader(market),
            MarketDataSnapshotReader = snapshotReader,
            RiskManager = risk ?? new CapturingRiskManager(),
            DecisionLogger = decisionLogger ?? new CapturingDecisionLogger()
        };

    private static UnifiedMarketDataSnapshot CreateUnifiedSnapshot(
        MarketInfoDto market,
        MarketWindowOracleStatus oracleStatus,
        decimal baselinePrice,
        decimal latestPrice,
        decimal yesAsk,
        decimal noAsk,
        DateTimeOffset? timestampUtc = null,
        string latestSource = "rtds:crypto_prices",
        string baselineSource = "rtds:crypto_prices")
    {
        var spec = CreateSpec(market, oracleStatus);
        var now = timestampUtc ?? spec.WindowStartUtc.AddMinutes(5);

        return new UnifiedMarketDataSnapshot(
            market,
            spec,
            new SpotPriceTick("BTCUSDT", latestPrice, now, latestSource),
            new SpotPriceTick("BTCUSDT", baselinePrice, spec.WindowStartUtc, baselineSource),
            Top("yes-token", yesAsk - 0.02m, 10m, yesAsk, 10m, now),
            Top("no-token", noAsk - 0.02m, 10m, noAsk, 10m, now),
            [new PriceLevelDto(yesAsk, 10m, false)],
            [new PriceLevelDto(noAsk, 10m, false)],
            new MarketDataStaleness(true, TimeSpan.FromSeconds(1), null),
            new MarketDataStaleness(true, TimeSpan.Zero, null),
            new MarketDataStaleness(true, TimeSpan.FromSeconds(1), null),
            now);
    }

    private static MarketWindowSpec CreateSpec(MarketInfoDto market, MarketWindowOracleStatus oracleStatus)
        => new(
            market.MarketId,
            market.Slug,
            MarketWindowType.CryptoUpDown15m,
            new DateTimeOffset(2026, 5, 4, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 4, 8, 15, 0, TimeSpan.Zero),
            "BTCUSDT",
            MarketWindowBoundaryPolicy.StartPriceVersusEndPrice,
            "polymarket-rtds-crypto-prices",
            "start-vs-end",
            oracleStatus,
            new MarketWindowTokenMap("yes-token", "no-token"));

    private static MarketSnapshot ToMarketSnapshot(UnifiedMarketDataSnapshot snapshot)
        => new(snapshot.Market, snapshot.YesTopOfBook, snapshot.NoTopOfBook, snapshot.TimestampUtc);

    private static TopOfBookDto Top(
        string assetId,
        decimal bid,
        decimal bidSize,
        decimal ask,
        decimal askSize,
        DateTimeOffset timestampUtc)
        => new(
            assetId,
            new Price(bid),
            new Quantity(bidSize),
            new Price(ask),
            new Quantity(askSize),
            ask - bid,
            timestampUtc);

    private static MarketInfoDto CreateMarket()
        => new()
        {
            MarketId = "market-1",
            ConditionId = "condition-1",
            Name = "Bitcoin Up or Down - May 4, 8:00AM-8:15AM",
            Slug = "btc-updown-15m-1777881600",
            Status = "Active",
            Liquidity = 1000m,
            Volume24h = 500m,
            TokenIds = ["yes-token", "no-token"]
        };

    private sealed class FakeMarketDataSnapshotReader : IMarketDataSnapshotReader
    {
        public FakeMarketDataSnapshotReader(UnifiedMarketDataSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public UnifiedMarketDataSnapshot Snapshot { get; set; }

        public UnifiedMarketDataSnapshot? GetSnapshot(
            string marketId,
            TimeSpan maxSpotAge,
            TimeSpan maxOrderBookAge,
            int depthLevels = 10,
            TimeSpan? maxBaselineSpotAge = null)
            => string.Equals(marketId, Snapshot.Market.MarketId, StringComparison.OrdinalIgnoreCase) ? Snapshot : null;

        public MarketWindowSpec? GetWindowSpec(string marketId)
            => string.Equals(marketId, Snapshot.Market.MarketId, StringComparison.OrdinalIgnoreCase)
                ? Snapshot.WindowSpec
                : null;
    }

    private sealed class FakeMarketCatalogReader : IMarketCatalogReader
    {
        private readonly MarketInfoDto _market;

        public FakeMarketCatalogReader(MarketInfoDto market)
        {
            _market = market;
        }

        public MarketInfoDto? GetMarket(string marketId) => _market;

        public IReadOnlyList<MarketInfoDto> GetAllMarkets() => [_market];

        public IReadOnlyList<MarketInfoDto> GetActiveMarkets() => [_market];

        public IReadOnlyList<MarketInfoDto> GetLiquidMarkets(decimal minVolume) => [_market];

        public IReadOnlyList<MarketInfoDto> GetExpiringMarkets(TimeSpan within) => [_market];
    }

    private sealed class FakeOrderBookReader : IOrderBookReader
    {
        public TopOfBookDto? GetTopOfBook(string assetId) => null;

        public IReadOnlyList<PriceLevelDto> GetDepth(string assetId, int levels = 10) => [];
    }

    private sealed class NullExecutionService : IExecutionService
    {
        public Task<ExecutionResult> PlaceOrderAsync(
            ExecutionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionResult.Succeed(request.ClientOrderId, "exchange"));

        public Task<IReadOnlyList<ExecutionResult>> PlaceOrdersAsync(
            IReadOnlyList<ExecutionRequest> requests,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExecutionResult>>(requests
                .Select(request => ExecutionResult.Succeed(request.ClientOrderId, "exchange"))
                .ToArray());

        public Task<ExecutionResult> CancelOrderAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionResult.Succeed(clientOrderId, "exchange", ExecutionStatus.Cancelled));

        public Task<OrderStatusResult> GetOrderStatusAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OrderStatusResult.NotFound(clientOrderId));
    }

    private sealed class CapturingDecisionLogger : IStrategyDecisionLogger
    {
        public List<StrategyDecision> Decisions { get; } = [];

        public Task LogAsync(StrategyDecision decision, CancellationToken cancellationToken = default)
        {
            Decisions.Add(decision);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingRiskManager : IRiskManager
    {
        public bool StrategyKillSwitchActivated { get; private set; }

        public Task<RiskCheckResult> ValidateOrderAsync(
            RiskOrderRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(RiskCheckResult.Allow());

        public Task RecordOrderAcceptedAsync(RiskOrderRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordOrderUpdateAsync(RiskOrderUpdate update, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordOrderErrorAsync(
            string strategyId,
            string clientOrderId,
            string errorCode,
            string message,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateKillSwitchAsync(string reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateKillSwitchAsync(
            KillSwitchLevel level,
            string reasonCode,
            string reason,
            string? contextJson = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateStrategyKillSwitchAsync(
            string strategyId,
            KillSwitchLevel level,
            string reasonCode,
            string reason,
            string? marketId = null,
            string? contextJson = null,
            CancellationToken cancellationToken = default)
        {
            StrategyKillSwitchActivated = true;
            return Task.CompletedTask;
        }

        public Task ResetKillSwitchAsync(string? strategyId = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public bool IsKillSwitchActive => false;

        public KillSwitchState GetKillSwitchState() => KillSwitchState.Inactive;

        public KillSwitchState GetStrategyKillSwitchState(string strategyId) => KillSwitchState.Inactive;

        public bool IsStrategyBlocked(string strategyId) => false;

        public IReadOnlyList<KillSwitchState> GetAllActiveKillSwitches() => [];

        public IReadOnlyList<string> GetOpenOrderIds() => [];

        public IReadOnlyList<string> GetOpenOrderIds(string strategyId) => [];

        public IReadOnlyList<UnhedgedExposureSnapshot> GetExpiredUnhedgedExposures(DateTimeOffset nowUtc) => [];

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

        public Task ClearUnhedgedExposureAsync(
            string strategyId,
            string marketId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public RiskStateSnapshot GetStateSnapshot()
            => new(0m, 0, 0m, 0m, 0m,
                new Dictionary<string, decimal>(),
                new Dictionary<string, decimal>(),
                new Dictionary<string, int>(),
                []);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;

        public StaticOptionsMonitor(T value)
        {
            _value = value;
        }

        public T CurrentValue => _value;

        public T Get(string? name) => _value;

        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
