using Autotrade.Application.RunSessions;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Decisions;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Tests.Decisions;

public sealed class StrategyDecisionLoggerRunSessionTests
{
    [Fact]
    public async Task LogAsync_AttachesActiveRunSessionWhenDecisionHasNoExplicitSession()
    {
        var repository = new CapturingStrategyDecisionRepository();
        var sessionId = Guid.NewGuid();
        var accessor = new StubRunSessionAccessor(new RunSessionIdentity(
            sessionId,
            "Paper",
            "cfg-v1",
            DateTimeOffset.UtcNow));
        var logger = CreateLogger(repository, accessor);

        await logger.LogAsync(new StrategyDecision(
            "dual_leg_arbitrage",
            "Hold",
            "no spread",
            "market-1",
            "{}",
            DateTimeOffset.UtcNow));

        var log = Assert.Single(repository.Logs);
        Assert.Equal(sessionId, log.RunSessionId);
        Assert.Equal("Paper", log.ExecutionMode);
        Assert.Equal("Paper", accessor.RequestedExecutionMode);
    }

    [Fact]
    public async Task LogAsync_KeepsExplicitRunSessionAndDoesNotQueryAccessor()
    {
        var repository = new CapturingStrategyDecisionRepository();
        var accessor = new StubRunSessionAccessor(new RunSessionIdentity(
            Guid.NewGuid(),
            "Paper",
            "cfg-v1",
            DateTimeOffset.UtcNow));
        var explicitSessionId = Guid.NewGuid();
        var logger = CreateLogger(repository, accessor);

        await logger.LogAsync(new StrategyDecision(
            "dual_leg_arbitrage",
            "Buy",
            "spread captured",
            "market-1",
            "{}",
            DateTimeOffset.UtcNow,
            RunSessionId: explicitSessionId));

        var log = Assert.Single(repository.Logs);
        Assert.Equal(explicitSessionId, log.RunSessionId);
        Assert.Null(accessor.RequestedExecutionMode);
    }

    private static StrategyDecisionLogger CreateLogger(
        CapturingStrategyDecisionRepository repository,
        IRunSessionAccessor accessor)
        => new(
            repository,
            Options.Create(new StrategyEngineOptions
            {
                DecisionLogEnabled = true,
                ConfigVersion = "cfg-v1"
            }),
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Paper }),
            NullLogger<StrategyDecisionLogger>.Instance,
            accessor);

    private sealed class CapturingStrategyDecisionRepository : IStrategyDecisionRepository
    {
        public List<StrategyDecisionLog> Logs { get; } = [];

        public Task AddAsync(StrategyDecisionLog log, CancellationToken cancellationToken = default)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StrategyDecisionLog>> QueryAsync(
            StrategyDecisionQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StrategyDecisionLog>>(Logs);

        public Task<StrategyDecisionLog?> GetAsync(Guid decisionId, CancellationToken cancellationToken = default)
            => Task.FromResult(Logs.FirstOrDefault(log => log.Id == decisionId));
    }

    private sealed class StubRunSessionAccessor(RunSessionIdentity? identity) : IRunSessionAccessor
    {
        public string? RequestedExecutionMode { get; private set; }

        public Task<RunSessionIdentity?> GetCurrentAsync(
            string executionMode,
            CancellationToken cancellationToken = default)
        {
            RequestedExecutionMode = executionMode;
            return Task.FromResult(identity);
        }
    }
}
