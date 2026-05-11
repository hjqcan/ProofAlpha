using System.Text.Json;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Domain.Shared.ValueObjects;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Strategies.Opportunity;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Tests;

public sealed class LlmOpportunityStrategyTests
{
    [Fact]
    public async Task SelectMarketsAsync_OnlyReturnsPublishedUnexpiredOpportunities()
    {
        var strategy = CreateStrategy(
            [
                Opportunity("mkt-1", DateTimeOffset.UtcNow.AddHours(1)),
                Opportunity("mkt-expired", DateTimeOffset.UtcNow.AddMinutes(-1))
            ]);

        var markets = await strategy.SelectMarketsAsync();

        Assert.Equal(new[] { "mkt-1" }, markets);
    }

    [Fact]
    public async Task EvaluateEntryAsync_UsesCompiledPolicyWithoutCallingExternalSources()
    {
        var opportunity = Opportunity("mkt-1", DateTimeOffset.UtcNow.AddHours(1));
        var strategy = CreateStrategy([opportunity], entryCooldownSeconds: 0);
        await strategy.StartAsync();
        await strategy.SelectMarketsAsync();

        var signal = await strategy.EvaluateEntryAsync(CreateSnapshot(yesBid: 0.50m, yesAsk: 0.53m));

        Assert.NotNull(signal);
        Assert.Equal(StrategySignalType.Entry, signal!.Type);
        var order = Assert.Single(signal.Orders);
        Assert.Equal(OutcomeSide.Yes, order.Outcome);
        Assert.Equal(OrderSide.Buy, order.Side);
        Assert.Equal(TimeInForce.Fok, order.TimeInForce);
        Assert.Equal(0.53m, order.Price);

        using var context = JsonDocument.Parse(signal.ContextJson!);
        Assert.Equal(opportunity.OpportunityId, context.RootElement.GetProperty("opportunityId").GetGuid());
        Assert.Equal(opportunity.ResearchRunId, context.RootElement.GetProperty("researchRunId").GetGuid());
        Assert.Single(context.RootElement.GetProperty("evidenceIds").EnumerateArray());
    }

    [Fact]
    public async Task EvaluateEntryAsync_IgnoresStaleOrderBook()
    {
        var strategy = CreateStrategy([Opportunity("mkt-1", DateTimeOffset.UtcNow.AddHours(1))]);
        await strategy.StartAsync();
        await strategy.SelectMarketsAsync();

        var signal = await strategy.EvaluateEntryAsync(
            CreateSnapshot(yesBid: 0.50m, yesAsk: 0.53m, quoteAge: TimeSpan.FromMinutes(5)));

        Assert.Null(signal);
    }

    [Fact]
    public async Task EvaluateExitAsync_TriggersOnTakeProfitFromCompiledPolicy()
    {
        var strategy = CreateStrategy([Opportunity("mkt-1", DateTimeOffset.UtcNow.AddHours(1))], exitCooldownSeconds: 0);
        await strategy.StartAsync();
        await strategy.SelectMarketsAsync();
        await strategy.OnOrderUpdateAsync(new StrategyOrderUpdate(
            "llm_opportunity",
            Guid.NewGuid().ToString("N"),
            "mkt-1",
            "yes-token",
            OutcomeSide.Yes,
            OrderLeg.Single,
            StrategySignalType.Entry,
            OrderSide.Buy,
            OrderType.Limit,
            TimeInForce.Fok,
            0.53m,
            ExecutionStatus.Filled,
            2m,
            2m,
            DateTimeOffset.UtcNow));

        var signal = await strategy.EvaluateExitAsync(CreateSnapshot(yesBid: 0.66m, yesAsk: 0.68m));

        Assert.NotNull(signal);
        Assert.Equal(StrategySignalType.Exit, signal!.Type);
        Assert.Contains("take_profit", signal.ContextJson!);
        var order = Assert.Single(signal.Orders);
        Assert.Equal(OrderSide.Sell, order.Side);
        Assert.Equal(0.66m, order.Price);
        Assert.Equal(2m, order.Quantity);
    }

    private static LlmOpportunityStrategy CreateStrategy(
        IReadOnlyList<PublishedOpportunityDto> opportunities,
        int entryCooldownSeconds = 60,
        int exitCooldownSeconds = 15)
    {
        return new LlmOpportunityStrategy(
            new StrategyContext
            {
                StrategyId = "llm_opportunity",
                ExecutionService = null!,
                OrderBookReader = null!,
                MarketCatalog = null!,
                RiskManager = null!,
                DecisionLogger = null!
            },
            new StaticPublishedOpportunityFeed(opportunities),
            new StaticOptionsMonitor<LlmOpportunityOptions>(new LlmOpportunityOptions
            {
                Enabled = true,
                MaxMarkets = 20,
                EntryCooldownSeconds = entryCooldownSeconds,
                ExitCooldownSeconds = exitCooldownSeconds,
                MaxOrderBookAgeSeconds = 10,
                MaxSlippage = 0m
            }));
    }

    private static PublishedOpportunityDto Opportunity(string marketId, DateTimeOffset validUntilUtc)
    {
        var opportunityId = Guid.NewGuid();
        var researchRunId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();
        var policy = new CompiledOpportunityPolicy(
            opportunityId,
            researchRunId,
            marketId,
            OutcomeSide.Yes,
            0.62m,
            0.72m,
            0.05m,
            0.57m,
            0.65m,
            0.49m,
            0.04m,
            2m,
            10m,
            validUntilUtc,
            [evidenceId]);

        return new PublishedOpportunityDto(
            opportunityId,
            researchRunId,
            marketId,
            OutcomeSide.Yes,
            0.05m,
            validUntilUtc,
            [evidenceId],
            policy);
    }

    private static MarketSnapshot CreateSnapshot(
        decimal yesBid,
        decimal yesAsk,
        TimeSpan? quoteAge = null)
    {
        var now = DateTimeOffset.UtcNow;
        var quoteTime = now - (quoteAge ?? TimeSpan.Zero);
        var yesTop = new TopOfBookDto(
            "yes-token",
            new Price(yesBid),
            new Quantity(10m),
            new Price(yesAsk),
            new Quantity(10m),
            yesAsk - yesBid,
            quoteTime);
        var noTop = new TopOfBookDto(
            "no-token",
            new Price(1m - yesAsk),
            new Quantity(10m),
            new Price(1m - yesBid),
            new Quantity(10m),
            yesAsk - yesBid,
            quoteTime);

        return new MarketSnapshot(
            new MarketInfoDto
            {
                MarketId = "mkt-1",
                ConditionId = "condition-1",
                Name = "Test market",
                Status = "active",
                TokenIds = ["yes-token", "no-token"],
                ExpiresAtUtc = now.AddDays(1)
            },
            yesTop,
            noTop,
            now);
    }

    private sealed class StaticPublishedOpportunityFeed : IPublishedOpportunityFeed
    {
        private readonly IReadOnlyList<PublishedOpportunityDto> _opportunities;

        public StaticPublishedOpportunityFeed(IReadOnlyList<PublishedOpportunityDto> opportunities)
        {
            _opportunities = opportunities;
        }

        public Task<IReadOnlyList<PublishedOpportunityDto>> GetPublishedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_opportunities);
        }
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
