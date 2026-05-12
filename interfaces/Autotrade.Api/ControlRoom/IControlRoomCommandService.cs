using Autotrade.Trading.Application.Contract.Execution;

namespace Autotrade.Api.ControlRoom;

public interface IControlRoomCommandService
{
    Task<LiveArmingStatus> GetLiveArmingStatusAsync(
        CancellationToken cancellationToken = default);

    Task<ControlRoomCommandResponse> SetStrategyStateAsync(
        string strategyId,
        SetStrategyStateRequest request,
        CancellationToken cancellationToken = default);

    Task<ArcPaperAutoTradeResponse> RequestArcPaperAutoTradeAsync(
        string strategyId,
        ArcPaperAutoTradeRequest request,
        CancellationToken cancellationToken = default);

    Task<ControlRoomCommandResponse> SetKillSwitchAsync(
        SetKillSwitchRequest request,
        CancellationToken cancellationToken = default);

    Task<IncidentActionCatalog> GetIncidentActionsAsync(
        CancellationToken cancellationToken = default);

    Task<ControlRoomCommandResponse> CancelOpenOrdersAsync(
        CancelOpenOrdersRequest request,
        CancellationToken cancellationToken = default);

    Task<IncidentPackage> ExportIncidentPackageAsync(
        IncidentPackageQuery query,
        CancellationToken cancellationToken = default);

    Task<ControlRoomCommandResponse> ArmLiveAsync(
        ArmLiveRequest request,
        CancellationToken cancellationToken = default);

    Task<ControlRoomCommandResponse> DisarmLiveAsync(
        DisarmLiveRequest request,
        CancellationToken cancellationToken = default);
}
