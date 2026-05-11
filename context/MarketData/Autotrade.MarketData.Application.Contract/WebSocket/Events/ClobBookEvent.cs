using System.Globalization;

namespace Autotrade.MarketData.Application.Contract.WebSocket.Events;

/// <summary>
/// 订单簿快照事件（CLOB Market 通道）。
/// </summary>
public sealed record ClobBookEvent
{
    public required string EventType { get; init; } // "book"
    public required string AssetId { get; init; }
    public required string Market { get; init; }
    public required string Timestamp { get; init; }
    public required string Hash { get; init; }
    public required IReadOnlyList<OrderLevel> Bids { get; init; }
    public required IReadOnlyList<OrderLevel> Asks { get; init; }
}

/// <summary>
/// 订单簿价格层级。
/// </summary>
public sealed record OrderLevel
{
    public required string Price { get; init; }
    public required string Size { get; init; }

    public decimal PriceDecimal => decimal.TryParse(Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : 0m;
    public decimal SizeDecimal => decimal.TryParse(Size, NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : 0m;
}
