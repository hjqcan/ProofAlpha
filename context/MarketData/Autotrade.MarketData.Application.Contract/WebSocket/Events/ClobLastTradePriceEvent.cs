using System.Globalization;

namespace Autotrade.MarketData.Application.Contract.WebSocket.Events;

/// <summary>
/// 最后成交价事件（订单撮合时推送）。
/// </summary>
public sealed record ClobLastTradePriceEvent
{
    public required string EventType { get; init; } // "last_trade_price"
    public required string AssetId { get; init; }
    public required string Market { get; init; }
    public required string Price { get; init; }
    public required string Side { get; init; } // "BUY" or "SELL"
    public required string Size { get; init; }
    public required string FeeRateBps { get; init; }
    public required string Timestamp { get; init; }

    public decimal PriceDecimal => decimal.TryParse(Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : 0m;
    public decimal SizeDecimal => decimal.TryParse(Size, NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : 0m;
    public bool IsBuy => string.Equals(Side, "BUY", StringComparison.OrdinalIgnoreCase);
}
