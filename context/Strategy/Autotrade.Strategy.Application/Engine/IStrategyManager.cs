// ============================================================================
// 策略管理器接口
// ============================================================================

using Autotrade.Strategy.Application.Contract.Strategies;

namespace Autotrade.Strategy.Application.Engine;

/// <summary>
/// 策略管理器接口。
/// 定义策略生命周期管理方法。
/// </summary>
public interface IStrategyManager
{
    /// <summary>
    /// 获取所有已注册策略的描述信息。
    /// </summary>
    IReadOnlyList<StrategyDescriptor> GetRegisteredStrategies();

    /// <summary>
    /// 获取所有策略的运行时状态。
    /// </summary>
    Task<IReadOnlyList<StrategyStatus>> GetStatusesAsync(CancellationToken cancellationToken = default);

    StrategyState GetDesiredState(string strategyId);

    Task SetDesiredStateAsync(string strategyId, StrategyState desiredState, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动指定策略。
    /// </summary>
    Task StartAsync(string strategyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 暂停指定策略。
    /// </summary>
    Task PauseAsync(string strategyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复指定策略。
    /// </summary>
    Task ResumeAsync(string strategyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止指定策略。
    /// </summary>
    Task StopAsync(string strategyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重新加载策略配置。
    /// 安全地暂停、替换实例并恢复。
    /// </summary>
    Task ReloadConfigAsync(string strategyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 策略描述符。
/// </summary>
/// <param name="StrategyId">策略 ID。</param>
/// <param name="Name">策略名称。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="ConfigVersion">配置版本。</param>
/// <param name="OptionsSectionName">配置节名称。</param>
public sealed record StrategyDescriptor(
    string StrategyId,
    string Name,
    bool Enabled,
    string ConfigVersion,
    string OptionsSectionName);

/// <summary>
/// 策略运行时状态。
/// </summary>
public sealed record StrategyStatus(
    string StrategyId,
    string Name,
    StrategyState State,
    bool Enabled,
    string ConfigVersion,
    int RestartCount,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastDecisionAtUtc,
    DateTimeOffset? LastHeartbeatUtc,
    string? LastError,
    IReadOnlyList<string>? ActiveMarkets = null,
    long CycleCount = 0,
    long SnapshotsProcessed = 0,
    int ChannelBacklog = 0,
    bool IsKillSwitchBlocked = false,
    StrategyState? DesiredState = null,
    StrategyBlockedReason? BlockedReason = null);
