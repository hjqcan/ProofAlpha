using System.Globalization;

namespace Autotrade.MarketData.Application.Contract.WebSocket.Events;

/// <summary>
/// 最小价格变动单位变更事件。
/// </summary>
public sealed record ClobTickSizeChangeEvent
{
    public required string EventType { get; init; } // "tick_size_change"
    public required string AssetId { get; init; }
    public required string Market { get; init; }
    public required string OldTickSize { get; init; }
    public required string NewTickSize { get; init; }
    public string? Side { get; init; }
    public required string Timestamp { get; init; }

    public decimal OldTickSizeDecimal => decimal.TryParse(OldTickSize, NumberStyles.Number, CultureInfo.InvariantCulture, out var o) ? o : 0m;
    public decimal NewTickSizeDecimal => decimal.TryParse(NewTickSize, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : 0m;
}
