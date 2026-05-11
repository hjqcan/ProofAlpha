using System.Collections.Concurrent;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Strategies.Common;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Strategies.LiquidityPulse;

public sealed class LiquidityPulseStrategy : TradingStrategyBase
{
    private readonly IOptionsMonitor<LiquidityPulseOptions> _optionsMonitor;
    private readonly ILogger<LiquidityPulseStrategy> _logger;
    private readonly ConcurrentDictionary<string, SingleLegMarketState> _states = new(StringComparer.OrdinalIgnoreCase);

    public LiquidityPulseStrategy(
        StrategyContext context,
        IOptionsMonitor<LiquidityPulseOptions> optionsMonitor,
        ILogger<LiquidityPulseStrategy> logger)
        : base(context, "LiquidityPulse")
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

    public override Task<StrategySignal?> EvaluateEntryAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();

        if (!options.Enabled || State != StrategyState.Running || !IsEligibleMarket(snapshot.Market, options))
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var now = DateTimeOffset.UtcNow;
        var state = GetState(snapshot.Market.MarketId);
        if (state.HasPosition || state.HasOpenEntryOrder || IsInCooldown(state.LastEntryAttemptUtc, now, options.EntryCooldownSeconds))
        {
            return Task.FromResult<StrategySignal?>(null);
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
            return Task.FromResult<StrategySignal?>(null);
        }

        var remainingNotional = options.MaxNotionalPerMarket - state.OpenNotional;
        var limitPrice = StrategyTopBook.ClampPrice(candidate.Quote.AskPrice * (1m + options.MaxSlippage));
        var quantity = StrategyTopBook.CalculateQuantity(
            options.DefaultOrderQuantity,
            options.MinOrderQuantity,
            options.MaxNotionalPerOrder,
            remainingNotional,
            limitPrice,
            candidate.Quote.AskSize);

        if (quantity <= 0m)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        state.LastEntryAttemptUtc = now;

        var order = new StrategyOrderIntent(
            snapshot.Market.MarketId,
            candidate.Quote.TokenId,
            candidate.Quote.Outcome,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Fok,
            limitPrice,
            quantity,
            StrategyTopBook.IsNegRisk(snapshot),
            OrderLeg.Single);

        var signal = new StrategySignal(
            StrategySignalType.Entry,
            snapshot.Market.MarketId,
            $"Liquidity pulse {candidate.Quote.Outcome}: dominance={candidate.BidDominance:F3}, spread={candidate.Quote.Spread:F4}",
            new[] { order },
            $"{{\"outcome\":\"{candidate.Quote.Outcome}\",\"bidDominance\":{candidate.BidDominance:F4},\"spread\":{candidate.Quote.Spread:F4},\"ask\":{candidate.Quote.AskPrice:F4}}}");

        return Task.FromResult<StrategySignal?>(signal);
    }

    public override Task<StrategySignal?> EvaluateExitAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();

        if (!options.Enabled || State != StrategyState.Running)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var state = GetState(snapshot.Market.MarketId);
        if (!state.HasPosition || state.Outcome is null || state.TokenId is null || state.HasOpenExitOrder)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var now = DateTimeOffset.UtcNow;
        if (IsInCooldown(state.LastExitAttemptUtc, now, options.ExitCooldownSeconds))
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        if (!StrategyTopBook.TryGetQuote(snapshot, state.Outcome.Value, out var quote) ||
            !StrategyTopBook.IsFresh(snapshot, quote, options.MaxOrderBookAgeSeconds))
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var holdAge = state.EntryFilledUtc is null ? TimeSpan.Zero : now - state.EntryFilledUtc.Value;
        var takeProfit = quote.BidPrice >= state.AverageEntryPrice + options.TakeProfitPriceDelta;
        var stopLoss = quote.BidPrice <= state.AverageEntryPrice - options.StopLossPriceDelta;
        var timeout = options.MaxHoldSeconds > 0 && holdAge >= TimeSpan.FromSeconds(options.MaxHoldSeconds);

        if (!takeProfit && !stopLoss && !timeout)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var quantity = Math.Min(state.Quantity, quote.BidSize);
        if (quantity < options.MinOrderQuantity)
        {
            return Task.FromResult<StrategySignal?>(null);
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

        var signal = new StrategySignal(
            StrategySignalType.Exit,
            snapshot.Market.MarketId,
            $"Liquidity pulse exit {reason}: bid={quote.BidPrice:F4}, entry={state.AverageEntryPrice:F4}",
            new[] { order },
            $"{{\"reason\":\"{reason}\",\"bid\":{quote.BidPrice:F4},\"entry\":{state.AverageEntryPrice:F4},\"qty\":{quantity:F4}}}");

        return Task.FromResult<StrategySignal?>(signal);
    }

    public override Task OnOrderUpdateAsync(
        StrategyOrderUpdate update,
        CancellationToken cancellationToken = default)
    {
        var state = GetState(update.MarketId);
        state.ApplyOrderUpdate(update);

        _logger.LogDebug(
            "LiquidityPulse order update: Market={MarketId}, Outcome={Outcome}, Side={Side}, Status={Status}, Filled={Filled}",
            update.MarketId,
            update.Outcome,
            update.Side,
            update.Status,
            update.FilledQuantity);

        return Task.CompletedTask;
    }

    private static bool IsEligibleMarket(MarketInfoDto market, LiquidityPulseOptions options)
        => StrategyTopBook.IsTradableBinaryMarket(market)
           && market.Liquidity >= options.MinLiquidity
           && market.Volume24h >= options.MinVolume24h;

    private static EntryCandidate? CreateEntryCandidate(TopBookQuote quote, LiquidityPulseOptions options)
    {
        if (quote.AskPrice < options.MinEntryPrice ||
            quote.AskPrice > options.MaxEntryPrice ||
            quote.Spread > options.MaxSpread ||
            quote.BidSize < options.MinTopSize ||
            quote.AskSize < options.MinTopSize)
        {
            return null;
        }

        var bidDominance = quote.BidSize / Math.Max(quote.BidSize + quote.AskSize, 0.000001m);
        if (bidDominance < options.MinBidDominance)
        {
            return null;
        }

        var depthRatio = quote.BidSize / Math.Max(quote.AskSize, 0.000001m);
        var score = bidDominance * 100m + depthRatio - quote.Spread * 10m;
        return new EntryCandidate(quote, bidDominance, score);
    }

    private SingleLegMarketState GetState(string marketId)
        => _states.GetOrAdd(marketId, id => new SingleLegMarketState(id));

    private static bool IsInCooldown(DateTimeOffset? lastAttemptUtc, DateTimeOffset now, int cooldownSeconds)
        => lastAttemptUtc.HasValue && now - lastAttemptUtc.Value < TimeSpan.FromSeconds(cooldownSeconds);

    private readonly record struct EntryCandidate(TopBookQuote Quote, decimal BidDominance, decimal Score);
}
