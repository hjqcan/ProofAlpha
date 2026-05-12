using System.Text.Json;
using Autotrade.Api.Controllers;
using Autotrade.ArcSettlement.Application.Contract.Performance;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Tests;

public sealed class ArcPerformanceControllerContractTests
{
    [Fact]
    public async Task GetAgentAsync_ReturnsAgentReputation()
    {
        var service = new FakeArcPerformanceRecorder
        {
            AgentResult = CreateReputation(scope: "agent", strategyId: null)
        };
        var controller = new ArcPerformanceController(service);

        var result = await controller.GetAgentAsync(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var reputation = Assert.IsType<ArcAgentReputation>(ok.Value);
        Assert.Equal("agent", reputation.Scope);
        Assert.Equal(1, service.GetAgentCallCount);
    }

    [Fact]
    public async Task GetStrategyAsync_ForwardsStrategyIdAndReturnsReputation()
    {
        var service = new FakeArcPerformanceRecorder
        {
            StrategyResult = CreateReputation(scope: "strategy", strategyId: "dual_leg_arbitrage")
        };
        var controller = new ArcPerformanceController(service);

        var result = await controller.GetStrategyAsync("dual_leg_arbitrage", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var reputation = Assert.IsType<ArcAgentReputation>(ok.Value);
        Assert.Equal("dual_leg_arbitrage", service.LastStrategyId);
        Assert.Equal("strategy", reputation.Scope);
        Assert.Equal("dual_leg_arbitrage", reputation.StrategyId);
    }

    [Fact]
    public async Task GetOutcomeAsync_ReturnsNotFoundForMissingOutcome()
    {
        var controller = new ArcPerformanceController(new FakeArcPerformanceRecorder());

        var result = await controller.GetOutcomeAsync(SignalId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetOutcomeAsync_ReturnsOutcomeWithoutSecretLikeFields()
    {
        var service = new FakeArcPerformanceRecorder
        {
            OutcomeResult = CreateOutcome()
        };
        var controller = new ArcPerformanceController(service);

        var result = await controller.GetOutcomeAsync(SignalId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var outcome = Assert.IsType<ArcPerformanceOutcomeRecord>(ok.Value);
        Assert.Equal(SignalId, outcome.SignalId);
        Assert.Equal(SignalId, service.LastOutcomeSignalId);

        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("privateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiSecret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signature", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ARC_SETTLEMENT_PRIVATE_KEY", json, StringComparison.Ordinal);
    }

    private const string SignalId = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static ArcAgentReputation CreateReputation(string scope, string? strategyId)
        => new(
            scope,
            strategyId,
            TotalSignals: 4,
            TerminalSignals: 3,
            PendingSignals: 1,
            ExecutedSignals: 1,
            ExpiredSignals: 1,
            RejectedSignals: 1,
            SkippedSignals: 0,
            FailedSignals: 0,
            CancelledSignals: 0,
            WinCount: 1,
            LossCount: 1,
            FlatCount: 0,
            AverageRealizedPnlBps: 8m,
            AverageSlippageBps: 2m,
            RiskRejectionRate: Decimal.Divide(1m, 3m),
            ConfidenceCoverage: 0.75m,
            CalculatedAtUtc: DateTimeOffset.Parse("2026-05-12T10:00:00Z"));

    private static ArcPerformanceOutcomeRecord CreateOutcome()
        => new(
            "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            SignalId,
            "paper-order-1",
            "dual_leg_arbitrage",
            "market-1",
            ArcPerformanceOutcomeStatus.ExecutedLoss,
            RealizedPnlBps: -12m,
            SlippageBps: 3m,
            FillRate: 1m,
            ReasonCode: "FILLED",
            OutcomeHash: "0xcccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            TransactionHash: "0xdddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            ExplorerUrl: "https://explorer.arc.test/tx/0xdddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
            ArcPerformanceRecordStatus.Confirmed,
            ErrorCode: null,
            CreatedAtUtc: DateTimeOffset.Parse("2026-05-12T09:59:00Z"),
            RecordedAtUtc: DateTimeOffset.Parse("2026-05-12T10:00:00Z"));

    private sealed class FakeArcPerformanceRecorder : IArcPerformanceRecorder
    {
        public int GetAgentCallCount { get; private set; }

        public string? LastStrategyId { get; private set; }

        public string? LastOutcomeSignalId { get; private set; }

        public ArcAgentReputation AgentResult { get; init; } = CreateReputation("agent", null);

        public ArcAgentReputation StrategyResult { get; init; } = CreateReputation("strategy", "dual_leg_arbitrage");

        public ArcPerformanceOutcomeRecord? OutcomeResult { get; init; }

        public Task<ArcPerformanceRecordResult> RecordAsync(
            RecordArcPerformanceOutcomeRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ArcAgentReputation> GetAgentReputationAsync(
            CancellationToken cancellationToken = default)
        {
            GetAgentCallCount++;
            return Task.FromResult(AgentResult);
        }

        public Task<ArcAgentReputation> GetStrategyReputationAsync(
            string strategyId,
            CancellationToken cancellationToken = default)
        {
            LastStrategyId = strategyId;
            return Task.FromResult(StrategyResult);
        }

        public Task<ArcPerformanceOutcomeRecord?> GetOutcomeAsync(
            string signalId,
            CancellationToken cancellationToken = default)
        {
            LastOutcomeSignalId = signalId;
            return Task.FromResult(OutcomeResult);
        }
    }
}
