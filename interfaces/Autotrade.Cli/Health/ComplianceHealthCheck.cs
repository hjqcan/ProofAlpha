using Autotrade.Trading.Application.Contract.Compliance;
using Autotrade.Trading.Application.Contract.Execution;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Autotrade.Cli.Health;

public sealed class ComplianceHealthCheck : IHealthCheck
{
    private readonly IComplianceGuard _complianceGuard;
    private readonly ExecutionOptions _executionOptions;
    private readonly ComplianceOptions _complianceOptions;

    public ComplianceHealthCheck(
        IComplianceGuard complianceGuard,
        IOptions<ExecutionOptions> executionOptions,
        IOptions<ComplianceOptions> complianceOptions)
    {
        _complianceGuard = complianceGuard ?? throw new ArgumentNullException(nameof(complianceGuard));
        _executionOptions = executionOptions?.Value ?? throw new ArgumentNullException(nameof(executionOptions));
        _complianceOptions = complianceOptions?.Value ?? throw new ArgumentNullException(nameof(complianceOptions));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_complianceOptions.Enabled || _executionOptions.Mode != ExecutionMode.Live)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Compliance guard not blocking current mode."));
        }

        var issues = _complianceGuard.CheckConfiguration(_executionOptions.Mode).Issues
            .Where(issue => issue.BlocksLiveOrders)
            .Select(issue => $"{issue.Code}: {issue.Message}")
            .ToList();

        return issues.Count == 0
            ? Task.FromResult(HealthCheckResult.Healthy("Compliance guard passed."))
            : Task.FromResult(HealthCheckResult.Unhealthy(string.Join("; ", issues)));
    }
}
