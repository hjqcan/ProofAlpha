using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.Trading.Infra.BackgroundJobs.Jobs;
using Microsoft.Extensions.Configuration;

namespace Autotrade.Trading.Infra.BackgroundJobs;

/// <summary>
/// Trading 模块 Hangfire 定时任务配置器。
/// </summary>
public sealed class TradingJobConfigurator : IRecurringJobConfigurator
{
    private const string TradingRetentionJobId = "trading-retention-cleanup";
    private const string TradingRetentionSection = "BackgroundJobs:TradingRetention";

    public void ConfigureJobs(IConfiguration configuration)
    {
        RecurringJobHelper.AddOrUpdateJob<TradingRetentionJob>(
            configuration,
            jobId: TradingRetentionJobId,
            jobExpression: job => job.ExecuteAsync(CancellationToken.None),
            configSection: TradingRetentionSection,
            defaultCronExpression: "0 3 * * *");
    }
}

