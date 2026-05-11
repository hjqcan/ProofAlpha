using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.Strategy.Application.Persistence;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.Strategy.Infra.BackgroundJobs.Jobs;

/// <summary>
/// Strategy 数据保留清理任务（Hangfire）。
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public sealed class StrategyRetentionJob : JobBase<StrategyRetentionJob>
{
    private readonly IStrategyMaintenanceRepository _repository;
    private readonly StrategyRetentionOptions _options;

    public StrategyRetentionJob(
        IStrategyMaintenanceRepository repository,
        IOptions<StrategyRetentionOptions> options,
        Microsoft.Extensions.Logging.ILogger<StrategyRetentionJob> logger)
        : base(logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        _options.Validate();

        var decisionCutoff = DateTimeOffset.UtcNow.AddDays(-_options.DecisionLogRetentionDays);
        var auditCutoff = DateTimeOffset.UtcNow.AddDays(-_options.CommandAuditRetentionDays);

        var removed = await _repository
            .CleanupAsync(decisionCutoff, auditCutoff, cancellationToken)
            .ConfigureAwait(false);

        if (removed > 0)
        {
            Logger.LogInformation("Strategy retention cleanup removed {Count} rows", removed);
        }
    }
}

