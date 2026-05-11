using Autotrade.Application.Services;

namespace Autotrade.Trading.Application.Contract.Accounts;

/// <summary>
/// 账户同步服务接口。
/// 负责从 Polymarket API 同步外部余额、持仓、挂单等快照，并执行对账。
/// </summary>
public interface IAccountSyncService : IApplicationService
{
    /// <summary>
    /// 同步外部余额和 Allowance。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果。</returns>
    Task<BalanceSyncResult> SyncBalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步外部持仓快照。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果（含对账信息）。</returns>
    Task<PositionsSyncResult> SyncPositionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步外部挂单快照。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果（含对账信息）。</returns>
    Task<OpenOrdersSyncResult> SyncOpenOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行完整同步（余额 + 持仓 + 挂单）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>综合同步结果。</returns>
    Task<FullSyncResult> SyncAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取最后同步时间。
    /// </summary>
    DateTimeOffset? LastSyncTime { get; }

    /// <summary>
    /// 获取最新外部余额快照（USDC）。
    /// </summary>
    ExternalBalanceSnapshot? LastBalanceSnapshot { get; }

    /// <summary>
    /// 获取最新外部持仓快照。
    /// </summary>
    IReadOnlyList<ExternalPositionSnapshot>? LastPositionsSnapshot { get; }
}

/// <summary>
/// 外部余额快照。
/// </summary>
public sealed record ExternalBalanceSnapshot(
    decimal BalanceUsdc,
    decimal AllowanceUsdc,
    DateTimeOffset SyncedAtUtc);

/// <summary>
/// 外部持仓快照。
/// </summary>
public sealed record ExternalPositionSnapshot(
    string MarketId,
    string TokenId,
    string Outcome,
    decimal Quantity,
    decimal AvgPrice,
    DateTimeOffset SyncedAtUtc);

/// <summary>
/// 余额同步结果。
/// </summary>
public sealed record BalanceSyncResult(
    bool IsSuccess,
    decimal? BalanceUsdc,
    decimal? AllowanceUsdc,
    string? ErrorMessage = null);

/// <summary>
/// 持仓同步结果。
/// </summary>
public sealed record PositionsSyncResult(
    bool IsSuccess,
    int PositionCount,
    int DriftCount,
    IReadOnlyList<PositionDrift>? Drifts = null,
    string? ErrorMessage = null);

/// <summary>
/// 持仓漂移详情。
/// </summary>
public sealed record PositionDrift(
    string MarketId,
    string Outcome,
    decimal InternalQuantity,
    decimal ExternalQuantity,
    decimal QuantityDiff,
    decimal? InternalAvgCost,
    decimal? ExternalAvgCost,
    decimal? AvgCostDiff,
    string DriftType);

/// <summary>
/// 挂单同步结果。
/// </summary>
public sealed record OpenOrdersSyncResult(
    bool IsSuccess,
    int OpenOrderCount,
    int UnknownExternalOrderCount,
    int MissingInternalOrderCount,
    string? ErrorMessage = null);

/// <summary>
/// 完整同步结果。
/// </summary>
public sealed record FullSyncResult(
    bool IsSuccess,
    BalanceSyncResult? Balance,
    PositionsSyncResult? Positions,
    OpenOrdersSyncResult? OpenOrders,
    string? ErrorMessage = null)
{
    /// <summary>
    /// 是否存在任何漂移或异常。
    /// </summary>
    public bool HasDrift =>
        (Positions?.DriftCount ?? 0) > 0 ||
        (OpenOrders?.UnknownExternalOrderCount ?? 0) > 0 ||
        (OpenOrders?.MissingInternalOrderCount ?? 0) > 0;
}
