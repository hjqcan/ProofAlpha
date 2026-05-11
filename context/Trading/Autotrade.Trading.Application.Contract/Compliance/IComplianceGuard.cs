using Autotrade.Application.Services;
using Autotrade.Trading.Application.Contract.Execution;

namespace Autotrade.Trading.Application.Contract.Compliance;

public interface IComplianceGuard : IApplicationService
{
    ComplianceCheckResult CheckConfiguration(ExecutionMode executionMode);

    ComplianceCheckResult CheckOrderPlacement(ExecutionMode executionMode);
}

public sealed record ComplianceCheckResult(
    bool Enabled,
    bool IsCompliant,
    bool BlocksOrders,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public static ComplianceCheckResult Disabled { get; } =
        new(false, true, false, Array.Empty<ComplianceIssue>());
}

public sealed record ComplianceIssue(
    string Code,
    ComplianceSeverity Severity,
    string Message,
    bool BlocksLiveOrders);

public enum ComplianceSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}
