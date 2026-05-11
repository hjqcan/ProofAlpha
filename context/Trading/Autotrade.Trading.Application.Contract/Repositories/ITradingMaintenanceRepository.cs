namespace Autotrade.Trading.Application.Contract.Repositories;

/// <summary>
/// Trading 数据维护仓储接口。
/// </summary>
public interface ITradingMaintenanceRepository
{
    /// <summary>
    /// 清理过期数据。
    /// </summary>
    /// <param name="ordersCutoffUtc">订单截止时间（早于此时间的订单将被删除）。</param>
    /// <param name="tradesCutoffUtc">成交截止时间（早于此时间的成交将被删除）。</param>
    /// <param name="eventsCutoffUtc">订单事件截止时间（早于此时间的事件将被删除）。</param>
    /// <param name="riskEventsCutoffUtc">风控事件截止时间（早于此时间的事件将被删除）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除的总记录数。</returns>
    Task<int> CleanupAsync(
        DateTimeOffset? ordersCutoffUtc,
        DateTimeOffset? tradesCutoffUtc,
        DateTimeOffset? eventsCutoffUtc,
        DateTimeOffset? riskEventsCutoffUtc,
        CancellationToken cancellationToken = default);
}
