using Autotrade.Application.DTOs;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Contract.Repositories;

/// <summary>
/// 订单事件仓储接口。
/// </summary>
public interface IOrderEventRepository
{
    /// <summary>
    /// 根据订单 ID 获取事件列表。
    /// </summary>
    Task<IReadOnlyList<OrderEventDto>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据客户端订单 ID 获取事件列表。
    /// </summary>
    Task<IReadOnlyList<OrderEventDto>> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderEventDto>> GetByRunSessionIdAsync(
        Guid runSessionId,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取策略的订单事件列表。
    /// </summary>
    Task<IReadOnlyList<OrderEventDto>> GetByStrategyIdAsync(
        string strategyId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页获取订单事件列表。
    /// </summary>
    Task<PagedResultDto<OrderEventDto>> GetPagedAsync(
        int page,
        int pageSize,
        string? strategyId = null,
        string? marketId = null,
        OrderEventType? eventType = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加订单事件。
    /// </summary>
    Task AddAsync(OrderEventDto orderEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量添加订单事件。
    /// </summary>
    Task AddRangeAsync(IEnumerable<OrderEventDto> orderEvents, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定时间之前的订单事件。
    /// </summary>
    Task<int> DeleteBeforeAsync(DateTimeOffset beforeUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// 订单事件 DTO。
/// </summary>
public sealed record OrderEventDto(
    Guid Id,
    Guid OrderId,
    string ClientOrderId,
    string StrategyId,
    string MarketId,
    OrderEventType EventType,
    OrderStatus Status,
    string Message,
    string? ContextJson,
    string? CorrelationId,
    DateTimeOffset CreatedAtUtc,
    Guid? RunSessionId = null);

/// <summary>
/// 订单事件类型。
/// </summary>
public enum OrderEventType
{
    Created = 0,
    Submitted = 1,
    Accepted = 2,
    Rejected = 3,
    PartiallyFilled = 4,
    Filled = 5,
    CancelPending = 6,
    Cancelled = 7,
    Expired = 8,
    Amended = 9
}
