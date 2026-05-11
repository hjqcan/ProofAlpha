using System.Collections.Concurrent;
using System.Text.Json;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.Snapshots;
using Autotrade.MarketData.Application.Contract.Spot;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Strategies.Common;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Strategies.RepricingLag;

public sealed class RepricingLagArbitrageStrategy : TradingStrategyBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOptionsMonitor<RepricingLagArbitrageOptions> _optionsMonitor;
    private readonly ILogger<RepricingLagArbitrageStrategy> _logger;
    private readonly IMarketDataSnapshotReader _marketDataSnapshotReader;
    private readonly ConcurrentDictionary<string, RepricingLagMarketState> _states = new(StringComparer.OrdinalIgnoreCase);

    public RepricingLagArbitrageStrategy(
        StrategyContext context,
        IOptionsMonitor<RepricingLagArbitrageOptions> optionsMonitor,
        ILogger<RepricingLagArbitrageStrategy> logger)
        : base(context, "RepricingLagArbitrage")
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _marketDataSnapshotReader = context.MarketDataSnapshotReader
            ?? throw new ArgumentException("StrategyContext.MarketDataSnapshotReader is required.", nameof(context));
    }

    public override Task<IEnumerable<string>> SelectMarketsAsync(CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();

        if (!options.Enabled)
        {
            return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        }

        var forced = _states
            .Where(item => item.Value.Position.HasPosition || item.Value.Position.HasOpenEntryOrder)
            .Select(item => item.Key)
            .ToArray();
        var forcedSet = forced.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selected = new List<string>(Math.Max(forced.Length, options.MaxMarkets));
        selected.AddRange(forced);

        foreach (var market in Context.MarketCatalog.GetActiveMarkets()
                     .Where(market => IsEligibleMarket(market, options))
                     .Where(market => !forcedSet.Contains(market.MarketId))
                     .OrderByDescending(market => market.Volume24h)
                     .ThenByDescending(market => market.Liquidity))
        {
            if (selected.Count >= options.MaxMarkets)
            {
                break;
            }

            selected.Add(market.MarketId);
        }

        return Task.FromResult<IEnumerable<string>>(selected);
    }

    public override async Task<StrategySignal?> EvaluateEntryAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();

        if (!options.Enabled || State != StrategyState.Running || !IsEligibleMarket(snapshot.Market, options))
        {
            return null;
        }

        var data = ReadUnifiedSnapshot(snapshot, options);
        if (data?.WindowSpec is null)
        {
            return null;
        }

        var state = GetState(snapshot.Market.MarketId);
        var now = data.TimestampUtc;

        if (!await ValidateOracleSafetyAsync(data, options, cancellationToken).ConfigureAwait(false))
        {
            state.Phase = RepricingLagState.Faulted;
            return null;
        }

        if (now < data.WindowSpec.WindowStartUtc)
        {
            state.Phase = RepricingLagState.Wait;
            return null;
        }

        if (now >= data.WindowSpec.WindowEndUtc)
        {
            state.Phase = RepricingLagState.Exit;
            return null;
        }

        if (now - data.WindowSpec.WindowStartUtc < TimeSpan.FromSeconds(options.ConfirmWaitDurationSeconds))
        {
            state.Phase = RepricingLagState.Confirm;
            return null;
        }

        if (!data.SpotStaleness.IsFresh || !data.BaselineSpotStaleness.IsFresh ||
            !data.OrderBookStaleness.IsFresh ||
            data.LatestSpot is null || data.BaselineSpot is null)
        {
            state.Phase = RepricingLagState.Confirm;
            return null;
        }

        if (state.Position.HasPosition ||
            state.Position.HasOpenEntryOrder ||
            IsInCooldown(state.LastEntryAttemptUtc, now, options.EntryCooldownSeconds))
        {
            return null;
        }

        var moveBps = RepricingLagSignalMath.CalculateMoveBps(data.BaselineSpot.Price, data.LatestSpot.Price);
        if (!RepricingLagSignalMath.TryGetConfirmedOutcome(
                moveBps,
                options.MinMoveBps,
                out var outcome,
                out var absoluteMoveBps))
        {
            state.Phase = RepricingLagState.Confirm;
            return null;
        }

        var enrichedSnapshot = ToMarketSnapshot(data);
        if (!StrategyTopBook.TryGetQuote(enrichedSnapshot, outcome, out var quote) ||
            !StrategyTopBook.IsFresh(enrichedSnapshot, quote, options.MaxOrderBookAgeSeconds))
        {
            state.Phase = RepricingLagState.Confirm;
            return null;
        }

        var fairProbability = RepricingLagSignalMath.CalculateFairProbability(absoluteMoveBps, options);
        var edge = fairProbability - quote.AskPrice;
        if (edge < options.MinEdge)
        {
            state.Phase = RepricingLagState.Confirm;
            return null;
        }

        var limitPrice = StrategyTopBook.ClampPrice(quote.AskPrice * (1m + options.MaxSlippagePct));
        var remainingNotional = options.MaxNotionalPerMarket - state.Position.OpenNotional;
        var quantity = StrategyTopBook.CalculateQuantity(
            options.DefaultOrderQuantity,
            options.MinOrderQuantity,
            options.MaxNotionalPerOrder,
            remainingNotional,
            limitPrice,
            quote.AskSize);

        if (quantity <= 0m)
        {
            return null;
        }

        state.LastEntryAttemptUtc = now;
        state.Phase = RepricingLagState.Signal;

        var order = new StrategyOrderIntent(
            snapshot.Market.MarketId,
            quote.TokenId,
            quote.Outcome,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Fok,
            limitPrice,
            quantity,
            StrategyTopBook.IsNegRisk(snapshot),
            OrderLeg.Single);

        var contextJson = JsonSerializer.Serialize(new
        {
            state = state.Phase.ToString(),
            data.WindowSpec,
            spot = new
            {
                symbol = data.LatestSpot.Symbol,
                baseline = data.BaselineSpot.Price,
                latest = data.LatestSpot.Price,
                data.LatestSpot.Source,
                data.LatestSpot.TimestampUtc,
                moveBps
            },
            outcome = quote.Outcome.ToString(),
            market = new
            {
                ask = quote.AskPrice,
                quote.AskSize,
                fairProbability,
                edge,
                limitPrice,
                quantity
            }
        }, JsonOptions);

        return new StrategySignal(
            StrategySignalType.Entry,
            snapshot.Market.MarketId,
            $"Repricing lag entry {quote.Outcome}: moveBps={moveBps:F2}, fair={fairProbability:F4}, ask={quote.AskPrice:F4}, edge={edge:F4}",
            new[] { order },
            contextJson);
    }

    public override async Task<StrategySignal?> EvaluateExitAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();

        if (!options.Enabled || State != StrategyState.Running)
        {
            return null;
        }

        var state = GetState(snapshot.Market.MarketId);
        var data = ReadUnifiedSnapshot(snapshot, options);
        var now = data?.TimestampUtc ?? DateTimeOffset.UtcNow;

        await CancelExpiredEntryOrdersAsync(state, options, now, cancellationToken).ConfigureAwait(false);

        if (!state.Position.HasPosition ||
            state.Position.Outcome is null ||
            state.Position.TokenId is null ||
            state.Position.HasOpenExitOrder)
        {
            return null;
        }

        var enrichedSnapshot = data is null ? snapshot : ToMarketSnapshot(data);
        if (!StrategyTopBook.TryGetQuote(enrichedSnapshot, state.Position.Outcome.Value, out var quote) ||
            !StrategyTopBook.IsFresh(enrichedSnapshot, quote, options.MaxOrderBookAgeSeconds))
        {
            return null;
        }

        var holdAge = state.Position.EntryFilledUtc is null
            ? TimeSpan.Zero
            : now - state.Position.EntryFilledUtc.Value;
        var windowEnded = data?.WindowSpec is not null && now >= data.WindowSpec.WindowEndUtc;
        var maxHoldReached = options.MaxHoldSeconds > 0 && holdAge >= TimeSpan.FromSeconds(options.MaxHoldSeconds);

        if (!windowEnded && !maxHoldReached)
        {
            return null;
        }

        var quantity = Math.Min(state.Position.Quantity, quote.BidSize);
        if (quantity < options.MinOrderQuantity)
        {
            return null;
        }

        state.Phase = RepricingLagState.Exit;
        state.LastExitAttemptUtc = now;

        var exitPrice = StrategyTopBook.ClampPrice(quote.BidPrice * (1m - options.MaxSlippagePct));
        var reason = windowEnded ? "window_ended" : "max_hold";
        var order = new StrategyOrderIntent(
            snapshot.Market.MarketId,
            state.Position.TokenId,
            state.Position.Outcome.Value,
            OrderSide.Sell,
            OrderType.Limit,
            TimeInForce.Fok,
            exitPrice,
            quantity,
            StrategyTopBook.IsNegRisk(snapshot),
            OrderLeg.Single);

        var contextJson = JsonSerializer.Serialize(new
        {
            state = state.Phase.ToString(),
            reason,
            entry = new
            {
                state.Position.AverageEntryPrice,
                state.Position.Quantity,
                state.Position.EntryFilledUtc
            },
            exit = new
            {
                quote.BidPrice,
                quote.BidSize,
                exitPrice,
                quantity
            }
        }, JsonOptions);

        return new StrategySignal(
            StrategySignalType.Exit,
            snapshot.Market.MarketId,
            $"Repricing lag exit {reason}: bid={quote.BidPrice:F4}, entry={state.Position.AverageEntryPrice:F4}",
            new[] { order },
            contextJson);
    }

    public override Task OnOrderUpdateAsync(
        StrategyOrderUpdate update,
        CancellationToken cancellationToken = default)
    {
        var state = GetState(update.MarketId);
        state.Position.ApplyOrderUpdate(update);
        state.MarkOrderSeen(update.ClientOrderId, update.TimestampUtc);

        if (state.Position.HasPosition)
        {
            state.Phase = RepricingLagState.Monitor;
        }
        else if (update.Status is ExecutionStatus.Rejected or ExecutionStatus.Expired or ExecutionStatus.Cancelled)
        {
            state.Phase = RepricingLagState.Wait;
        }
        else if (update.SignalType == StrategySignalType.Entry)
        {
            state.Phase = RepricingLagState.Submit;
        }

        _logger.LogDebug(
            "RepricingLag order update: Market={MarketId}, Outcome={Outcome}, Side={Side}, Status={Status}, Filled={Filled}",
            update.MarketId,
            update.Outcome,
            update.Side,
            update.Status,
            update.FilledQuantity);

        return Task.CompletedTask;
    }

    private UnifiedMarketDataSnapshot? ReadUnifiedSnapshot(
        MarketSnapshot snapshot,
        RepricingLagArbitrageOptions options)
        => _marketDataSnapshotReader.GetSnapshot(
            snapshot.Market.MarketId,
            TimeSpan.FromSeconds(options.MaxDataStalenessSeconds),
            TimeSpan.FromSeconds(options.MaxOrderBookAgeSeconds),
            depthLevels: 5,
            maxBaselineSpotAge: TimeSpan.FromSeconds(options.MaxBaselineSpotAgeSeconds));

    private async Task<bool> ValidateOracleSafetyAsync(
        UnifiedMarketDataSnapshot data,
        RepricingLagArbitrageOptions options,
        CancellationToken cancellationToken)
    {
        if (data.WindowSpec is null)
        {
            return false;
        }

        var oracleAccepted = options.IsOracleAccepted(data.WindowSpec);
        var rejectedSpotSourceReason = GetRejectedSpotSourceReason(data, options);
        var sourceAccepted = rejectedSpotSourceReason is null;

        if (oracleAccepted && sourceAccepted)
        {
            return true;
        }

        var reasonCode = !oracleAccepted ? "ORACLE_NOT_CONFIRMED" : "SPOT_SOURCE_MISMATCH";
        var reason = !oracleAccepted
            ? $"Market window oracle is {data.WindowSpec.OracleStatus}; strategy requires confirmed oracle alignment."
            : rejectedSpotSourceReason!;
        var contextJson = JsonSerializer.Serialize(new
        {
            data.Market.MarketId,
            data.WindowSpec,
            latestSpot = data.LatestSpot,
            baselineSpot = data.BaselineSpot,
            options.RequireConfirmedOracle,
            options.AllowedSpotSources
        }, JsonOptions);

        await Context.DecisionLogger.LogAsync(new StrategyDecision(
            Id,
            "SafetyRejected",
            reason,
            data.Market.MarketId,
            contextJson,
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);

        if (options.TriggerKillSwitchOnOracleMismatch)
        {
            await Context.RiskManager.ActivateStrategyKillSwitchAsync(
                Id,
                KillSwitchLevel.SoftStop,
                reasonCode,
                reason,
                data.Market.MarketId,
                contextJson,
                cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task CancelExpiredEntryOrdersAsync(
        RepricingLagMarketState state,
        RepricingLagArbitrageOptions options,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var clientOrderId in state.Position.GetOpenOrderIds(StrategySignalType.Entry))
        {
            if (!state.TryGetOrderSeenUtc(clientOrderId, out var seenUtc))
            {
                continue;
            }

            if (nowUtc - seenUtc < TimeSpan.FromSeconds(options.MaxOrderAgeSeconds))
            {
                continue;
            }

            var result = await Context.ExecutionService
                .CancelOrderAsync(clientOrderId, cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                state.Position.MarkOrderCancelled(clientOrderId);
            }

            await Context.DecisionLogger.LogAsync(new StrategyDecision(
                Id,
                result.Success ? "CancelExpiredOrder" : "CancelExpiredOrderFailed",
                result.Success
                    ? "Entry order expired before repricing lag could be filled."
                    : result.ErrorMessage ?? "Cancel failed.",
                state.Position.MarketId,
                JsonSerializer.Serialize(new
                {
                    clientOrderId,
                    result.Status,
                    result.ErrorCode,
                    result.ErrorMessage,
                    maxOrderAgeSeconds = options.MaxOrderAgeSeconds
                }, JsonOptions),
                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
        }
    }

    private RepricingLagMarketState GetState(string marketId)
        => _states.GetOrAdd(marketId, id => new RepricingLagMarketState(id));

    private bool IsEligibleMarket(MarketInfoDto market, RepricingLagArbitrageOptions options)
        => StrategyTopBook.IsTradableBinaryMarket(market)
           && market.Liquidity >= options.MinLiquidity
           && market.Volume24h >= options.MinVolume24h
           && _marketDataSnapshotReader.GetWindowSpec(market.MarketId) is not null;

    private static MarketSnapshot ToMarketSnapshot(UnifiedMarketDataSnapshot data)
        => new(
            data.Market,
            data.YesTopOfBook,
            data.NoTopOfBook,
            data.TimestampUtc);

    private static bool IsInCooldown(DateTimeOffset? lastAttemptUtc, DateTimeOffset now, int cooldownSeconds)
        => lastAttemptUtc.HasValue && now - lastAttemptUtc.Value < TimeSpan.FromSeconds(cooldownSeconds);

    private static string? GetRejectedSpotSourceReason(
        UnifiedMarketDataSnapshot data,
        RepricingLagArbitrageOptions options)
    {
        if (IsRejectedSpotSource(data.LatestSpot, options))
        {
            return $"Latest spot source {data.LatestSpot!.Source} is not allowed for repricing lag strategy.";
        }

        if (IsRejectedSpotSource(data.BaselineSpot, options))
        {
            return $"Baseline spot source {data.BaselineSpot!.Source} is not allowed for repricing lag strategy.";
        }

        return null;
    }

    private static bool IsRejectedSpotSource(SpotPriceTick? tick, RepricingLagArbitrageOptions options)
        => tick is not null &&
           !options.AllowedSpotSources.Contains(tick.Source, StringComparer.OrdinalIgnoreCase);

    private sealed class RepricingLagMarketState
    {
        private readonly Dictionary<string, DateTimeOffset> _orderSeenUtc = new(StringComparer.OrdinalIgnoreCase);

        public RepricingLagMarketState(string marketId)
        {
            Position = new SingleLegMarketState(marketId);
        }

        public RepricingLagState Phase { get; set; } = RepricingLagState.Wait;

        public SingleLegMarketState Position { get; }

        public DateTimeOffset? LastEntryAttemptUtc { get; set; }

        public DateTimeOffset? LastExitAttemptUtc { get; set; }

        public void MarkOrderSeen(string clientOrderId, DateTimeOffset timestampUtc)
            => _orderSeenUtc.TryAdd(clientOrderId, timestampUtc.ToUniversalTime());

        public bool TryGetOrderSeenUtc(string clientOrderId, out DateTimeOffset timestampUtc)
            => _orderSeenUtc.TryGetValue(clientOrderId, out timestampUtc);
    }
}
