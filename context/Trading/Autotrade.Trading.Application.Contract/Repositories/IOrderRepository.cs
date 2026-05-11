using Autotrade.Application.DTOs;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Contract.Repositories;

/// <summary>
/// Order repository contract.
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// Adds an order.
    /// </summary>
    Task AddAsync(OrderDto order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds orders in batch.
    /// </summary>
    Task AddRangeAsync(IEnumerable<OrderDto> orders, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an order.
    /// </summary>
    Task UpdateAsync(OrderDto order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an order by internal ID.
    /// </summary>
    Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an order by client order ID.
    /// </summary>
    Task<OrderDto?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an order by exchange order ID.
    /// </summary>
    Task<OrderDto?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets orders that are still open.
    /// </summary>
    Task<IReadOnlyList<OrderDto>> GetOpenOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets orders for a strategy.
    /// </summary>
    Task<IReadOnlyList<OrderDto>> GetByStrategyIdAsync(
        string strategyId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets orders for a market.
    /// </summary>
    Task<IReadOnlyList<OrderDto>> GetByMarketIdAsync(
        string marketId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets orders by status.
    /// </summary>
    Task<IReadOnlyList<OrderDto>> GetByStatusAsync(
        OrderStatus status,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a paged order list.
    /// </summary>
    Task<PagedResultDto<OrderDto>> GetPagedAsync(
        int page,
        int pageSize,
        string? strategyId = null,
        string? marketId = null,
        OrderStatus? status = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes orders before the specified UTC timestamp.
    /// </summary>
    Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Order DTO.
/// </summary>
public sealed record OrderDto(
    Guid Id,
    Guid TradingAccountId,
    string MarketId,
    string? TokenId,
    string? StrategyId,
    string? ClientOrderId,
    string? ExchangeOrderId,
    string? CorrelationId,
    OutcomeSide Outcome,
    OrderSide Side,
    OrderType OrderType,
    TimeInForce TimeInForce,
    DateTimeOffset? GoodTilDateUtc,
    bool NegRisk,
    decimal Price,
    decimal Quantity,
    decimal FilledQuantity,
    OrderStatus Status,
    string? RejectionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? OrderSalt = null,
    string? OrderTimestamp = null);
