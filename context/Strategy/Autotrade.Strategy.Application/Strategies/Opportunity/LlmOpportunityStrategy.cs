using System.Collections.Concurrent;
using System.Text.Json;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Strategies.Common;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Application.Strategies.Opportunity;

public sealed class LlmOpportunityStrategy : TradingStrategyBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IExecutableOpportunityPolicyFeed _policyFeed;
    private readonly IOptionsMonitor<LlmOpportunityOptions> _optionsMonitor;
    private readonly ConcurrentDictionary<string, ExecutableOpportunityPolicyDto> _policies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SingleLegMarketState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cycleBudgetLock = new();
    private readonly HashSet<string> _cycleEntryMarkets = new(StringComparer.OrdinalIgnoreCase);
    private decimal _cycleAllocatedNotional;

    public LlmOpportunityStrategy(
        StrategyContext context,
        IExecutableOpportunityPolicyFeed policyFeed,
        IOptionsMonitor<LlmOpportunityOptions> optionsMonitor)
        : base(context, "LlmOpportunity")
    {
        _policyFeed = policyFeed ?? throw new ArgumentNullException(nameof(policyFeed));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    public override async Task<IEnumerable<string>> SelectMarketsAsync(CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();
        ResetCycleBudget();
        if (!options.Enabled || IsRiskBlocked())
        {
            _policies.Clear();
            return Array.Empty<string>();
        }

        var now = DateTimeOffset.UtcNow;
        var policies = await _policyFeed.GetExecutableAsync(options.MaxMarkets, cancellationToken).ConfigureAwait(false);
        var selected = policies
            .Where(IsExecutablePolicy)
            .OrderByDescending(item => item.Edge)
            .Take(options.MaxMarkets)
            .ToList();

        _policies.Clear();
        foreach (var item in selected)
        {
            _policies[item.MarketId] = item;
        }

        return selected.Select(item => item.MarketId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        bool IsExecutablePolicy(ExecutableOpportunityPolicyDto policy)
            => policy.Status == ExecutableOpportunityPolicyStatus.Active &&
               policy.ValidFromUtc <= now &&
               policy.ValidUntilUtc > now;
    }

    public override Task<StrategySignal?> EvaluateEntryAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();
        if (!options.Enabled || State != StrategyState.Running || IsRiskBlocked())
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var now = DateTimeOffset.UtcNow;
        if (!_policies.TryGetValue(snapshot.Market.MarketId, out var policy) ||
            policy.Status != ExecutableOpportunityPolicyStatus.Active ||
            policy.ValidFromUtc > now ||
            policy.ValidUntilUtc <= now)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var state = GetState(snapshot.Market.MarketId);
        if (state.HasPosition || state.HasOpenEntryOrder || IsInCooldown(state.LastEntryAttemptUtc, now, options.EntryCooldownSeconds))
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        if (!StrategyTopBook.TryGetQuote(snapshot, policy.Outcome, out var quote) ||
            !StrategyTopBook.IsFresh(snapshot, quote, options.MaxOrderBookAgeSeconds) ||
            quote.Spread > policy.MaxSpread ||
            quote.AskPrice > policy.EntryMaxPrice)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        var limitPrice = StrategyTopBook.ClampPrice(quote.AskPrice * (1m + options.MaxSlippage));
        var maxNotional = Math.Min(policy.MaxNotional, policy.AllocationMaxNotional);
        var maxContracts = Math.Min(policy.Quantity, policy.AllocationMaxContracts);
        var quantity = StrategyTopBook.CalculateQuantity(
            maxContracts,
            minQuantity: 0.000001m,
            maxNotional,
            maxNotional - state.OpenNotional,
            limitPrice,
            quote.AskSize);
        if (quantity <= 0m)
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        if (!TryReserveEntryBudget(snapshot.Market.MarketId, quantity * limitPrice, options))
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
            $"LLM opportunity entry edge={policy.Edge:F4} fair={policy.FairProbability:F4}",
            new[] { order },
            BuildContextJson(policy, "entry", quote.AskPrice, quantity)));
    }

    public override Task<StrategySignal?> EvaluateExitAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        options.Validate();
        if (!options.Enabled || State != StrategyState.Running || IsRiskBlocked())
        {
            return Task.FromResult<StrategySignal?>(null);
        }

        if (!_policies.TryGetValue(snapshot.Market.MarketId, out var policy))
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

        var takeProfit = quote.BidPrice >= policy.TakeProfitPrice;
        var stopLoss = quote.BidPrice <= policy.StopLossPrice;
        var expired = policy.ValidUntilUtc <= now;
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
            BuildContextJson(policy, reason, quote.BidPrice, quantity)));
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

    private bool IsRiskBlocked()
        => Context.RiskManager.IsKillSwitchActive || Context.RiskManager.IsStrategyBlocked(Id);

    private void ResetCycleBudget()
    {
        lock (_cycleBudgetLock)
        {
            _cycleAllocatedNotional = 0m;
            _cycleEntryMarkets.Clear();
        }
    }

    private bool TryReserveEntryBudget(string marketId, decimal notional, LlmOpportunityOptions options)
    {
        lock (_cycleBudgetLock)
        {
            var activeMarketCount = _states.Values.Count(state => state.HasPosition || state.HasOpenEntryOrder);
            var alreadyReserved = _cycleEntryMarkets.Contains(marketId);
            var alreadyActive = _states.TryGetValue(marketId, out var currentState) &&
                (currentState.HasPosition || currentState.HasOpenEntryOrder);
            if (!alreadyReserved &&
                !alreadyActive &&
                activeMarketCount + _cycleEntryMarkets.Count >= options.MaxActiveOpportunities)
            {
                return false;
            }

            if (_cycleAllocatedNotional + notional > options.MaxPerCycleNotional)
            {
                return false;
            }

            _cycleAllocatedNotional += notional;
            _cycleEntryMarkets.Add(marketId);
            return true;
        }
    }

    private static string BuildContextJson(
        ExecutableOpportunityPolicyDto policy,
        string action,
        decimal observedPrice,
        decimal quantity)
    {
        return JsonSerializer.Serialize(new
        {
            opportunityId = policy.HypothesisId,
            policyId = policy.PolicyId,
            scoreId = policy.ScoreId,
            gateRunId = policy.GateRunId,
            allocationId = policy.AllocationId,
            scoreVersion = policy.ScoreVersion,
            policyVersion = policy.PolicyVersion,
            evidenceIds = policy.EvidenceIds,
            policy.Edge,
            policy.FairProbability,
            action,
            observedPrice,
            quantity
        }, JsonOptions);
    }
}
