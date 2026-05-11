using Autotrade.Trading.Application.Contract.Accounts;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Application.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Risk;

/// <summary>
/// 有效风控资金提供者。
/// 计算 effective capital = min(RiskLimit, ExternalBalance, ExternalAllowance)。
/// 确保 RiskManager 的资金上限不超过实际可用资金。
/// </summary>
public sealed class EffectiveRiskCapitalProvider : IRiskCapitalProvider
{
    private readonly RiskCapitalOptions _riskOptions;
    private readonly ExternalAccountSnapshotStore _snapshotStore;
    private readonly ExecutionOptions _executionOptions;
    private readonly ILogger<EffectiveRiskCapitalProvider> _logger;

    public EffectiveRiskCapitalProvider(
        IOptions<RiskCapitalOptions> riskOptions,
        ExternalAccountSnapshotStore snapshotStore,
        IOptions<ExecutionOptions> executionOptions,
        ILogger<EffectiveRiskCapitalProvider> logger)
    {
        _riskOptions = riskOptions?.Value ?? throw new ArgumentNullException(nameof(riskOptions));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _executionOptions = executionOptions?.Value ?? throw new ArgumentNullException(nameof(executionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 获取有效风控资金快照。
    /// Live 模式：min(RiskLimit, ExternalBalance, ExternalAllowance)
    /// Paper 模式：直接返回配置的风控上限
    /// </summary>
    public RiskCapitalSnapshot GetSnapshot()
    {
        // Paper 模式直接使用配置值
        if (_executionOptions.Mode == ExecutionMode.Paper)
        {
            return new RiskCapitalSnapshot(
                _riskOptions.TotalCapital,
                _riskOptions.AvailableCapital,
                _riskOptions.RealizedDailyPnl);
        }

        // Live 模式计算 effective capital
        var externalSnapshot = _snapshotStore.BalanceSnapshot;

        if (externalSnapshot is null)
        {
            // 外部快照尚未同步：不应进入交易运行态（启动阶段应 fail-fast）
            // 这里仍返回配置值以保持风控校验逻辑可运行，并输出明显告警。
            _logger.LogError("外部余额快照缺失：Live 模式下 effective capital 无法计算，将回退到 Risk:Capital（应在启动阶段 fail-fast）");
            return new RiskCapitalSnapshot(
                _riskOptions.TotalCapital,
                _riskOptions.AvailableCapital,
                _riskOptions.RealizedDailyPnl);
        }

        // 计算 effective capital = min(RiskLimit, ExternalBalance, ExternalAllowance)
        var effectiveTotalCapital = Math.Min(
            _riskOptions.TotalCapital,
            Math.Min(externalSnapshot.BalanceUsdc, externalSnapshot.AllowanceUsdc));

        // 可用资金不能超过 effective total capital
        var effectiveAvailableCapital = Math.Min(
            _riskOptions.AvailableCapital,
            effectiveTotalCapital);

        // 检查是否存在限制
        if (effectiveTotalCapital < _riskOptions.TotalCapital)
        {
            _logger.LogWarning(
                "有效资金受限: RiskLimit={RiskLimit}, Balance={Balance}, Allowance={Allowance}, Effective={Effective}",
                _riskOptions.TotalCapital,
                externalSnapshot.BalanceUsdc,
                externalSnapshot.AllowanceUsdc,
                effectiveTotalCapital);
        }

        return new RiskCapitalSnapshot(
            effectiveTotalCapital,
            effectiveAvailableCapital,
            _riskOptions.RealizedDailyPnl);
    }
}
