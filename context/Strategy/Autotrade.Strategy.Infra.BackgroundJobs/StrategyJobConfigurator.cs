using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.Strategy.Infra.BackgroundJobs.Jobs;
using Microsoft.Extensions.Configuration;

namespace Autotrade.Strategy.Infra.BackgroundJobs;

/// <summary>
/// Strategy 模块 Hangfire 定时任务配置器。
/// </summary>
public sealed class StrategyJobConfigurator : IRecurringJobConfigurator
{
    private const string StrategyRetentionJobId = "strategy-retention-cleanup";
    private const string StrategyRetentionSection = "BackgroundJobs:StrategyRetention";

    public void ConfigureJobs(IConfiguration configuration)
    {
        RecurringJobHelper.AddOrUpdateJob<StrategyRetentionJob>(
            configuration,
            jobId: StrategyRetentionJobId,
            jobExpression: job => job.ExecuteAsync(CancellationToken.None),
            configSection: StrategyRetentionSection,
            defaultCronExpression: "0 */6 * * *");
    }
}

