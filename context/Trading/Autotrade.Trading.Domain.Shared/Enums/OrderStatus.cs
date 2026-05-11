namespace Autotrade.Trading.Domain.Shared.Enums;

/// <summary>
/// 订单状态（最小状态机）。
/// </summary>
public enum OrderStatus
{
    Pending = 1,
    Open = 2,
    PartiallyFilled = 3,
    Filled = 4,
    Cancelled = 5,
    Rejected = 6,
    Expired = 7
}

