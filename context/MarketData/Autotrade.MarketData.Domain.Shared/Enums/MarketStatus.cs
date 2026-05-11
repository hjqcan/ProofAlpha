namespace Autotrade.MarketData.Domain.Shared.Enums;

/// <summary>
/// 市场状态（简化）。
/// </summary>
public enum MarketStatus
{
    Unknown = 0,
    Active = 1,
    Suspended = 2,
    Resolved = 3,
    Closed = 4,
    Cancelled = 5
}

