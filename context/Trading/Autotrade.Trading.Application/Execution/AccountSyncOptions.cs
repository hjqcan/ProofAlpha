namespace Autotrade.Trading.Application.Execution;

/// <summary>
/// 账户同步配置选项。
/// </summary>
public sealed class AccountSyncOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "AccountSync";

    /// <summary>
    /// 是否启用自动同步。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 同步间隔（秒）。
    /// </summary>
    public int SyncIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// 启动时是否同步。
    /// </summary>
    public bool SyncOnStartup { get; set; } = true;

    /// <summary>
    /// 是否检测外部挂单。
    /// </summary>
    public bool DetectExternalOpenOrders { get; set; } = true;

    /// <summary>
    /// 持仓数量漂移容差（用于对账）。
    /// </summary>
    public decimal QuantityDriftTolerance { get; set; } = 0.0001m;

    /// <summary>
    /// 平均成本漂移容差（用于对账）。
    /// </summary>
    public decimal AverageCostDriftTolerance { get; set; } = 0.0001m;

    /// <summary>
    /// 发现漂移时是否触发 Kill Switch（仅 Live 模式）。
    /// </summary>
    public bool TriggerKillSwitchOnDrift { get; set; } = true;

    /// <summary>
    /// 启动阶段发现漂移时是否 fail-fast（Live 模式安全策略）。
    /// </summary>
    public bool FailFastOnStartupDrift { get; set; } = true;

    /// <summary>
    /// 同步失败最大重试次数（启动阶段）。
    /// </summary>
    public int StartupMaxRetries { get; set; } = 3;

    /// <summary>
    /// 同步失败重试延迟（毫秒）。
    /// </summary>
    public int StartupRetryDelayMs { get; set; } = 1000;
}
