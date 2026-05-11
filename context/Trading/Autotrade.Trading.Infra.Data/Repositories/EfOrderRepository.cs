using Autotrade.Application.DTOs;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.ValueObjects;
using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Trading.Infra.Data.Repositories;

/// <summary>
/// 订单仓储 EF Core 实现。
/// </summary>
public sealed class EfOrderRepository : IOrderRepository
{
    private readonly TradingContext _context;

    public EfOrderRepository(TradingContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(OrderDto order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var entity = ToEntity(order);
        ApplyDtoState(entity, order);
        await _context.Orders.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddRangeAsync(IEnumerable<OrderDto> orders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var entities = orders.Select(dto =>
            {
                var entity = ToEntity(dto);
                ApplyDtoState(entity, dto);
                return entity;
            })
            .ToList();
        if (entities.Count == 0) return;

        await _context.Orders.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(OrderDto order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var entity = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == order.Id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new InvalidOperationException($"Order with ID {order.Id} not found");
        }

        // Apply state changes based on DTO status
        ApplyDtoState(entity, order);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return order is null ? null : ToDto(order);
    }

    public async Task<OrderDto?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return null;
        }

        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.ClientOrderId == clientOrderId, cancellationToken)
            .ConfigureAwait(false);

        return order is null ? null : ToDto(order);
    }

    public async Task<OrderDto?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            return null;
        }

        var order = await _context.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.ExchangeOrderId == exchangeOrderId, cancellationToken)
            .ConfigureAwait(false);

        return order is null ? null : ToDto(order);
    }

    public async Task<IReadOnlyList<OrderDto>> GetOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Pending
                     || o.Status == OrderStatus.Open
                     || o.Status == OrderStatus.PartiallyFilled)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return orders.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<OrderDto>> GetByStrategyIdAsync(
        string strategyId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Orders.AsNoTracking()
            .Where(o => o.StrategyId == strategyId);

        query = ApplyTimeFilter(query, from, to);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        var orders = await query.OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return orders.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<OrderDto>> GetByMarketIdAsync(
        string marketId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Orders.AsNoTracking()
            .Where(o => o.MarketId == marketId);

        query = ApplyTimeFilter(query, from, to);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        var orders = await query.OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return orders.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<OrderDto>> GetByStatusAsync(
        OrderStatus status,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Orders.AsNoTracking()
            .Where(o => o.Status == status);

        query = ApplyTimeFilter(query, from, to);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        var orders = await query.OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return orders.Select(ToDto).ToList();
    }

    public async Task<PagedResultDto<OrderDto>> GetPagedAsync(
        int page,
        int pageSize,
        string? strategyId = null,
        string? marketId = null,
        OrderStatus? status = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 1000) pageSize = 1000;

        var query = _context.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(strategyId))
        {
            query = query.Where(o => o.StrategyId == strategyId);
        }

        if (!string.IsNullOrWhiteSpace(marketId))
        {
            query = query.Where(o => o.MarketId == marketId);
        }

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        query = ApplyTimeFilter(query, from, to);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var orders = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResultDto<OrderDto>(orders.Select(ToDto).ToList(), totalCount, page, pageSize);
    }

    public async Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Where(o => o.CreatedAtUtc < beforeUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static IQueryable<Domain.Entities.Order> ApplyTimeFilter(
        IQueryable<Domain.Entities.Order> query,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from.HasValue)
        {
            query = query.Where(o => o.CreatedAtUtc >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(o => o.CreatedAtUtc <= to.Value);
        }

        return query;
    }

    private static OrderDto ToDto(Order o) => new(
        o.Id,
        o.TradingAccountId,
        o.MarketId,
        o.TokenId,
        o.StrategyId,
        o.ClientOrderId,
        o.ExchangeOrderId,
        o.CorrelationId,
        o.Outcome,
        o.Side,
        o.OrderType,
        o.TimeInForce,
        o.GoodTilDateUtc,
        o.NegRisk,
        o.Price.Value,
        o.Quantity.Value,
        o.FilledQuantity.Value,
        o.Status,
        o.RejectionReason,
        o.CreatedAtUtc,
        o.UpdatedAtUtc,
        o.OrderSalt,
        o.OrderTimestamp);

    private static Order ToEntity(OrderDto dto)
    {
        var entity = new Order(
            dto.TradingAccountId,
            dto.MarketId,
            dto.Outcome,
            dto.Side,
            dto.OrderType,
            dto.TimeInForce,
            new Price(dto.Price),
            new Quantity(dto.Quantity),
            dto.GoodTilDateUtc,
            dto.NegRisk);

        entity.SetClientInfo(
            dto.StrategyId ?? string.Empty,
            dto.ClientOrderId ?? string.Empty,
            dto.TokenId,
            dto.CorrelationId);
        entity.SetExchangeOrderId(dto.ExchangeOrderId);
        entity.SetOrderSigningPayload(dto.OrderSalt, dto.OrderTimestamp);

        // Use reflection to set Id
        var idProperty = typeof(NetDevPack.Domain.Entity).GetProperty("Id");
        idProperty?.SetValue(entity, dto.Id);

        return entity;
    }

    private static void ApplyDtoState(Order entity, OrderDto dto)
    {
        entity.SetMarketInfo(dto.MarketId, dto.TokenId);
        entity.SetNegRisk(dto.NegRisk);

        // Update client info
        entity.SetClientInfo(
            dto.StrategyId ?? string.Empty,
            dto.ClientOrderId ?? string.Empty,
            dto.TokenId,
            dto.CorrelationId);
        entity.SetExchangeOrderId(dto.ExchangeOrderId);
        entity.SetOrderSigningPayload(dto.OrderSalt, dto.OrderTimestamp);

        // 1) Apply fill delta first (so later status transitions like Cancel/Expire can be applied on top)
        if (dto.FilledQuantity > 0m && dto.FilledQuantity != entity.FilledQuantity.Value)
        {
            entity.SetFilledQuantity(new Quantity(dto.FilledQuantity));
        }

        // 2) Apply explicit status transition
        switch (dto.Status)
        {
            case OrderStatus.Open when entity.Status == OrderStatus.Pending:
                entity.MarkOpen();
                break;
            case OrderStatus.Rejected:
                entity.Reject(dto.RejectionReason ?? "Unknown reason");
                break;
            case OrderStatus.Cancelled:
                entity.Cancel();
                break;
            case OrderStatus.Expired:
                entity.Expire();
                break;
        }
    }
}
