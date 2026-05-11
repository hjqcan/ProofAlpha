using Autotrade.Strategy.Application.RunSessions;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Tests.RunSessions;

public sealed class PaperRunSessionServiceTests
{
    [Fact]
    public async Task StartOrRecoverAsync_CreatesActiveSessionWithNormalizedMetadata()
    {
        var repository = new InMemoryPaperRunSessionRepository();
        var service = CreateService(repository);
        var startedAt = new DateTimeOffset(2026, 5, 3, 1, 2, 3, TimeSpan.Zero);

        var result = await service.StartOrRecoverAsync(new PaperRunSessionStartRequest(
            " cfg-v1 ",
            ["liquidity_pulse", "dual_leg_arbitrage", "LIQUIDITY_PULSE"],
            "{\"maxNotional\":100}",
            " cli ",
            StartedAtUtc: startedAt));

        Assert.False(result.Recovered);
        Assert.True(result.IsActive);
        Assert.Equal("Paper", result.ExecutionMode);
        Assert.Equal("cfg-v1", result.ConfigVersion);
        Assert.Equal(["dual_leg_arbitrage", "liquidity_pulse"], result.Strategies);
        Assert.Equal("{\"maxNotional\":100}", result.RiskProfileJson);
        Assert.Equal("cli", result.OperatorSource);
        Assert.Equal(startedAt, result.StartedAtUtc);
        Assert.Single(repository.Sessions);
    }

    [Fact]
    public async Task StartOrRecoverAsync_ReusesActiveSessionUnlessForceNewSessionIsRequested()
    {
        var repository = new InMemoryPaperRunSessionRepository();
        var service = CreateService(repository);

        var first = await service.StartOrRecoverAsync(CreateStartRequest("cfg-v1"));
        var recovered = await service.StartOrRecoverAsync(CreateStartRequest("cfg-v2"));

        Assert.Equal(first.SessionId, recovered.SessionId);
        Assert.True(recovered.Recovered);
        Assert.Single(repository.Sessions);

        var replacement = await service.StartOrRecoverAsync(CreateStartRequest(
            "cfg-v2",
            forceNewSession: true,
            startedAtUtc: first.StartedAtUtc.AddMinutes(5)));

        Assert.NotEqual(first.SessionId, replacement.SessionId);
        Assert.False(replacement.Recovered);
        Assert.Equal(2, repository.Sessions.Count);
        var stopped = await service.ExportAsync(first.SessionId);
        Assert.NotNull(stopped);
        Assert.False(stopped.IsActive);
        Assert.Equal("Superseded by explicit Paper run start.", stopped.StopReason);
    }

    [Fact]
    public async Task StartOrRecoverAsync_RecoversActiveSessionCreatedByConcurrentStart()
    {
        var repository = new ConcurrentInsertPaperRunSessionRepository();
        var service = CreateService(repository);

        var result = await service.StartOrRecoverAsync(CreateStartRequest("cfg-v1"));

        Assert.True(result.Recovered);
        Assert.Equal(repository.ConcurrentSession.Id, result.SessionId);
        Assert.Equal("cfg-concurrent", result.ConfigVersion);
    }

    [Fact]
    public async Task StopAsync_StopsActiveSessionAndExportAsyncReturnsFinalRecord()
    {
        var repository = new InMemoryPaperRunSessionRepository();
        var service = CreateService(repository);
        var started = await service.StartOrRecoverAsync(CreateStartRequest("cfg-v1"));
        var stoppedAt = started.StartedAtUtc.AddMinutes(30);

        var stopped = await service.StopAsync(new PaperRunSessionStopRequest(
            started.SessionId,
            "operator-1",
            null,
            stoppedAt));

        Assert.NotNull(stopped);
        Assert.False(stopped.IsActive);
        Assert.Equal(stoppedAt, stopped.StoppedAtUtc);
        Assert.Equal("Stopped by operator-1", stopped.StopReason);

        var active = await service.GetActiveAsync();
        Assert.Null(active);

        var exported = await service.ExportAsync(started.SessionId);
        Assert.NotNull(exported);
        Assert.Equal(started.SessionId, exported.SessionId);
        Assert.Equal(stopped.StopReason, exported.StopReason);
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsOnlyActivePaperSessionIdentity()
    {
        var repository = new InMemoryPaperRunSessionRepository();
        var service = CreateService(repository);
        var started = await service.StartOrRecoverAsync(CreateStartRequest("cfg-v1"));

        var paper = await service.GetCurrentAsync("Paper");
        var live = await service.GetCurrentAsync("Live");

        Assert.NotNull(paper);
        Assert.Equal(started.SessionId, paper.SessionId);
        Assert.Equal("cfg-v1", paper.ConfigVersion);
        Assert.Null(live);
    }

    private static PaperRunSessionService CreateService(IPaperRunSessionRepository repository)
        => new(repository, Options.Create(new ExecutionOptions { Mode = ExecutionMode.Paper }));

    private static PaperRunSessionStartRequest CreateStartRequest(
        string configVersion,
        bool forceNewSession = false,
        DateTimeOffset? startedAtUtc = null)
        => new(
            configVersion,
            ["dual_leg_arbitrage"],
            "{\"maxExposure\":250}",
            "test",
            forceNewSession,
            startedAtUtc ?? DateTimeOffset.UtcNow);

    private sealed class InMemoryPaperRunSessionRepository : IPaperRunSessionRepository
    {
        public List<PaperRunSession> Sessions { get; } = [];

        public Task<PaperRunSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(Sessions.FirstOrDefault(session => session.Id == sessionId));

        public Task<PaperRunSession?> GetActiveAsync(string executionMode, CancellationToken cancellationToken = default)
            => Task.FromResult(Sessions.FirstOrDefault(session =>
                session.ExecutionMode == executionMode && session.IsActive));

        public Task AddAsync(PaperRunSession session, CancellationToken cancellationToken = default)
        {
            Sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(PaperRunSession session, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ConcurrentInsertPaperRunSessionRepository : IPaperRunSessionRepository
    {
        private int _activeLookupCount;

        public PaperRunSession ConcurrentSession { get; } = new(
            "Paper",
            "cfg-concurrent",
            "[\"dual_leg_arbitrage\"]",
            "{\"maxExposure\":250}",
            "other-process",
            DateTimeOffset.UtcNow);

        public Task<PaperRunSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(sessionId == ConcurrentSession.Id ? ConcurrentSession : null);

        public Task<PaperRunSession?> GetActiveAsync(string executionMode, CancellationToken cancellationToken = default)
        {
            _activeLookupCount++;
            return Task.FromResult(_activeLookupCount == 1 ? null : ConcurrentSession);
        }

        public Task AddAsync(PaperRunSession session, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Active Paper session already exists.");

        public Task UpdateAsync(PaperRunSession session, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
