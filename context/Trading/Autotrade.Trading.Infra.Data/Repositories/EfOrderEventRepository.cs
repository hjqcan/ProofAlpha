using Autotrade.Application.DTOs;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using DomainOrderEventType = Autotrade.Trading.Domain.Entities.OrderEventType;
using ContractOrderEventType = Autotrade.Trading.Application.Contract.Repositories.OrderEventType;

namespace Autotrade.Trading.Infra.Data.Repositories;

/// <summary>
/// 订单事件仓储 EF Core 实现。
/// </summary>
public sealed class EfOrderEventRepository : IOrderEventRepository
{
    private readonly TradingContext _context;

    public EfOrderEventRepository(TradingContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<OrderEventDto>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var events = await _context.OrderEvents.AsNoTracking()
            .Where(e => e.OrderId == orderId)
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return events.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<OrderEventDto>> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return Array.Empty<OrderEventDto>();
        }

        var events = await _context.OrderEvents.AsNoTracking()
            .Where(e => e.ClientOrderId == clientOrderId)
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return events.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<OrderEventDto>> GetByRunSessionIdAsync(
        Guid runSessionId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (runSessionId == Guid.Empty)
        {
            return Array.Empty<OrderEventDto>();
        }

        IQueryable<OrderEvent> query = _context.OrderEvents.AsNoTracking()
            .Where(e => e.RunSessionId == runSessionId)
            .OrderBy(e => e.CreatedAtUtc);

        if (limit.HasValue)
        {
            query = query.Take(Math.Max(1, limit.Value));
        }

        var events = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return events.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<OrderEventDto>> GetByStrategyIdAsync(
        string strategyId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.OrderEvents.AsNoTracking()
            .Where(e => e.StrategyId == strategyId);

        query = ApplyTimeFilter(query, from, to);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        var events = await query.OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return events.Select(ToDto).ToList();
    }

    public async Task<PagedResultDto<OrderEventDto>> GetPagedAsync(
        int page,
        int pageSize,
        string? strategyId = null,
        string? marketId = null,
        ContractOrderEventType? eventType = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 1000) pageSize = 1000;

        var query = _context.OrderEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(strategyId))
        {
            query = query.Where(e => e.StrategyId == strategyId);
        }

        if (!string.IsNullOrWhiteSpace(marketId))
        {
            query = query.Where(e => e.MarketId == marketId);
        }

        if (eventType.HasValue)
        {
            var domainEventType = (DomainOrderEventType)(int)eventType.Value;
            query = query.Where(e => e.EventType == domainEventType);
        }

        query = ApplyTimeFilter(query, from, to);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var events = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResultDto<OrderEventDto>(events.Select(ToDto).ToList(), totalCount, page, pageSize);
    }

    public async Task AddAsync(OrderEventDto dto, CancellationToken cancellationToken = default)
    {
        var entity = ToEntity(dto);
        await _context.OrderEvents.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddRangeAsync(IEnumerable<OrderEventDto> dtos, CancellationToken cancellationToken = default)
    {
        var entities = dtos.Select(ToEntity);
        await _context.OrderEvents.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
    {
        return await _context.OrderEvents
            .Where(e => e.CreatedAtUtc < beforeUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static OrderEvent ToEntity(OrderEventDto dto) => new(
        dto.OrderId,
        dto.ClientOrderId,
        dto.StrategyId,
        dto.MarketId,
        (DomainOrderEventType)(int)dto.EventType,
        dto.Status,
        dto.Message,
        dto.ContextJson,
        dto.CorrelationId,
        dto.RunSessionId);

    private static IQueryable<OrderEvent> ApplyTimeFilter(IQueryable<OrderEvent> query, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from.HasValue)
        {
            query = query.Where(e => e.CreatedAtUtc >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(e => e.CreatedAtUtc <= to.Value);
        }

        return query;
    }

    private static OrderEventDto ToDto(OrderEvent e) => new(
        e.Id,
        e.OrderId,
        e.ClientOrderId,
        e.StrategyId,
        e.MarketId,
        (ContractOrderEventType)(int)e.EventType,
        e.Status,
        e.Message,
        e.ContextJson,
        e.CorrelationId,
        e.CreatedAtUtc,
        e.RunSessionId);
}
