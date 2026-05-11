using System.Collections.Concurrent;
using System.Text.Json;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Strategies.Common;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Strategies.Opportunity;

public sealed class LlmOpportunityStrategy : TradingStrategyBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPublishedOpportunityFeed _opportunityFeed;
    private readonly IOptionsMonitor<LlmOpportunityOptions> _optionsMonitor;
    private readonly ConcurrentDictionary<string, PublishedOpportunityDto> _published = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SingleLegMarketState> _states = new(StringComparer.OrdinalIgnoreCase);

    public LlmOpportunityStrategy(
        StrategyContext context,
        IPublishedOpportunityFeed opportunityFeed,
        IOptionsMonitor<LlmOpportunityOptions> optionsMonitor)
        : base(context, "LlmOpportunity")
    {
        _opportunityFeed = opportunityFeed ?? throw new ArgumentNullException(nameof(opportunityFeed));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    public override async Task<IEnumerable<string>> SelectMarketsAsync(CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();
        if (!options.Enabled)
        {
            _published.Clear();
            return Array.Empty<string>();
        }

        var now = DateTimeOffset.UtcNow;
        var opportunities = await _opportunityFeed.GetPublishedAsync(cancellationToken).ConfigureAwait(false);
        var selected = opportunities
            .Where(item => item.ValidUntilUtc > now)
            .OrderByDescending(item => item.Edge)
            .Take(options.MaxMarkets)
            .ToList();

        _published.Clear();
        foreach (var item in selected)
        {
            _published[item.MarketId] = item;
        }

        return selected.Select(item => item.MarketId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public override Task<StrategySignal?> EvaluateEntryAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();
        if (!options.Enabled || State != StrategyState.Running)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        if (!_published.TryGetValue(snapshot.Market.MarketId, out var opportunity) ||
            opportunity.ValidUntilUtc <= DateTimeOffset.UtcNow)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var state = GetState(snapshot.Market.MarketId);
        var now = DateTimeOffset.UtcNow;
        if (state.HasPosition || state.HasOpenEntryOrder || IsInCooldown(state.LastEntryAttemptUtc, now, options.EntryCooldownSeconds))
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        if (!StrategyTopBook.TryGetQuote(snapshot, opportunity.Outcome, out var quote) ||
            !StrategyTopBook.IsFresh(snapshot, quote, options.MaxOrderBookAgeSeconds) ||
            quote.Spread > opportunity.Policy.MaxSpread ||
            quote.AskPrice > opportunity.Policy.EntryMaxPrice)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var limitPrice = StrategyTopBook.ClampPrice(quote.AskPrice * (1m + options.MaxSlippage));
        var quantity = StrategyTopBook.CalculateQuantity(
            opportunity.Policy.Quantity,
            minQuantity: 0.000001m,
            opportunity.Policy.MaxNotional,
            opportunity.Policy.MaxNotional - state.OpenNotional,
            limitPrice,
            quote.AskSize);
        if (quantity <= 0m)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        state.LastEntryAttemptUtc = now;
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

        return Task.FromResult<StrategySignal?>(new StrategySignal(
            StrategySignalType.Entry,
            snapshot.Market.MarketId,
            $"LLM opportunity entry edge={opportunity.Edge:F4} fair={opportunity.Policy.FairProbability:F4}",
            new[] { order },
            BuildContextJson(opportunity, "entry", quote.AskPrice, quantity)));
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

        if (!_published.TryGetValue(snapshot.Market.MarketId, out var opportunity))
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

        var takeProfit = quote.BidPrice >= opportunity.Policy.TakeProfitPrice;
        var stopLoss = quote.BidPrice <= opportunity.Policy.StopLossPrice;
        var expired = opportunity.ValidUntilUtc <= now;
        if (!takeProfit && !stopLoss && !expired)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var quantity = Math.Min(state.Quantity, quote.BidSize);
        if (quantity <= 0m)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        state.LastExitAttemptUtc = now;
        var exitPrice = StrategyTopBook.ClampPrice(quote.BidPrice * (1m - options.MaxSlippage));
        var reason = takeProfit ? "take_profit" : stopLoss ? "stop_loss" : "expired";
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

        return Task.FromResult<StrategySignal?>(new StrategySignal(
            StrategySignalType.Exit,
            snapshot.Market.MarketId,
            $"LLM opportunity exit {reason}: bid={quote.BidPrice:F4}",
            new[] { order },
            BuildContextJson(opportunity, reason, quote.BidPrice, quantity)));
    }

    public override Task OnOrderUpdateAsync(
        StrategyOrderUpdate update,
        CancellationToken cancellationToken = default)
    {
        GetState(update.MarketId).ApplyOrderUpdate(update);
        return Task.CompletedTask;
    }

    private SingleLegMarketState GetState(string marketId)
    {
        return _states.GetOrAdd(marketId, id => new SingleLegMarketState(id));
    }

    private static bool IsInCooldown(DateTimeOffset? lastAttemptUtc, DateTimeOffset now, int cooldownSeconds)
    {
        return lastAttemptUtc.HasValue && now - lastAttemptUtc.Value < TimeSpan.FromSeconds(cooldownSeconds);
    }

    private static string BuildContextJson(
        PublishedOpportunityDto opportunity,
        string action,
        decimal observedPrice,
        decimal quantity)
    {
        return JsonSerializer.Serialize(new
        {
            opportunityId = opportunity.OpportunityId,
            researchRunId = opportunity.ResearchRunId,
            evidenceIds = opportunity.EvidenceIds,
            opportunity.Edge,
            action,
            observedPrice,
            quantity
        }, JsonOptions);
    }
}
