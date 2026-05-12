using System.Text.Json;
using System.Text.Json.Serialization;
using Autotrade.ArcSettlement.Application.Contract.Configuration;
using Autotrade.ArcSettlement.Application.Contract.Proofs;
using Autotrade.ArcSettlement.Application.Contract.Signals;
using Autotrade.ArcSettlement.Application.Proofs;
using Autotrade.Hosting.ArcSettlement;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Options;

namespace Autotrade.Api.Tests;

public sealed class ArcSignalSourceProofResolverTests
{
    private static readonly Guid OpportunityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ResearchRunId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EvidenceId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset SourceTime = DateTimeOffset.Parse("2026-05-12T10:00:00Z");

    [Fact]
    public async Task OpportunityResolverBuildsProofFromApprovedOpportunityPolicyAndEvidence()
    {
        var service = new FakeOpportunityQueryService
        {
            Opportunity = CreateOpportunity(OpportunityStatus.Approved),
            Evidence = [CreateEvidence()]
        };
        var resolver = new ArcOpportunitySignalProofResolver(
            service,
            new ArcProofHashService(),
            new StaticOptionsMonitor<ArcSettlementOptions>(CreateOptions()));

        var resolution = await resolver.ResolveAsync(
            new PublishArcSignalSourceRequest(
                ArcProofSourceKind.Opportunity,
                OpportunityId.ToString("D"),
                "operator-1",
                "publish reviewed opportunity"));

        Assert.Equal(ArcSignalSourceReviewStatus.Approved, resolution.SourceReviewStatus);
        Assert.StartsWith("0x", resolution.SourcePolicyHash, StringComparison.Ordinal);
        Assert.Equal(ArcProofSourceKind.Opportunity, resolution.SignalProof.SourceKind);
        Assert.Equal(OpportunityId.ToString("D"), resolution.SignalProof.SourceId);
        Assert.Equal("llm_opportunity", resolution.SignalProof.StrategyId);
        Assert.Equal("market-1", resolution.SignalProof.MarketId);
        Assert.Equal(450m, resolution.SignalProof.ExpectedEdgeBps);
        Assert.Equal(75m, resolution.SignalProof.MaxNotionalUsdc);
        Assert.Contains(EvidenceId.ToString("D"), resolution.SignalProof.EvidenceIds);
        Assert.StartsWith("0x", resolution.SignalProof.RiskEnvelopeHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StrategyDecisionResolverBuildsProofOnlyWhenRiskEnvelopeIsPresent()
    {
        var sourcePolicyHash = Hash("policy");
        var decision = CreateDecision($$"""
            {
              "expectedEdgeBps": 42,
              "validUntilUtc": "2026-05-12T10:30:00Z",
              "sourcePolicyHash": "{{sourcePolicyHash}}",
              "evidenceIds": [ "{{EvidenceId:D}}" ],
              "riskEnvelope": {
                "maxNotionalUsdc": 75,
                "riskTier": "paper",
                "constraints": [ "max_notional", "paper_only" ]
              }
            }
            """);
        var resolver = new ArcStrategyDecisionSignalProofResolver(
            new FakeStrategyDecisionQueryService { Decision = decision },
            new StaticOptionsMonitor<ArcSettlementOptions>(CreateOptions()));

        var resolution = await resolver.ResolveAsync(
            new PublishArcSignalSourceRequest(
                ArcProofSourceKind.StrategyDecision,
                decision.DecisionId.ToString("D"),
                "operator-1",
                "publish selected strategy decision"));

        Assert.Equal(ArcSignalSourceReviewStatus.Approved, resolution.SourceReviewStatus);
        Assert.Equal(sourcePolicyHash, resolution.SourcePolicyHash);
        Assert.Equal(ArcProofSourceKind.StrategyDecision, resolution.SignalProof.SourceKind);
        Assert.Equal(decision.DecisionId.ToString("D"), resolution.SignalProof.SourceId);
        Assert.Equal("strategy-main", resolution.SignalProof.StrategyId);
        Assert.Equal("market-1", resolution.SignalProof.MarketId);
        Assert.Equal(42m, resolution.SignalProof.ExpectedEdgeBps);
        Assert.Equal(75m, resolution.SignalProof.MaxNotionalUsdc);
        Assert.Contains(EvidenceId.ToString("D"), resolution.SignalProof.EvidenceIds);
        Assert.StartsWith("0x", resolution.SignalProof.RiskEnvelopeHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StrategyDecisionResolverRejectsContextWithoutRiskEnvelope()
    {
        var decision = CreateDecision("""{"expectedEdgeBps":42,"maxNotionalUsdc":75}""");
        var resolver = new ArcStrategyDecisionSignalProofResolver(
            new FakeStrategyDecisionQueryService { Decision = decision },
            new StaticOptionsMonitor<ArcSettlementOptions>(CreateOptions()));

        var ex = await Assert.ThrowsAsync<ArcSignalSourceResolutionException>(
            () => resolver.ResolveAsync(
                new PublishArcSignalSourceRequest(
                    ArcProofSourceKind.StrategyDecision,
                    decision.DecisionId.ToString("D"),
                    "operator-1",
                    "publish selected strategy decision")));

        Assert.Equal("DECISION_RISK_ENVELOPE_MISSING", ex.ErrorCode);
    }

    private static MarketOpportunityDto CreateOpportunity(OpportunityStatus status)
    {
        var policy = new CompiledOpportunityPolicy(
            OpportunityId,
            ResearchRunId,
            "market-1",
            OutcomeSide.Yes,
            FairProbability: 0.58m,
            Confidence: 0.71m,
            Edge: 0.045m,
            EntryMaxPrice: 0.52m,
            TakeProfitPrice: 0.61m,
            StopLossPrice: 0.44m,
            MaxSpread: 0.03m,
            Quantity: 100m,
            MaxNotional: 75m,
            SourceTime.AddHours(1),
            [EvidenceId]);

        return new MarketOpportunityDto(
            OpportunityId,
            ResearchRunId,
            "market-1",
            OutcomeSide.Yes,
            FairProbability: 0.58m,
            Confidence: 0.71m,
            Edge: 0.045m,
            status,
            SourceTime.AddHours(1),
            "Evidence-backed mispricing.",
            JsonSerializer.Serialize(new[] { EvidenceId }, JsonOptions),
            """{"modelScore":0.91}""",
            JsonSerializer.Serialize(policy, JsonOptions),
            SourceTime,
            SourceTime);
    }

    private static EvidenceItemDto CreateEvidence()
        => new(
            EvidenceId,
            ResearchRunId,
            EvidenceSourceKind.Polymarket,
            "polymarket-orderbook",
            "https://polymarket.example/market-1",
            "market-1 order book",
            "Order book showed positive edge.",
            SourceTime.AddMinutes(-2),
            SourceTime,
            Hash("evidence"),
            0.95m);

    private static StrategyDecisionRecord CreateDecision(string contextJson)
        => new(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "strategy-main",
            "Entry",
            "Positive edge after risk check.",
            "market-1",
            contextJson,
            SourceTime,
            "cfg-001",
            "corr-1",
            "Paper");

    private static ArcSettlementOptions CreateOptions()
        => new()
        {
            SignalProof = new ArcSettlementSignalProofOptions
            {
                AgentAddress = "0x0000000000000000000000000000000000000001",
                Venue = "polymarket",
                OpportunityStrategyId = "llm_opportunity",
                DecisionValidForMinutes = 30,
                DefaultRiskTier = "paper"
            }
        };

    private static string Hash(string value)
        => $"0x{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()}";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private sealed class FakeOpportunityQueryService : IOpportunityQueryService
    {
        public MarketOpportunityDto? Opportunity { get; init; }

        public IReadOnlyList<EvidenceItemDto> Evidence { get; init; } = [];

        public Task<IReadOnlyList<MarketOpportunityDto>> ListOpportunitiesAsync(
            OpportunityStatus? status,
            int limit = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MarketOpportunityDto>>([]);

        public Task<MarketOpportunityDto?> GetOpportunityAsync(
            Guid opportunityId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Opportunity?.Id == opportunityId ? Opportunity : null);

        public Task<IReadOnlyList<EvidenceItemDto>> GetEvidenceAsync(
            Guid opportunityId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Evidence);
    }

    private sealed class FakeStrategyDecisionQueryService : IStrategyDecisionQueryService
    {
        public StrategyDecisionRecord? Decision { get; init; }

        public Task<IReadOnlyList<StrategyDecision>> QueryAsync(
            StrategyDecisionQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StrategyDecision>>([]);

        public Task<IReadOnlyList<StrategyDecisionRecord>> QueryRecordsAsync(
            StrategyDecisionQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StrategyDecisionRecord>>([]);

        public Task<StrategyDecisionRecord?> GetAsync(
            Guid decisionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Decision?.DecisionId == decisionId ? Decision : null);
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener)
            => null;
    }
}
