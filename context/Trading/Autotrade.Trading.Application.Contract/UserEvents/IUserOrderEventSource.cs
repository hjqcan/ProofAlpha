namespace Autotrade.Trading.Application.Contract.UserEvents;

/// <summary>
/// Low-latency user order/trade event source. REST reconciliation remains authoritative.
/// </summary>
public interface IUserOrderEventSource : IAsyncDisposable
{
    bool IsConnected { get; }

    IReadOnlyCollection<string> SubscribedMarkets { get; }

    IDisposable OnOrder(Func<UserOrderEvent, CancellationToken, Task> callback);

    IDisposable OnTrade(Func<UserTradeEvent, CancellationToken, Task> callback);

    Task SubscribeMarketsAsync(IEnumerable<string> marketIds, CancellationToken cancellationToken = default);

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public sealed record UserOrderEvent
{
    public required string ExchangeOrderId { get; init; }

    public string? MarketId { get; init; }

    public string? TokenId { get; init; }

    public string? Side { get; init; }

    public string? Outcome { get; init; }

    public string? Status { get; init; }

    public string? Type { get; init; }

    public string? OriginalSize { get; init; }

    public string? SizeMatched { get; init; }

    public string? Price { get; init; }

    public IReadOnlyList<string> AssociateTrades { get; init; } = Array.Empty<string>();

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record UserTradeEvent
{
    public required string ExchangeTradeId { get; init; }

    public string? ExchangeOrderId { get; init; }

    public string? MarketId { get; init; }

    public string? TokenId { get; init; }

    public string? Side { get; init; }

    public string? Outcome { get; init; }

    public string? Status { get; init; }

    public string? Type { get; init; }

    public string? Price { get; init; }

    public string? Size { get; init; }

    public string? FeeRateBps { get; init; }

    public IReadOnlyList<UserTradeMakerOrder> MakerOrders { get; init; } = Array.Empty<UserTradeMakerOrder>();

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record UserTradeMakerOrder
{
    public string? ExchangeOrderId { get; init; }

    public string? Owner { get; init; }

    public string? AssetId { get; init; }

    public string? Outcome { get; init; }

    public string? Side { get; init; }

    public string? MatchedAmount { get; init; }
}
