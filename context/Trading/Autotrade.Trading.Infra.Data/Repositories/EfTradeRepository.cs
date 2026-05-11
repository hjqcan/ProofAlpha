using Autotrade.Application.DTOs;
using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Domain.Shared.ValueObjects;
using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Trading.Infra.Data.Repositories;

/// <summary>
/// 成交仓储 EF Core 实现。
/// </summary>
public sealed class EfTradeRepository : ITradeRepository
{
    private readonly TradingContext _context;

    public EfTradeRepository(TradingContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<TradeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var trade = await _context.Trades
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return trade is null ? null : ToDto(trade);
    }

    public async Task<IReadOnlyList<TradeDto>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var trades = await _context.Trades.AsNoTracking()
            .Where(t => t.OrderId == orderId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return trades.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<TradeDto>> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return Array.Empty<TradeDto>();
        }

        var trades = await _context.Trades.AsNoTracking()
            .Where(t => t.ClientOrderId == clientOrderId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return trades.Select(ToDto).ToList();
    }

    public async Task<TradeDto?> GetByExchangeTradeIdAsync(string exchangeTradeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeTradeId))
        {
            return null;
        }

        var trade = await _context.Trades
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.ExchangeTradeId == exchangeTradeId, cancellationToken)
            .ConfigureAwait(false);

        return trade is null ? null : ToDto(trade);
    }

    public async Task<IReadOnlyList<TradeDto>> GetByStrategyIdAsync(
        string strategyId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Trades.AsNoTracking()
            .Where(t => t.StrategyId == strategyId);

        query = ApplyTimeFilter(query, from, to);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        var trades = await query.OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return trades.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<TradeDto>> GetByMarketIdAsync(
        string marketId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Trades.AsNoTracking()
            .Where(t => t.MarketId == marketId);

        query = ApplyTimeFilter(query, from, to);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        var trades = await query.OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return trades.Select(ToDto).ToList();
    }

    public async Task<PagedResultDto<TradeDto>> GetPagedAsync(
        int page,
        int pageSize,
        string? strategyId = null,
        string? marketId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 1000) pageSize = 1000;

        var query = _context.Trades.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(strategyId))
        {
            query = query.Where(t => t.StrategyId == strategyId);
        }

        if (!string.IsNullOrWhiteSpace(marketId))
        {
            query = query.Where(t => t.MarketId == marketId);
        }

        query = ApplyTimeFilter(query, from, to);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var trades = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResultDto<TradeDto>(trades.Select(ToDto).ToList(), totalCount, page, pageSize);
    }

    public async Task AddAsync(TradeDto dto, CancellationToken cancellationToken = default)
    {
        var entity = ToEntity(dto);
        await _context.Trades.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddRangeAsync(IEnumerable<TradeDto> dtos, CancellationToken cancellationToken = default)
    {
        var entities = dtos.Select(ToEntity);
        await _context.Trades.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default)
    {
        return await _context.Trades
            .Where(t => t.CreatedAtUtc < beforeUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PnLSummary> GetPnLSummaryAsync(
        string strategyId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Trades.AsNoTracking()
            .Where(t => t.StrategyId == strategyId);

        query = ApplyTimeFilter(query, from, to);

        var trades = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        var buyTrades = trades.Where(t => t.Side == OrderSide.Buy);
        var sellTrades = trades.Where(t => t.Side == OrderSide.Sell);

        return new PnLSummary(
            strategyId,
            buyTrades.Sum(t => t.Price.Value * t.Quantity.Value),
            sellTrades.Sum(t => t.Price.Value * t.Quantity.Value),
            trades.Sum(t => t.Fee),
            trades.Count,
            from,
            to);
    }

    private static IQueryable<Domain.Entities.Trade> ApplyTimeFilter(
        IQueryable<Domain.Entities.Trade> query,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from.HasValue)
        {
            query = query.Where(t => t.CreatedAtUtc >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(t => t.CreatedAtUtc <= to.Value);
        }

        return query;
    }

    private static Trade ToEntity(TradeDto dto) => new(
        dto.OrderId,
        dto.TradingAccountId,
        dto.ClientOrderId,
        dto.StrategyId,
        dto.MarketId,
        dto.TokenId,
        dto.Outcome,
        dto.Side,
        new Price(dto.Price),
        new Quantity(dto.Quantity),
        dto.ExchangeTradeId,
        dto.Fee,
        dto.CorrelationId);

    private static TradeDto ToDto(Trade t) => new(
        t.Id,
        t.OrderId,
        t.TradingAccountId,
        t.ClientOrderId,
        t.StrategyId,
        t.MarketId,
        t.TokenId,
        t.Outcome,
        t.Side,
        t.Price.Value,
        t.Quantity.Value,
        t.ExchangeTradeId,
        t.Fee,
        t.CorrelationId,
        t.CreatedAtUtc);
}
