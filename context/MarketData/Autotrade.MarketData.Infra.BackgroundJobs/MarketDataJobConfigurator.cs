using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.MarketData.Infra.BackgroundJobs.Jobs;
using Hangfire;
using Microsoft.Extensions.Configuration;

namespace Autotrade.MarketData.Infra.BackgroundJobs;

/// <summary>
/// MarketData 模块 Hangfire 定时任务配置器。
/// </summary>
public sealed class MarketDataJobConfigurator : IRecurringJobConfigurator
{
    private const string MarketCatalogSyncJobId = "marketdata-catalog-sync";
    private const string MarketCatalogSyncSection = "BackgroundJobs:MarketCatalogSync";

    public void ConfigureJobs(IConfiguration configuration)
    {
        // 市场目录同步（分钟级）
        RecurringJobHelper.AddOrUpdateJob<MarketCatalogSyncJob>(
            configuration,
            jobId: MarketCatalogSyncJobId,
            jobExpression: job => job.ExecuteAsync(CancellationToken.None),
            configSection: MarketCatalogSyncSection,
            defaultCronExpression: "*/5 * * * *");

        var enabled = configuration.GetValue<bool>($"{MarketCatalogSyncSection}:Enabled", true);
        var runOnStartup = configuration.GetValue<bool>($"{MarketCatalogSyncSection}:RunOnStartup", true);
        if (enabled && runOnStartup)
        {
            RecurringJob.TriggerJob(MarketCatalogSyncJobId);
        }
    }
}

