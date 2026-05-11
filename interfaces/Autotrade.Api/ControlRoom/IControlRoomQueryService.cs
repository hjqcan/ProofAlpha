namespace Autotrade.Api.ControlRoom;

public interface IControlRoomQueryService
{
    Task<ControlRoomSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
