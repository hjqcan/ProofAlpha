using Autotrade.ArcSettlement.Application.Contract.Performance;
using Autotrade.ArcSettlement.Application.Performance;

namespace Autotrade.ArcSettlement.Tests.Performance;

public sealed class ArcPerformanceOutcomeBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WhenExecutedOutcomeHasTradeEvidence_ReturnsTrimmedRecorderRequest()
    {
        var builder = CreateBuilder();

        var result = builder.Build(CreateRequest(
            signalId: " 0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ",
            executionId: " paper-order-1 ",
            reasonCode: " TAKE_PROFIT ",
            evidenceReferences:
            [
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.StrategyDecision, "decision-1"),
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.OrderEvent, "order-event-1"),
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.Trade, "trade-1")
            ]));

        Assert.Equal("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.SignalId);
        Assert.Equal("paper-order-1", result.ExecutionId);
        Assert.Equal("dual_leg_arbitrage", result.StrategyId);
        Assert.Equal("market-1", result.MarketId);
        Assert.Equal(ArcPerformanceOutcomeStatus.ExecutedLoss, result.Status);
        Assert.Equal(-12m, result.RealizedPnlBps);
        Assert.Equal(2m, result.SlippageBps);
        Assert.Equal(1m, result.FillRate);
        Assert.Equal("TAKE_PROFIT", result.ReasonCode);
        Assert.Equal(Now, result.CreatedAtUtc);
    }

    [Fact]
    public void Build_WhenRiskRejectedHasRiskEventEvidence_ReturnsRejectedOutcome()
    {
        var builder = CreateBuilder();

        var result = builder.Build(CreateRequest(
            status: ArcPerformanceOutcomeStatus.RejectedRisk,
            realizedPnlBps: null,
            slippageBps: null,
            fillRate: null,
            reasonCode: "EXPOSURE_LIMIT",
            evidenceReferences:
            [
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.StrategyDecision, "decision-1"),
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.RiskEvent, "risk-1")
            ]));

        Assert.Equal(ArcPerformanceOutcomeStatus.RejectedRisk, result.Status);
        Assert.Equal("EXPOSURE_LIMIT", result.ReasonCode);
        Assert.Null(result.RealizedPnlBps);
    }

    [Fact]
    public void Build_WhenSignalExpiredAfterValidUntil_ReturnsExpiredOutcome()
    {
        var builder = CreateBuilder();

        var result = builder.Build(CreateRequest(
            status: ArcPerformanceOutcomeStatus.Expired,
            validUntilUtc: Now.AddSeconds(-1),
            realizedPnlBps: null,
            slippageBps: null,
            fillRate: null,
            reasonCode: "SIGNAL_EXPIRED",
            evidenceReferences:
            [
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.StrategyDecision, "decision-1"),
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.ReplayExport, "replay-1")
            ]));

        Assert.Equal(ArcPerformanceOutcomeStatus.Expired, result.Status);
        Assert.Equal("SIGNAL_EXPIRED", result.ReasonCode);
        Assert.Equal(Now, result.CreatedAtUtc);
    }

    [Fact]
    public void Build_WhenNoEvidenceReferences_Throws()
    {
        var builder = CreateBuilder();

        var request = CreateRequest(evidenceReferences: []);

        Assert.Throws<ArgumentException>(() => builder.Build(request));
    }

    [Fact]
    public void Build_WhenExecutedOutcomeHasNoOrderOrTradeEvidence_Throws()
    {
        var builder = CreateBuilder();

        var request = CreateRequest(
            evidenceReferences:
            [
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.StrategyDecision, "decision-1")
            ]);

        Assert.Throws<ArgumentException>(() => builder.Build(request));
    }

    [Fact]
    public void Build_WhenRiskRejectedHasNoRiskEventEvidence_Throws()
    {
        var builder = CreateBuilder();

        var request = CreateRequest(
            status: ArcPerformanceOutcomeStatus.RejectedRisk,
            realizedPnlBps: null,
            slippageBps: null,
            fillRate: null,
            evidenceReferences:
            [
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.StrategyDecision, "decision-1")
            ]);

        Assert.Throws<ArgumentException>(() => builder.Build(request));
    }

    [Fact]
    public void Build_WhenExpiredBeforeValidUntil_Throws()
    {
        var builder = CreateBuilder();

        var request = CreateRequest(
            status: ArcPerformanceOutcomeStatus.Expired,
            validUntilUtc: Now.AddMinutes(1),
            realizedPnlBps: null,
            slippageBps: null,
            fillRate: null,
            evidenceReferences:
            [
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.StrategyDecision, "decision-1")
            ]);

        Assert.Throws<InvalidOperationException>(() => builder.Build(request));
    }

    private static ArcPerformanceOutcomeBuilder CreateBuilder()
        => new(new FixedTimeProvider(Now));

    private static BuildArcPerformanceOutcomeRequest CreateRequest(
        string signalId = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        string executionId = "paper-order-1",
        ArcPerformanceOutcomeStatus status = ArcPerformanceOutcomeStatus.ExecutedLoss,
        DateTimeOffset? validUntilUtc = null,
        decimal? realizedPnlBps = -12m,
        decimal? slippageBps = 2m,
        decimal? fillRate = 1m,
        string? reasonCode = null,
        IReadOnlyList<ArcPerformanceEvidenceReference>? evidenceReferences = null)
        => new(
            SignalId: signalId,
            ExecutionId: executionId,
            StrategyId: "dual_leg_arbitrage",
            MarketId: "market-1",
            ValidUntilUtc: validUntilUtc ?? Now.AddMinutes(30),
            Status: status,
            RealizedPnlBps: realizedPnlBps,
            SlippageBps: slippageBps,
            FillRate: fillRate,
            ReasonCode: reasonCode,
            EvidenceReferences: evidenceReferences ??
            [
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.StrategyDecision, "decision-1"),
                new ArcPerformanceEvidenceReference(ArcPerformanceEvidenceKind.OrderEvent, "order-event-1")
            ]);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
