using Autotrade.Trading.Application.Compliance;
using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Autotrade.Trading.Application.Contract.Risk;
using Microsoft.Extensions.Options;

namespace Autotrade.Trading.Tests.Compliance;

public sealed class ComplianceGuardTests
{
    [Fact]
    public void CheckOrderPlacement_LiveWithoutGeoKyc_BlocksOrders()
    {
        var guard = CreateGuard(new ComplianceOptions { GeoKycAllowed = false });

        var result = guard.CheckOrderPlacement(ExecutionMode.Live);

        Assert.True(result.BlocksOrders);
        Assert.Contains(result.Issues, issue => issue.Code == "COMPLIANCE_GEO_KYC_UNCONFIRMED");
    }

    [Fact]
    public void CheckOrderPlacement_PaperWithoutGeoKyc_WarnsButDoesNotBlock()
    {
        var guard = CreateGuard(new ComplianceOptions { GeoKycAllowed = false });

        var result = guard.CheckOrderPlacement(ExecutionMode.Paper);

        Assert.False(result.BlocksOrders);
        Assert.True(result.IsCompliant);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "COMPLIANCE_GEO_KYC_UNCONFIRMED" &&
            issue.Severity == ComplianceSeverity.Warning);
    }

    [Fact]
    public void CheckOrderPlacement_LiveUnsafeRiskWithoutOverride_BlocksOrders()
    {
        var guard = CreateGuard(
            new ComplianceOptions
            {
                GeoKycAllowed = true,
                MaxLiveTotalCapitalUtilization = 0.50m,
                AllowUnsafeLiveParameters = false
            },
            riskOptions: new RiskOptions
            {
                MaxTotalCapitalUtilization = 0.75m,
                MaxCapitalPerStrategy = 0.30m,
                MaxCapitalPerMarket = 0.05m,
                MaxUnhedgedCapitalPerMarket = 0.02m,
                CapitalUtilizationWarningThreshold = 0.40m
            });

        var result = guard.CheckOrderPlacement(ExecutionMode.Live);

        Assert.True(result.BlocksOrders);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "COMPLIANCE_MAX_TOTAL_CAPITAL_UTILIZATION" &&
            issue.Severity == ComplianceSeverity.Error);
    }

    [Fact]
    public void CheckOrderPlacement_LiveUnsafeRiskWithOverride_WarnsButDoesNotBlock()
    {
        var guard = CreateGuard(
            new ComplianceOptions
            {
                GeoKycAllowed = true,
                MaxLiveTotalCapitalUtilization = 0.50m,
                AllowUnsafeLiveParameters = true
            },
            riskOptions: new RiskOptions
            {
                MaxTotalCapitalUtilization = 0.75m,
                MaxCapitalPerStrategy = 0.30m,
                MaxCapitalPerMarket = 0.05m,
                MaxUnhedgedCapitalPerMarket = 0.02m,
                CapitalUtilizationWarningThreshold = 0.40m
            });

        var result = guard.CheckOrderPlacement(ExecutionMode.Live);

        Assert.False(result.BlocksOrders);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "COMPLIANCE_MAX_TOTAL_CAPITAL_UTILIZATION" &&
            issue.Severity == ComplianceSeverity.Warning);
    }

    [Fact]
    public void CheckOrderPlacement_LiveUnsafeStrategyEngineWithoutOverride_BlocksOrders()
    {
        var guard = CreateGuard(
            new ComplianceOptions
            {
                GeoKycAllowed = true,
                MinLiveEvaluationIntervalSeconds = 5,
                MaxLiveOrdersPerCycle = 2
            },
            strategyOptions: new ComplianceStrategyEngineOptions
            {
                EvaluationIntervalSeconds = 1,
                MaxOrdersPerCycle = 4
            });

        var result = guard.CheckOrderPlacement(ExecutionMode.Live);

        Assert.True(result.BlocksOrders);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "COMPLIANCE_EVALUATION_INTERVAL" &&
            issue.Severity == ComplianceSeverity.Error);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "COMPLIANCE_MAX_ORDERS_PER_CYCLE" &&
            issue.Severity == ComplianceSeverity.Error);
    }

    [Fact]
    public void CheckOrderPlacement_PaperUnsafeStrategyEngine_WarnsButDoesNotBlock()
    {
        var guard = CreateGuard(
            new ComplianceOptions
            {
                GeoKycAllowed = true,
                MinLiveEvaluationIntervalSeconds = 5,
                MaxLiveOrdersPerCycle = 2
            },
            strategyOptions: new ComplianceStrategyEngineOptions
            {
                EvaluationIntervalSeconds = 1,
                MaxOrdersPerCycle = 4
            });

        var result = guard.CheckOrderPlacement(ExecutionMode.Paper);

        Assert.False(result.BlocksOrders);
        Assert.True(result.IsCompliant);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "COMPLIANCE_EVALUATION_INTERVAL" &&
            issue.Severity == ComplianceSeverity.Warning);
    }

    [Fact]
    public void CheckOrderPlacement_LiveUnsafeStrategyEngineWithOverride_WarnsButDoesNotBlock()
    {
        var guard = CreateGuard(
            new ComplianceOptions
            {
                GeoKycAllowed = true,
                AllowUnsafeLiveParameters = true,
                MinLiveEvaluationIntervalSeconds = 5,
                MaxLiveOrdersPerCycle = 2
            },
            strategyOptions: new ComplianceStrategyEngineOptions
            {
                EvaluationIntervalSeconds = 1,
                MaxOrdersPerCycle = 4
            });

        var result = guard.CheckOrderPlacement(ExecutionMode.Live);

        Assert.False(result.BlocksOrders);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "COMPLIANCE_EVALUATION_INTERVAL" &&
            issue.Severity == ComplianceSeverity.Warning);
    }

    private static ComplianceGuard CreateGuard(
        ComplianceOptions complianceOptions,
        ExecutionOptions? executionOptions = null,
        RiskOptions? riskOptions = null,
        ComplianceStrategyEngineOptions? strategyOptions = null)
    {
        return new ComplianceGuard(
            Options.Create(complianceOptions),
            Options.Create(executionOptions ?? new ExecutionOptions()),
            Options.Create(riskOptions ?? new RiskOptions()),
            Options.Create(strategyOptions ?? new ComplianceStrategyEngineOptions()));
    }
}
