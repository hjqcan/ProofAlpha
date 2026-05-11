namespace Autotrade.Api.ControlRoom;

public interface IControlRoomMarketDataService
{
    Task<ControlRoomMarketsResponse> GetMarketsAsync(
        ControlRoomMarketDiscoveryQuery query,
        CancellationToken cancellationToken = default);

    Task<ControlRoomMarketDetailResponse?> GetMarketDetailAsync(
        string marketId,
        int? levels,
        CancellationToken cancellationToken = default);

    Task<ControlRoomOrderBookDto?> GetOrderBookAsync(
        string marketId,
        string? tokenId,
        string? outcome,
        int? levels,
        CancellationToken cancellationToken = default);
}
