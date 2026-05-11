using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Application.Compliance;

public sealed class ComplianceGuard : IComplianceGuard
{
    private readonly ComplianceOptions _complianceOptions;
    private readonly ExecutionOptions _executionOptions;
    private readonly RiskOptions _riskOptions;
    private readonly ComplianceStrategyEngineOptions _strategyOptions;

    public ComplianceGuard(
        IOptions<ComplianceOptions> complianceOptions,
        IOptions<ExecutionOptions> executionOptions,
        IOptions<RiskOptions> riskOptions,
        IOptions<ComplianceStrategyEngineOptions> strategyOptions)
    {
        _complianceOptions = complianceOptions?.Value ?? throw new ArgumentNullException(nameof(complianceOptions));
        _executionOptions = executionOptions?.Value ?? throw new ArgumentNullException(nameof(executionOptions));
        _riskOptions = riskOptions?.Value ?? throw new ArgumentNullException(nameof(riskOptions));
        _strategyOptions = strategyOptions?.Value ?? throw new ArgumentNullException(nameof(strategyOptions));
    }

    public ComplianceCheckResult CheckConfiguration(ExecutionMode executionMode)
    {
        if (!_complianceOptions.Enabled)
        {
            return ComplianceCheckResult.Disabled;
        }

        var issues = new List<ComplianceIssue>();
        var isLive = executionMode == ExecutionMode.Live;

        if (!_complianceOptions.GeoKycAllowed)
        {
            issues.Add(new ComplianceIssue(
                "COMPLIANCE_GEO_KYC_UNCONFIRMED",
                isLive ? ComplianceSeverity.Error : ComplianceSeverity.Warning,
                "Compliance.GeoKycAllowed must be explicitly true before Live order placement.",
                BlocksLiveOrders: true));
        }

        AddUnsafeParameterIssueIfNeeded(
            issues,
            isLive,
            _strategyOptions.EvaluationIntervalSeconds < _complianceOptions.MinLiveEvaluationIntervalSeconds,
            "COMPLIANCE_EVALUATION_INTERVAL",
            $"StrategyEngine.EvaluationIntervalSeconds={_strategyOptions.EvaluationIntervalSeconds} is below Compliance.MinLiveEvaluationIntervalSeconds={_complianceOptions.MinLiveEvaluationIntervalSeconds}.");

        AddUnsafeParameterIssueIfNeeded(
            issues,
            isLive,
            _strategyOptions.MaxOrdersPerCycle > _complianceOptions.MaxLiveOrdersPerCycle,
            "COMPLIANCE_MAX_ORDERS_PER_CYCLE",
            $"StrategyEngine.MaxOrdersPerCycle={_strategyOptions.MaxOrdersPerCycle} exceeds Compliance.MaxLiveOrdersPerCycle={_complianceOptions.MaxLiveOrdersPerCycle}.");

        AddUnsafeParameterIssueIfNeeded(
            issues,
            isLive,
            _executionOptions.MaxOpenOrdersPerMarket > _complianceOptions.MaxLiveOpenOrdersPerMarket,
            "COMPLIANCE_MAX_OPEN_ORDERS_PER_MARKET",
            $"Execution.MaxOpenOrdersPerMarket={_executionOptions.MaxOpenOrdersPerMarket} exceeds Compliance.MaxLiveOpenOrdersPerMarket={_complianceOptions.MaxLiveOpenOrdersPerMarket}.");

        AddUnsafeParameterIssueIfNeeded(
            issues,
            isLive,
            _executionOptions.ReconciliationIntervalSeconds < _complianceOptions.MinLiveReconciliationIntervalSeconds,
            "COMPLIANCE_RECONCILIATION_INTERVAL",
            $"Execution.ReconciliationIntervalSeconds={_executionOptions.ReconciliationIntervalSeconds} is below Compliance.MinLiveReconciliationIntervalSeconds={_complianceOptions.MinLiveReconciliationIntervalSeconds}.");

        AddUnsafeParameterIssueIfNeeded(
            issues,
            isLive,
            _riskOptions.MaxOpenOrders > _complianceOptions.MaxLiveOpenOrders,
            "COMPLIANCE_MAX_OPEN_ORDERS",
            $"Risk.MaxOpenOrders={_riskOptions.MaxOpenOrders} exceeds Compliance.MaxLiveOpenOrders={_complianceOptions.MaxLiveOpenOrders}.");

        AddUnsafeParameterIssueIfNeeded(
            issues,
            isLive,
            _riskOptions.MaxCapitalPerMarket > _complianceOptions.MaxLiveCapitalPerMarket,
            "COMPLIANCE_MAX_CAPITAL_PER_MARKET",
            $"Risk.MaxCapitalPerMarket={_riskOptions.MaxCapitalPerMarket:P2} exceeds Compliance.MaxLiveCapitalPerMarket={_complianceOptions.MaxLiveCapitalPerMarket:P2}.");

        AddUnsafeParameterIssueIfNeeded(
            issues,
            isLive,
            _riskOptions.MaxCapitalPerStrategy > _complianceOptions.MaxLiveCapitalPerStrategy,
            "COMPLIANCE_MAX_CAPITAL_PER_STRATEGY",
            $"Risk.MaxCapitalPerStrategy={_riskOptions.MaxCapitalPerStrategy:P2} exceeds Compliance.MaxLiveCapitalPerStrategy={_complianceOptions.MaxLiveCapitalPerStrategy:P2}.");

        AddUnsafeParameterIssueIfNeeded(
            issues,
            isLive,
            _riskOptions.MaxTotalCapitalUtilization > _complianceOptions.MaxLiveTotalCapitalUtilization,
            "COMPLIANCE_MAX_TOTAL_CAPITAL_UTILIZATION",
            $"Risk.MaxTotalCapitalUtilization={_riskOptions.MaxTotalCapitalUtilization:P2} exceeds Compliance.MaxLiveTotalCapitalUtilization={_complianceOptions.MaxLiveTotalCapitalUtilization:P2}.");

        var blocks = isLive && issues.Any(issue =>
            issue.BlocksLiveOrders &&
            issue.Severity == ComplianceSeverity.Error);

        return new ComplianceCheckResult(
            true,
            !issues.Any(issue => issue.Severity == ComplianceSeverity.Error),
            blocks,
            issues);
    }

    public ComplianceCheckResult CheckOrderPlacement(ExecutionMode executionMode)
    {
        var result = CheckConfiguration(executionMode);
        if (executionMode != ExecutionMode.Live)
        {
            return result with { BlocksOrders = false };
        }

        return result;
    }

    private void AddUnsafeParameterIssueIfNeeded(
        ICollection<ComplianceIssue> issues,
        bool isLive,
        bool condition,
        string code,
        string message)
    {
        if (!condition)
        {
            return;
        }

        issues.Add(new ComplianceIssue(
            code,
            !isLive || _complianceOptions.AllowUnsafeLiveParameters ? ComplianceSeverity.Warning : ComplianceSeverity.Error,
            message,
            BlocksLiveOrders: isLive && !_complianceOptions.AllowUnsafeLiveParameters));
    }
}
