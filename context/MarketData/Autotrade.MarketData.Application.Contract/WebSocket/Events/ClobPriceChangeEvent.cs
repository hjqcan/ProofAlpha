using System.Globalization;

namespace Autotrade.MarketData.Application.Contract.WebSocket.Events;

/// <summary>
/// 价格变动事件（订单挂单/撤单时推送）。
/// </summary>
public sealed record ClobPriceChangeEvent
{
    public required string EventType { get; init; } // "price_change"
    public required string Market { get; init; }
    public required string Timestamp { get; init; }
    public required IReadOnlyList<PriceChange> PriceChanges { get; init; }
}

/// <summary>
/// 单个价格变动条目。
/// </summary>
public sealed record PriceChange
{
    public required string AssetId { get; init; }
    public required string Price { get; init; }
    public required string Size { get; init; }
    public required string Side { get; init; } // "BUY" or "SELL"
    public required string Hash { get; init; }
    public required string BestBid { get; init; }
    public required string BestAsk { get; init; }

    public decimal PriceDecimal => decimal.TryParse(Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : 0m;
    public decimal SizeDecimal => decimal.TryParse(Size, NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : 0m;
    public decimal BestBidDecimal => decimal.TryParse(BestBid, NumberStyles.Number, CultureInfo.InvariantCulture, out var b) ? b : 0m;
    public decimal BestAskDecimal => decimal.TryParse(BestAsk, NumberStyles.Number, CultureInfo.InvariantCulture, out var a) ? a : 0m;
    public bool IsBuy => string.Equals(Side, "BUY", StringComparison.OrdinalIgnoreCase);
}
