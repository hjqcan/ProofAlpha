using Autotrade.Api.ControlRoom;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.AspNetCore.Mvc;

namespace Autotrade.Api.Controllers;

[ApiController]
[Route("api/control-room")]
public sealed class ControlRoomController(
    IControlRoomQueryService queryService,
    IControlRoomMarketDataService marketDataService,
    IControlRoomCommandService commandService) : ControllerBase
{
    [HttpGet("snapshot")]
    public async Task<ActionResult<ControlRoomSnapshotResponse>> GetSnapshot(CancellationToken cancellationToken)
    {
        var snapshot = await queryService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return Ok(snapshot);
    }

    [HttpGet("markets")]
    public async Task<ActionResult<ControlRoomMarketsResponse>> GetMarkets(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? status,
        [FromQuery] string? sort,
        [FromQuery] decimal? minLiquidity,
        [FromQuery] decimal? minVolume24h,
        [FromQuery] int? maxDaysToExpiry,
        [FromQuery] bool? acceptingOrders,
        [FromQuery] decimal? minSignalScore,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        var query = new ControlRoomMarketDiscoveryQuery(
            search,
            category,
            status,
            sort,
            minLiquidity,
            minVolume24h,
            maxDaysToExpiry,
            acceptingOrders,
            minSignalScore,
            limit,
            offset);

        var response = await marketDataService
            .GetMarketsAsync(query, cancellationToken)
            .ConfigureAwait(false);

        return Ok(response);
    }

    [HttpGet("markets/{marketId}")]
    public async Task<ActionResult<ControlRoomMarketDetailResponse>> GetMarketDetail(
        string marketId,
        [FromQuery] int? levels,
        CancellationToken cancellationToken)
    {
        var response = await marketDataService
            .GetMarketDetailAsync(marketId, levels, cancellationToken)
            .ConfigureAwait(false);

        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("markets/{marketId}/order-book")]
    public async Task<ActionResult<ControlRoomOrderBookDto>> GetOrderBook(
        string marketId,
        [FromQuery] string? tokenId,
        [FromQuery] string? outcome,
        [FromQuery] int? levels,
        CancellationToken cancellationToken)
    {
        var response = await marketDataService
            .GetOrderBookAsync(marketId, tokenId, outcome, levels, cancellationToken)
            .ConfigureAwait(false);

        return response is null ? NotFound() : Ok(response);
    }

    [HttpPost("strategies/{strategyId}/state")]
    public async Task<ActionResult<ControlRoomCommandResponse>> SetStrategyState(
        string strategyId,
        [FromBody] SetStrategyStateRequest request,
        CancellationToken cancellationToken)
    {
        var response = await commandService
            .SetStrategyStateAsync(strategyId, request, cancellationToken)
            .ConfigureAwait(false);

        return Accepted(response);
    }

    [HttpPost("risk/kill-switch")]
    public async Task<ActionResult<ControlRoomCommandResponse>> SetKillSwitch(
        [FromBody] SetKillSwitchRequest request,
        CancellationToken cancellationToken)
    {
        var response = await commandService
            .SetKillSwitchAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return Accepted(response);
    }

    [HttpGet("incidents/actions")]
    public async Task<ActionResult<IncidentActionCatalog>> GetIncidentActions(CancellationToken cancellationToken)
    {
        var response = await commandService
            .GetIncidentActionsAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(response);
    }

    [HttpPost("incidents/cancel-open-orders")]
    public async Task<ActionResult<ControlRoomCommandResponse>> CancelOpenOrders(
        [FromBody] CancelOpenOrdersRequest request,
        CancellationToken cancellationToken)
    {
        var response = await commandService
            .CancelOpenOrdersAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return Accepted(response);
    }

    [HttpGet("incidents/package")]
    public async Task<ActionResult<IncidentPackage>> ExportIncidentPackage(
        [FromQuery] string? riskEventId,
        [FromQuery] string? strategyId,
        [FromQuery] string? marketId,
        [FromQuery] string? orderId,
        [FromQuery] string? correlationId,
        CancellationToken cancellationToken)
    {
        var response = await commandService
            .ExportIncidentPackageAsync(
                new IncidentPackageQuery(riskEventId, strategyId, marketId, orderId, correlationId),
                cancellationToken)
            .ConfigureAwait(false);

        return Ok(response);
    }

    [HttpGet("live/arming")]
    public async Task<ActionResult<LiveArmingStatus>> GetLiveArmingStatus(CancellationToken cancellationToken)
    {
        var response = await commandService
            .GetLiveArmingStatusAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(response);
    }

    [HttpPost("live/arm")]
    public async Task<ActionResult<ControlRoomCommandResponse>> ArmLive(
        [FromBody] ArmLiveRequest request,
        CancellationToken cancellationToken)
    {
        var response = await commandService
            .ArmLiveAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return Accepted(response);
    }

    [HttpPost("live/disarm")]
    public async Task<ActionResult<ControlRoomCommandResponse>> DisarmLive(
        [FromBody] DisarmLiveRequest request,
        CancellationToken cancellationToken)
    {
        var response = await commandService
            .DisarmLiveAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return Accepted(response);
    }
}
