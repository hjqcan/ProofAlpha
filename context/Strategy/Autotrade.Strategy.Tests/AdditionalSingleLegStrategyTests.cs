using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Domain.Shared.ValueObjects;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.Strategies.LiquidityMaking;
using Autotrade.Strategy.Application.Strategies.LiquidityPulse;
using Autotrade.Strategy.Application.Strategies.Volatility;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Autotrade.Strategy.Tests;

public sealed class AdditionalSingleLegStrategyTests
{
    [Fact]
    public async Task LiquidityPulse_ReturnsBuySignal_WhenBidDepthDominatesTightBook()
    {
        var strategy = new LiquidityPulseStrategy(
            CreateContext("liquidity_pulse"),
            new StaticOptionsMonitor<LiquidityPulseOptions>(new LiquidityPulseOptions
            {
                MinLiquidity = 0m,
                MinVolume24h = 0m,
                MinBidDominance = 0.70m,
                MaxSpread = 0.05m,
                EntryCooldownSeconds = 0
            }),
            NullLogger<LiquidityPulseStrategy>.Instance);

        await strategy.StartAsync();

        var signal = await strategy.EvaluateEntryAsync(CreateSnapshot(
            yesBid: 0.44m,
            yesAsk: 0.46m,
            yesBidSize: 15m,
            yesAskSize: 5m,
            noBid: 0.52m,
            noAsk: 0.55m,
            noBidSize: 2m,
            noAskSize: 10m));

        Assert.NotNull(signal);
        Assert.Equal(StrategySignalType.Entry, signal.Type);
        var order = Assert.Single(signal.Orders);
        Assert.Equal(OutcomeSide.Yes, order.Outcome);
        Assert.Equal(OrderSide.Buy, order.Side);
        Assert.Equal(TimeInForce.Fok, order.TimeInForce);
        Assert.True(order.Price >= 0.46m);
    }

    [Fact]
    public async Task LiquidityPulse_ReturnsExitSignal_WhenPositionReachesTakeProfit()
    {
        var strategy = new LiquidityPulseStrategy(
            CreateContext("liquidity_pulse"),
            new StaticOptionsMonitor<LiquidityPulseOptions>(new LiquidityPulseOptions
            {
                MinLiquidity = 0m,
                MinVolume24h = 0m,
                TakeProfitPriceDelta = 0.02m,
                ExitCooldownSeconds = 0,
                MaxSlippage = 0m
            }),
            NullLogger<LiquidityPulseStrategy>.Instance);

        await strategy.StartAsync();
        await strategy.OnOrderUpdateAsync(CreateEntryFillUpdate("liquidity_pulse", OutcomeSide.Yes, 0.40m, 2m));

        var signal = await strategy.EvaluateExitAsync(CreateSnapshot(
            yesBid: 0.43m,
            yesAsk: 0.45m,
            yesBidSize: 10m,
            yesAskSize: 6m,
            noBid: 0.54m,
            noAsk: 0.57m,
            noBidSize: 4m,
            noAskSize: 8m));

        AssertTakeProfitExit(signal, OutcomeSide.Yes, 0.43m);
    }

    [Fact]
    public async Task LiquidityMaker_ReturnsPassiveGtcQuote_WhenSpreadIsTradeable()
    {
        var strategy = new LiquidityMakerStrategy(
            CreateContext("liquidity_maker"),
            new StaticOptionsMonitor<LiquidityMakerOptions>(new LiquidityMakerOptions
            {
                MinLiquidity = 0m,
                MinVolume24h = 0m,
                MinSpread = 0.02m,
                MaxSpread = 0.08m,
                QuoteImproveTicks = 0.001m,
                EntryCooldownSeconds = 0
            }),
            NullLogger<LiquidityMakerStrategy>.Instance);

        await strategy.StartAsync();

        var signal = await strategy.EvaluateEntryAsync(CreateSnapshot(
            yesBid: 0.40m,
            yesAsk: 0.45m,
            yesBidSize: 20m,
            yesAskSize: 10m,
            noBid: 0.51m,
            noAsk: 0.54m,
            noBidSize: 5m,
            noAskSize: 5m));

        Assert.NotNull(signal);
        var order = Assert.Single(signal.Orders);
        Assert.Equal(OutcomeSide.Yes, order.Outcome);
        Assert.Equal(TimeInForce.Gtc, order.TimeInForce);
        Assert.Equal(0.401m, order.Price);
        Assert.True(order.Price < 0.45m);
    }

    [Fact]
    public async Task LiquidityMaker_ReturnsExitSignal_WhenPositionReachesTakeProfit()
    {
        var strategy = new LiquidityMakerStrategy(
            CreateContext("liquidity_maker"),
            new StaticOptionsMonitor<LiquidityMakerOptions>(new LiquidityMakerOptions
            {
                MinLiquidity = 0m,
                MinVolume24h = 0m,
                TakeProfitPriceDelta = 0.02m,
                ExitCooldownSeconds = 0,
                MaxPassiveOrderAgeSeconds = 0,
                MaxSlippage = 0m
            }),
            NullLogger<LiquidityMakerStrategy>.Instance);

        await strategy.StartAsync();
        await strategy.OnOrderUpdateAsync(CreateEntryFillUpdate("liquidity_maker", OutcomeSide.Yes, 0.40m, 2m));

        var signal = await strategy.EvaluateExitAsync(CreateSnapshot(
            yesBid: 0.43m,
            yesAsk: 0.45m,
            yesBidSize: 10m,
            yesAskSize: 6m,
            noBid: 0.54m,
            noAsk: 0.57m,
            noBidSize: 4m,
            noAskSize: 8m));

        AssertTakeProfitExit(signal, OutcomeSide.Yes, 0.43m);
    }

    [Fact]
    public async Task MicroVolatilityScalper_ReturnsBuySignal_WhenAskDipsBelowRollingAverage()
    {
        var strategy = new MicroVolatilityScalperStrategy(
            CreateContext("micro_volatility_scalper"),
            new StaticOptionsMonitor<MicroVolatilityScalperOptions>(new MicroVolatilityScalperOptions
            {
                MinLiquidity = 0m,
                MinVolume24h = 0m,
                SampleWindowSize = 4,
                MinSamples = 3,
                MinDipFromAverage = 0.03m,
                EntryCooldownSeconds = 0
            }),
            NullLogger<MicroVolatilityScalperStrategy>.Instance);

        await strategy.StartAsync();

        for (var i = 0; i < 3; i++)
        {
            var warmupSignal = await strategy.EvaluateEntryAsync(CreateSnapshot(
                yesBid: 0.48m,
                yesAsk: 0.50m,
                yesBidSize: 10m,
                yesAskSize: 10m,
                noBid: 0.49m,
                noAsk: 0.51m,
                noBidSize: 10m,
                noAskSize: 10m));
            Assert.Null(warmupSignal);
        }

        var signal = await strategy.EvaluateEntryAsync(CreateSnapshot(
            yesBid: 0.40m,
            yesAsk: 0.42m,
            yesBidSize: 12m,
            yesAskSize: 12m,
            noBid: 0.57m,
            noAsk: 0.59m,
            noBidSize: 12m,
            noAskSize: 12m));

        Assert.NotNull(signal);
        var order = Assert.Single(signal.Orders);
        Assert.Equal(OutcomeSide.Yes, order.Outcome);
        Assert.Equal(OrderSide.Buy, order.Side);
        Assert.Equal(TimeInForce.Fok, order.TimeInForce);
        Assert.True(order.Price >= 0.42m);
    }

    [Fact]
    public async Task MicroVolatilityScalper_ReturnsExitSignal_WhenPositionReachesTakeProfit()
    {
        var strategy = new MicroVolatilityScalperStrategy(
            CreateContext("micro_volatility_scalper"),
            new StaticOptionsMonitor<MicroVolatilityScalperOptions>(new MicroVolatilityScalperOptions
            {
                MinLiquidity = 0m,
                MinVolume24h = 0m,
                TakeProfitPriceDelta = 0.02m,
                ExitCooldownSeconds = 0,
                MaxSlippage = 0m
            }),
            NullLogger<MicroVolatilityScalperStrategy>.Instance);

        await strategy.StartAsync();
        await strategy.OnOrderUpdateAsync(CreateEntryFillUpdate("micro_volatility_scalper", OutcomeSide.Yes, 0.40m, 2m));

        var signal = await strategy.EvaluateExitAsync(CreateSnapshot(
            yesBid: 0.43m,
            yesAsk: 0.45m,
            yesBidSize: 10m,
            yesAskSize: 6m,
            noBid: 0.54m,
            noAsk: 0.57m,
            noBidSize: 4m,
            noAskSize: 8m));

        AssertTakeProfitExit(signal, OutcomeSide.Yes, 0.43m);
    }

    private static StrategyContext CreateContext(string strategyId)
    {
        var market = CreateMarket();
        return new StrategyContext
        {
            StrategyId = strategyId,
            ExecutionService = new NullExecutionService(),
            OrderBookReader = new FakeOrderBookReader(),
            MarketCatalog = new FakeMarketCatalogReader(new[] { market }),
            RiskManager = new NullRiskManager(),
            DecisionLogger = new NullDecisionLogger()
        };
    }

    private static MarketSnapshot CreateSnapshot(
        decimal yesBid,
        decimal yesAsk,
        decimal yesBidSize,
        decimal yesAskSize,
        decimal noBid,
        decimal noAsk,
        decimal noBidSize,
        decimal noAskSize)
    {
        var now = DateTimeOffset.UtcNow;
        var market = CreateMarket();

        var yesTop = new TopOfBookDto(
            "yes-token",
            new Price(yesBid),
            new Quantity(yesBidSize),
            new Price(yesAsk),
            new Quantity(yesAskSize),
            yesAsk - yesBid,
            now);

        var noTop = new TopOfBookDto(
            "no-token",
            new Price(noBid),
            new Quantity(noBidSize),
            new Price(noAsk),
            new Quantity(noAskSize),
            noAsk - noBid,
            now);

        return new MarketSnapshot(market, yesTop, noTop, now);
    }

    private static MarketInfoDto CreateMarket()
        => new()
        {
            MarketId = "mkt-1",
            ConditionId = "cond-1",
            Name = "Test Market",
            Status = "Active",
            Liquidity = 10000m,
            Volume24h = 5000m,
            TokenIds = new[] { "yes-token", "no-token" },
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };

    private static StrategyOrderUpdate CreateEntryFillUpdate(
        string strategyId,
        OutcomeSide outcome,
        decimal price,
        decimal quantity)
    {
        return new StrategyOrderUpdate(
            strategyId,
            Guid.NewGuid().ToString("N"),
            "mkt-1",
            outcome == OutcomeSide.Yes ? "yes-token" : "no-token",
            outcome,
            OrderLeg.Single,
            StrategySignalType.Entry,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Fok,
            price,
            ExecutionStatus.Filled,
            quantity,
            quantity,
            DateTimeOffset.UtcNow);
    }

    private static void AssertTakeProfitExit(
        StrategySignal? signal,
        OutcomeSide outcome,
        decimal expectedPrice)
    {
        Assert.NotNull(signal);
        Assert.Equal(StrategySignalType.Exit, signal!.Type);
        Assert.Contains("take_profit", signal.Reason);
        var order = Assert.Single(signal.Orders);
        Assert.Equal(outcome, order.Outcome);
        Assert.Equal(OrderSide.Sell, order.Side);
        Assert.Equal(TimeInForce.Fok, order.TimeInForce);
        Assert.Equal(expectedPrice, order.Price);
        Assert.Equal(2m, order.Quantity);
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
            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeMarketCatalogReader : IMarketCatalogReader
    {
        private readonly IReadOnlyList<MarketInfoDto> _markets;

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
            => Task.FromResult<IReadOnlyList<ExecutionResult>>(
                requests.Select(request => ExecutionResult.Fail(request.ClientOrderId, "NOT_IMPLEMENTED", "Not implemented")).ToArray());

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

        public RiskStateSnapshot GetStateSnapshot()
            => new(
                TotalOpenNotional: 0m,
                TotalOpenOrders: 0,
                TotalCapital: 0m,
                AvailableCapital: 0m,
                CapitalUtilizationPct: 0m,
                NotionalByStrategy: new Dictionary<string, decimal>(),
                NotionalByMarket: new Dictionary<string, decimal>(),
                OpenOrdersByStrategy: new Dictionary<string, int>(),
                UnhedgedExposures: Array.Empty<UnhedgedExposureSnapshot>());
    }
}
