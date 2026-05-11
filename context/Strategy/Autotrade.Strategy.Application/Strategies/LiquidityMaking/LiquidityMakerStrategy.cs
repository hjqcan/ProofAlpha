using System.Collections.Concurrent;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Strategies.Common;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Strategies.LiquidityMaking;

public sealed class LiquidityMakerStrategy : TradingStrategyBase
{
    private readonly IOptionsMonitor<LiquidityMakerOptions> _optionsMonitor;
    private readonly ILogger<LiquidityMakerStrategy> _logger;
    private readonly ConcurrentDictionary<string, SingleLegMarketState> _states = new(StringComparer.OrdinalIgnoreCase);

    public LiquidityMakerStrategy(
        StrategyContext context,
        IOptionsMonitor<LiquidityMakerOptions> optionsMonitor,
        ILogger<LiquidityMakerStrategy> logger)
        : base(context, "LiquidityMaker")
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            .Where(item => item.Value.HasPosition || item.Value.HasOpenEntryOrder || item.Value.HasOpenExitOrder)
            .Select(item => item.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var forcedSet = forced.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = new List<string>(Math.Max(options.MaxMarkets, forced.Count));
        selected.AddRange(forced);

        foreach (var market in Context.MarketCatalog.GetActiveMarkets()
                     .Where(market => IsEligibleMarket(market, options))
                     .Where(market => !forcedSet.Contains(market.MarketId))
                     .OrderByDescending(market => market.Liquidity)
                     .ThenByDescending(market => market.Volume24h))
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

        var now = DateTimeOffset.UtcNow;
        var state = GetState(snapshot.Market.MarketId);
        await CancelStaleOrdersAsync(state, StrategySignalType.Entry, options.MaxPassiveOrderAgeSeconds, now, cancellationToken)
            .ConfigureAwait(false);

        if (state.HasPosition || state.HasOpenEntryOrder || IsInCooldown(state.LastEntryAttemptUtc, now, options.EntryCooldownSeconds))
        {
            return null;
        }

        var candidate = StrategyTopBook.GetQuotes(snapshot)
            .Where(quote => StrategyTopBook.IsFresh(snapshot, quote, options.MaxOrderBookAgeSeconds))
            .Select(quote => CreateEntryCandidate(quote, options))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value)
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();

        if (candidate.Quote.TokenId is null)
        {
            return null;
        }

        var remainingNotional = options.MaxNotionalPerMarket - state.OpenNotional;
        var quantity = StrategyTopBook.CalculateQuantity(
            options.DefaultOrderQuantity,
            options.MinOrderQuantity,
            options.MaxNotionalPerOrder,
            remainingNotional,
            candidate.PassivePrice,
            candidate.Quote.AskSize);

        if (quantity <= 0m)
        {
            return null;
        }

        state.LastEntryAttemptUtc = now;

        var order = new StrategyOrderIntent(
            snapshot.Market.MarketId,
            candidate.Quote.TokenId,
            candidate.Quote.Outcome,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Gtc,
            candidate.PassivePrice,
            quantity,
            StrategyTopBook.IsNegRisk(snapshot),
            OrderLeg.Single);

        return new StrategySignal(
            StrategySignalType.Entry,
            snapshot.Market.MarketId,
            $"Liquidity maker quote {candidate.Quote.Outcome}: bid={candidate.Quote.BidPrice:F4}, ask={candidate.Quote.AskPrice:F4}, quote={candidate.PassivePrice:F4}",
            new[] { order },
            $"{{\"outcome\":\"{candidate.Quote.Outcome}\",\"bid\":{candidate.Quote.BidPrice:F4},\"ask\":{candidate.Quote.AskPrice:F4},\"quote\":{candidate.PassivePrice:F4},\"spread\":{candidate.Quote.Spread:F4}}}");
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
        await CancelStaleOrdersAsync(state, StrategySignalType.Exit, options.MaxPassiveOrderAgeSeconds, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (!state.HasPosition || state.Outcome is null || state.TokenId is null || state.HasOpenExitOrder)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (IsInCooldown(state.LastExitAttemptUtc, now, options.ExitCooldownSeconds))
        {
            return null;
        }

        if (!StrategyTopBook.TryGetQuote(snapshot, state.Outcome.Value, out var quote) ||
            !StrategyTopBook.IsFresh(snapshot, quote, options.MaxOrderBookAgeSeconds))
        {
            return null;
        }

        var holdAge = state.EntryFilledUtc is null ? TimeSpan.Zero : now - state.EntryFilledUtc.Value;
        var takeProfit = quote.BidPrice >= state.AverageEntryPrice + options.TakeProfitPriceDelta;
        var stopLoss = quote.BidPrice <= state.AverageEntryPrice - options.StopLossPriceDelta;
        var timeout = options.MaxHoldSeconds > 0 && holdAge >= TimeSpan.FromSeconds(options.MaxHoldSeconds);

        if (!takeProfit && !stopLoss && !timeout)
        {
            return null;
        }

        var quantity = Math.Min(state.Quantity, quote.BidSize);
        if (quantity < options.MinOrderQuantity)
        {
            return null;
        }

        var exitPrice = StrategyTopBook.ClampPrice(quote.BidPrice * (1m - options.MaxSlippage));
        state.LastExitAttemptUtc = now;

        var reason = takeProfit
            ? "take_profit"
            : stopLoss
                ? "stop_loss"
                : "max_hold";

        var order = new StrategyOrderIntent(
            snapshot.Market.MarketId,
            state.TokenId,
            state.Outcome.Value,
            OrderSide.Sell,
            OrderType.Limit,
            TimeInForce.Fok,
            exitPrice,
            quantity,
            StrategyTopBook.IsNegRisk(snapshot),
            OrderLeg.Single);

        return new StrategySignal(
            StrategySignalType.Exit,
            snapshot.Market.MarketId,
            $"Liquidity maker exit {reason}: bid={quote.BidPrice:F4}, entry={state.AverageEntryPrice:F4}",
            new[] { order },
            $"{{\"reason\":\"{reason}\",\"bid\":{quote.BidPrice:F4},\"entry\":{state.AverageEntryPrice:F4},\"qty\":{quantity:F4}}}");
    }

    public override Task OnOrderUpdateAsync(
        StrategyOrderUpdate update,
        CancellationToken cancellationToken = default)
    {
        var state = GetState(update.MarketId);
        state.ApplyOrderUpdate(update);

        _logger.LogDebug(
            "LiquidityMaker order update: Market={MarketId}, Outcome={Outcome}, Side={Side}, Status={Status}, Filled={Filled}",
            update.MarketId,
            update.Outcome,
            update.Side,
            update.Status,
            update.FilledQuantity);

        return Task.CompletedTask;
    }

    private async Task CancelStaleOrdersAsync(
        SingleLegMarketState state,
        StrategySignalType signalType,
        int maxOrderAgeSeconds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (maxOrderAgeSeconds <= 0)
        {
            return;
        }

        var anchor = signalType == StrategySignalType.Entry
            ? state.LastEntryAttemptUtc
            : state.LastExitAttemptUtc;

        if (!anchor.HasValue || now - anchor.Value < TimeSpan.FromSeconds(maxOrderAgeSeconds))
        {
            return;
        }

        foreach (var clientOrderId in state.GetOpenOrderIds(signalType))
        {
            var result = await Context.ExecutionService.CancelOrderAsync(clientOrderId, cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                state.MarkOrderCancelled(clientOrderId);
                continue;
            }

            _logger.LogWarning(
                "Failed to cancel stale maker order {ClientOrderId} for market {MarketId}: {Error}",
                clientOrderId,
                state.MarketId,
                result.ErrorMessage ?? result.ErrorCode ?? "UNKNOWN");
        }
    }

    private static EntryCandidate? CreateEntryCandidate(TopBookQuote quote, LiquidityMakerOptions options)
    {
        if (quote.AskPrice < options.MinEntryPrice ||
            quote.AskPrice > options.MaxEntryPrice ||
            quote.Spread < options.MinSpread ||
            quote.Spread > options.MaxSpread ||
            quote.BidSize < options.MinTopSize ||
            quote.AskSize < options.MinTopSize)
        {
            return null;
        }

        var passivePrice = StrategyTopBook.ClampPrice(quote.BidPrice + options.QuoteImproveTicks);
        var maxMakerPrice = quote.AskPrice - options.QuoteImproveTicks;
        if (passivePrice >= quote.AskPrice)
        {
            passivePrice = StrategyTopBook.ClampPrice(maxMakerPrice);
        }

        if (passivePrice < StrategyTopBook.MinLimitPrice || passivePrice >= quote.AskPrice)
        {
            return null;
        }

        var score = quote.Spread * 100m + quote.BidSize / Math.Max(quote.AskSize, 0.000001m);
        return new EntryCandidate(quote, passivePrice, score);
    }

    private SingleLegMarketState GetState(string marketId)
        => _states.GetOrAdd(marketId, id => new SingleLegMarketState(id));

    private static bool IsEligibleMarket(MarketInfoDto market, LiquidityMakerOptions options)
        => StrategyTopBook.IsTradableBinaryMarket(market)
           && market.Liquidity >= options.MinLiquidity
           && market.Volume24h >= options.MinVolume24h;

    private static bool IsInCooldown(DateTimeOffset? lastAttemptUtc, DateTimeOffset now, int cooldownSeconds)
        => lastAttemptUtc.HasValue && now - lastAttemptUtc.Value < TimeSpan.FromSeconds(cooldownSeconds);

    private readonly record struct EntryCandidate(TopBookQuote Quote, decimal PassivePrice, decimal Score);
}
