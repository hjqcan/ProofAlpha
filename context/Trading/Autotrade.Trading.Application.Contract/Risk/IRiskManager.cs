using Autotrade.Application.Services;
using Autotrade.Trading.Domain.Shared.Enums;

namespace Autotrade.Trading.Application.Contract.Risk;

/// <summary>
/// Risk manager interface.
/// </summary>
public interface IRiskManager : IApplicationService
{
    /// <summary>
    /// 验证订单是否符合风控规则。
    /// </summary>
    Task<RiskCheckResult> ValidateOrderAsync(RiskOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录订单已被接受。
    /// </summary>
    Task RecordOrderAcceptedAsync(RiskOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录订单状态更新，并执行 post-trade 风险再评估。
    /// </summary>
    Task RecordOrderUpdateAsync(RiskOrderUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录订单错误。
    /// </summary>
    Task RecordOrderErrorAsync(string strategyId, string clientOrderId, string errorCode, string message,
        CancellationToken cancellationToken = default);

    #region Kill Switch

    /// <summary>
    /// 激活全局 Kill Switch（简化版，使用默认级别）。
    /// </summary>
    /// <param name="reason">原因描述。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ActivateKillSwitchAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// 激活全局 Kill Switch。
    /// </summary>
    /// <param name="level">Kill switch 级别。</param>
    /// <param name="reasonCode">原因代码。</param>
    /// <param name="reason">原因描述。</param>
    /// <param name="contextJson">上下文 JSON（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ActivateKillSwitchAsync(
        KillSwitchLevel level,
        string reasonCode,
        string reason,
        string? contextJson = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 激活策略级别 Kill Switch。
    /// </summary>
    /// <param name="strategyId">策略 ID。</param>
    /// <param name="level">Kill switch 级别。</param>
    /// <param name="reasonCode">原因代码。</param>
    /// <param name="reason">原因描述。</param>
    /// <param name="marketId">关联市场 ID（可选）。</param>
    /// <param name="contextJson">上下文 JSON（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ActivateStrategyKillSwitchAsync(
        string strategyId,
        KillSwitchLevel level,
        string reasonCode,
        string reason,
        string? marketId = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置 Kill Switch 状态（需手动确认后调用）。
    /// </summary>
    /// <param name="strategyId">策略 ID（为 null 表示重置全局）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ResetKillSwitchAsync(string? strategyId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 全局 Kill Switch 是否激活。
    /// </summary>
    bool IsKillSwitchActive { get; }

    /// <summary>
    /// 获取全局 Kill Switch 状态。
    /// </summary>
    KillSwitchState GetKillSwitchState();

    /// <summary>
    /// 获取指定策略的 Kill Switch 状态。
    /// </summary>
    /// <param name="strategyId">策略 ID。</param>
    KillSwitchState GetStrategyKillSwitchState(string strategyId);

    /// <summary>
    /// 检查指定策略是否被 Kill Switch 阻止（包括全局和策略级别）。
    /// </summary>
    /// <param name="strategyId">策略 ID。</param>
    bool IsStrategyBlocked(string strategyId);

    /// <summary>
    /// 获取所有活跃的 Kill Switch 状态。
    /// </summary>
    IReadOnlyList<KillSwitchState> GetAllActiveKillSwitches();

    #endregion

    /// <summary>
    /// 获取当前所有未完成订单 ID。
    /// </summary>
    IReadOnlyList<string> GetOpenOrderIds();

    /// <summary>
    /// 获取指定策略的未完成订单 ID。
    /// </summary>
    /// <param name="strategyId">策略 ID。</param>
    IReadOnlyList<string> GetOpenOrderIds(string strategyId);

    /// <summary>
    /// 获取超时的未对冲敞口。
    /// </summary>
    IReadOnlyList<UnhedgedExposureSnapshot> GetExpiredUnhedgedExposures(DateTimeOffset nowUtc);

    /// <summary>
    /// 记录未对冲敞口（包含完整的订单信息用于退出/对冲）。
    /// </summary>
    /// <param name="strategyId">策略 ID</param>
    /// <param name="marketId">市场 ID</param>
    /// <param name="tokenId">第一腿的 Token ID</param>
    /// <param name="hedgeTokenId">对冲腿的 Token ID（用于 ForceHedge）</param>
    /// <param name="outcome">第一腿的 Outcome</param>
    /// <param name="side">第一腿的方向</param>
    /// <param name="quantity">第一腿的数量</param>
    /// <param name="price">第一腿的价格</param>
    /// <param name="startedAtUtc">敞口开始时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RecordUnhedgedExposureAsync(
        string strategyId,
        string marketId,
        string tokenId,
        string hedgeTokenId,
        OutcomeSide outcome,
        OrderSide side,
        decimal quantity,
        decimal price,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除未对冲敞口（对冲完成时调用）。
    /// </summary>
    Task ClearUnhedgedExposureAsync(string strategyId, string marketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前风控状态快照（用于指标和监控）。
    /// </summary>
    RiskStateSnapshot GetStateSnapshot();
}

/// <summary>
/// Snapshot of an unhedged exposure with full order details for exit/hedge actions.
/// </summary>
/// <param name="StrategyId">策略 ID</param>
/// <param name="MarketId">市场 ID (Condition ID)</param>
/// <param name="TokenId">第一腿的 Token ID</param>
/// <param name="HedgeTokenId">对冲腿的 Token ID（用于 ForceHedge）</param>
/// <param name="Outcome">第一腿的 Outcome (Yes/No)</param>
/// <param name="Side">第一腿的方向 (Buy/Sell)</param>
/// <param name="Quantity">第一腿的数量</param>
/// <param name="Price">第一腿的价格</param>
/// <param name="Notional">名义金额 (Quantity * Price)</param>
/// <param name="StartedAtUtc">敞口开始时间</param>
public sealed record UnhedgedExposureSnapshot(
    string StrategyId,
    string MarketId,
    string TokenId,
    string HedgeTokenId,
    OutcomeSide Outcome,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    decimal Notional,
    DateTimeOffset StartedAtUtc);

/// <summary>
/// 风控状态快照（用于指标和监控）。
/// </summary>
public sealed record RiskStateSnapshot(
    decimal TotalOpenNotional,
    int TotalOpenOrders,
    decimal TotalCapital,
    decimal AvailableCapital,
    decimal CapitalUtilizationPct,
    IReadOnlyDictionary<string, decimal> NotionalByStrategy,
    IReadOnlyDictionary<string, decimal> NotionalByMarket,
    IReadOnlyDictionary<string, int> OpenOrdersByStrategy,
    IReadOnlyList<UnhedgedExposureSnapshot> UnhedgedExposures);
