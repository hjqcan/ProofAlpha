using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.MarketData.Infra.BackgroundJobs.Jobs;
using Autotrade.MarketData.Infra.BackgroundJobs.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.MarketData.Infra.BackgroundJobs;

/// <summary>
/// MarketData 模块后台任务服务扩展方法。
/// </summary>
public static class MarketDataBackgroundJobsExtensions
{
    /// <summary>
    /// 添加 MarketData 模块后台任务服务。
    /// </summary>
    public static IServiceCollection AddMarketDataBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Hangfire Recurring Jobs
        services.AddScoped<MarketCatalogSyncJob>();
        services.AddSingleton<IRecurringJobConfigurator, MarketDataJobConfigurator>();
        services.AddHostedService<SpotPriceFeedWorker>();

        return services;
    }
}
