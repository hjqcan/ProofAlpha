using Autotrade.Application.DTOs;
using Autotrade.Application.RunSessions;
using Autotrade.Trading.Application.Audit;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Application.EventHandlers;
using Autotrade.Trading.Domain.Events;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetDevPack.Messaging;

namespace Autotrade.Trading.Tests.Audit;

public sealed class OrderAuditLoggerRunSessionTests
{
    [Fact]
    public async Task LogOrderCreatedAsync_AttachesActivePaperRunSession()
    {
        var repository = new CapturingOrderEventRepository();
        var sessionId = Guid.NewGuid();
        var logger = CreateAuditLogger(
            repository,
            ExecutionMode.Paper,
            new StubRunSessionAccessor(new RunSessionIdentity(
                sessionId,
                "Paper",
                "cfg-v1",
                DateTimeOffset.UtcNow)));

        await logger.LogOrderCreatedAsync(
            Guid.NewGuid(),
            "client-1",
            "dual_leg_arbitrage",
            "market-1",
            "corr-1");

        var orderEvent = Assert.Single(repository.Events);
        Assert.Equal(sessionId, orderEvent.RunSessionId);
    }

    [Fact]
    public async Task LogOrderCreatedAsync_DoesNotQueryRunSessionOutsidePaperMode()
    {
        var repository = new CapturingOrderEventRepository();
        var accessor = new ThrowingRunSessionAccessor();
        var logger = CreateAuditLogger(repository, ExecutionMode.Live, accessor);

        await logger.LogOrderCreatedAsync(
            Guid.NewGuid(),
            "client-1",
            "dual_leg_arbitrage",
            "market-1",
            "corr-1");

        var orderEvent = Assert.Single(repository.Events);
        Assert.Null(orderEvent.RunSessionId);
    }

    [Fact]
    public async Task OrderAcceptedEventHandler_AttachesActivePaperRunSession()
    {
        var repository = new CapturingOrderEventRepository();
        var sessionId = Guid.NewGuid();
        var handler = new OrderAcceptedEventHandler(
            repository,
            NullLogger<OrderAcceptedEventHandler>.Instance,
            Options.Create(new ExecutionOptions { Mode = ExecutionMode.Paper }),
            new StubRunSessionAccessor(new RunSessionIdentity(
                sessionId,
                "Paper",
                "cfg-v1",
                DateTimeOffset.UtcNow)));

        await handler.Handle(new OrderAcceptedEvent(
            Guid.NewGuid(),
            "client-1",
            "dual_leg_arbitrage",
            "market-1",
            "exchange-1",
            "corr-1"));

        var orderEvent = Assert.Single(repository.Events);
        Assert.Equal(sessionId, orderEvent.RunSessionId);
        Assert.Equal(OrderEventType.Accepted, orderEvent.EventType);
    }

    private static OrderAuditLogger CreateAuditLogger(
        CapturingOrderEventRepository repository,
        ExecutionMode mode,
        IRunSessionAccessor accessor)
        => new(
            repository,
            new NoopDomainEventDispatcher(),
            NullLogger<OrderAuditLogger>.Instance,
            Options.Create(new ExecutionOptions { Mode = mode }),
            accessor);

    private sealed class CapturingOrderEventRepository : IOrderEventRepository
    {
        public List<OrderEventDto> Events { get; } = [];

        public Task<IReadOnlyList<OrderEventDto>> GetByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderEventDto>>(Events.Where(e => e.OrderId == orderId).ToList());

        public Task<IReadOnlyList<OrderEventDto>> GetByClientOrderIdAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OrderEventDto>>(Events.Where(e => e.ClientOrderId == clientOrderId).ToList());

        public Task<IReadOnlyList<OrderEventDto>> GetByRunSessionIdAsync(
            Guid runSessionId,
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            var query = Events.Where(e => e.RunSessionId == runSessionId);
            if (limit.HasValue)
            {
                query = query.Take(limit.Value);
            }

            return Task.FromResult<IReadOnlyList<OrderEventDto>>(query.ToList());
        }

        public Task<IReadOnlyList<OrderEventDto>> GetByStrategyIdAsync(
            string strategyId,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            var query = Events.Where(e => e.StrategyId == strategyId);
            if (from.HasValue)
            {
                query = query.Where(e => e.CreatedAtUtc >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(e => e.CreatedAtUtc <= to.Value);
            }

            if (limit.HasValue)
            {
                query = query.Take(limit.Value);
            }

            return Task.FromResult<IReadOnlyList<OrderEventDto>>(query.ToList());
        }

        public Task<PagedResultDto<OrderEventDto>> GetPagedAsync(
            int page,
            int pageSize,
            string? strategyId = null,
            string? marketId = null,
            OrderEventType? eventType = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResultDto<OrderEventDto>(Events, Events.Count, page, pageSize));

        public Task AddAsync(OrderEventDto orderEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(orderEvent);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<OrderEventDto> orderEvents, CancellationToken cancellationToken = default)
        {
            Events.AddRange(orderEvents);
            return Task.CompletedTask;
        }

        public Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class NoopDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<DomainEvent> domainEvents) => Task.CompletedTask;

        public void Dispatch(IEnumerable<DomainEvent> domainEvents)
        {
        }
    }

    private sealed class StubRunSessionAccessor(RunSessionIdentity? identity) : IRunSessionAccessor
    {
        public Task<RunSessionIdentity?> GetCurrentAsync(
            string executionMode,
            CancellationToken cancellationToken = default)
            => Task.FromResult(identity);
    }

    private sealed class ThrowingRunSessionAccessor : IRunSessionAccessor
    {
        public Task<RunSessionIdentity?> GetCurrentAsync(
            string executionMode,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Accessor should not be called outside Paper mode.");
    }
}
