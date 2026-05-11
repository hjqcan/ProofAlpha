using Autotrade.Trading.Domain.Shared.Enums;
using NetDevPack.Domain;

namespace Autotrade.Trading.Domain.Entities;

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

/// <summary>
/// 订单事件：记录订单生命周期中的状态变化（审计/历史）。
/// </summary>
public sealed class OrderEvent : Entity
{
    // EF Core
    private OrderEvent()
    {
        OrderId = Guid.Empty;
        ClientOrderId = string.Empty;
        StrategyId = string.Empty;
        MarketId = string.Empty;
        EventType = OrderEventType.Created;
        Status = OrderStatus.Pending;
        Message = string.Empty;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public OrderEvent(
        Guid orderId,
        string clientOrderId,
        string strategyId,
        string marketId,
        OrderEventType eventType,
        OrderStatus status,
        string? message = null,
        string? contextJson = null,
        string? correlationId = null,
        Guid? runSessionId = null)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("订单 ID 不能为空", nameof(orderId));
        }

        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            throw new ArgumentException("客户端订单 ID 不能为空", nameof(clientOrderId));
        }

        OrderId = orderId;
        ClientOrderId = clientOrderId.Trim();
        StrategyId = strategyId?.Trim() ?? string.Empty;
        MarketId = marketId?.Trim() ?? string.Empty;
        EventType = eventType;
        Status = status;
        Message = message?.Trim() ?? string.Empty;
        ContextJson = contextJson ?? "{}";
        CorrelationId = correlationId?.Trim();
        RunSessionId = runSessionId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 关联的订单 ID。
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// 客户端订单 ID。
    /// </summary>
    public string ClientOrderId { get; private set; }

    /// <summary>
    /// 策略 ID。
    /// </summary>
    public string StrategyId { get; private set; }

    /// <summary>
    /// 市场 ID。
    /// </summary>
    public string MarketId { get; private set; }

    /// <summary>
    /// 事件类型。
    /// </summary>
    public OrderEventType EventType { get; private set; }

    /// <summary>
    /// 订单状态。
    /// </summary>
    public OrderStatus Status { get; private set; }

    /// <summary>
    /// 事件消息/描述。
    /// </summary>
    public string Message { get; private set; }

    /// <summary>
    /// 事件上下文（JSON 格式）。
    /// </summary>
    public string? ContextJson { get; private set; }

    /// <summary>
    /// 关联 ID（用于跟踪）。
    /// </summary>
    public string? CorrelationId { get; private set; }

    public Guid? RunSessionId { get; private set; }

    /// <summary>
    /// 事件创建时间。
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
