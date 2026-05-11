using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.Strategies.Endgame;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Autotrade.Strategy.Tests;

public sealed class EndgameSweepStrategyTests
{
    [Fact]
    public async Task SelectMarkets_ForceIncludesPositionMarket_EvenWhenOutsideExpiryWindowAndMaxMarketsIsOne()
    {
        var farMarket = new MarketInfoDto
        {
            MarketId = "far-market",
            ConditionId = "cond-far",
            Name = "Far Market",
            Status = "Active",
            Liquidity = 5000m,
            Volume24h = 1000m,
            TokenIds = new[] { "far-yes", "far-no" },
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2)
        };

        var expiringMarket = new MarketInfoDto
        {
            MarketId = "expiring-market",
            ConditionId = "cond-exp",
            Name = "Expiring Market",
            Status = "Active",
            Liquidity = 5000m,
            Volume24h = 1000m,
            TokenIds = new[] { "exp-yes", "exp-no" },
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        var catalog = new FakeMarketCatalogReader(new[] { farMarket, expiringMarket });
        var context = new StrategyContext
        {
            StrategyId = "endgame_sweep",
            ExecutionService = new NullExecutionService(),
            OrderBookReader = new FakeOrderBookReader(),
            MarketCatalog = catalog,
            RiskManager = new NullRiskManager(),
            DecisionLogger = new NullDecisionLogger()
        };

        var options = new EndgameSweepOptions
        {
            Enabled = true,
            MaxSecondsToExpiry = 900, // 15min
            MinSecondsToExpiry = 60,
            MinLiquidity = 0m,
            MaxMarkets = 1
        };

        var strategy = new EndgameSweepStrategy(
            context,
            new StaticOptionsMonitor<EndgameSweepOptions>(options),
            NullLogger<EndgameSweepStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        // 模拟 far-market 已有持仓（入场订单已成交）
        await strategy.OnOrderUpdateAsync(
            CreateEntryFillUpdate("far-market", "far-yes", OutcomeSide.Yes, filled: 1m),
            CancellationToken.None);

        var markets = await strategy.SelectMarketsAsync(CancellationToken.None);
        var marketList = markets.ToList();

        // 必须强制纳入持仓市场（即使不在到期窗口内，且 MaxMarkets=1）
        Assert.Single(marketList);
        Assert.Equal("far-market", marketList[0]);
    }

    [Fact]
    public async Task SelectMarkets_ForceIncludesAllPositionMarkets_EvenWhenExceedingMaxMarkets()
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
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2)
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
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2)
        };

        var catalog = new FakeMarketCatalogReader(new[] { m1, m2 });
        var context = new StrategyContext
        {
            StrategyId = "endgame_sweep",
            ExecutionService = new NullExecutionService(),
            OrderBookReader = new FakeOrderBookReader(),
            MarketCatalog = catalog,
            RiskManager = new NullRiskManager(),
            DecisionLogger = new NullDecisionLogger()
        };

        var options = new EndgameSweepOptions
        {
            Enabled = true,
            MaxSecondsToExpiry = 900,
            MinSecondsToExpiry = 60,
            MinLiquidity = 0m,
            MaxMarkets = 1
        };

        var strategy = new EndgameSweepStrategy(
            context,
            new StaticOptionsMonitor<EndgameSweepOptions>(options),
            NullLogger<EndgameSweepStrategy>.Instance);

        await strategy.StartAsync(CancellationToken.None);

        // 两个市场都有持仓：强制集数量 > MaxMarkets
        await strategy.OnOrderUpdateAsync(
            CreateEntryFillUpdate("m1", "m1-yes", OutcomeSide.Yes, filled: 1m),
            CancellationToken.None);
        await strategy.OnOrderUpdateAsync(
            CreateEntryFillUpdate("m2", "m2-yes", OutcomeSide.Yes, filled: 1m),
            CancellationToken.None);

        var markets = await strategy.SelectMarketsAsync(CancellationToken.None);
        var marketList = markets.ToList();

        Assert.Equal(2, marketList.Count);
        Assert.Contains("m1", marketList);
        Assert.Contains("m2", marketList);
    }

    private static StrategyOrderUpdate CreateEntryFillUpdate(
        string marketId,
        string tokenId,
        OutcomeSide outcome,
        decimal filled)
    {
        return new StrategyOrderUpdate(
            "endgame_sweep",
            Guid.NewGuid().ToString("N"),
            marketId,
            tokenId,
            outcome,
            OrderLeg.First,
            StrategySignalType.Entry,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Gtc,
            0.95m,
            ExecutionStatus.Filled,
            filled,
            filled,
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

