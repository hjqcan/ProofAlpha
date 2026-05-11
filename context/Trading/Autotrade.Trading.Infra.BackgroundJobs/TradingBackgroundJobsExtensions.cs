using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.Trading.Infra.BackgroundJobs.Jobs;
using Autotrade.Trading.Infra.BackgroundJobs.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Trading.Infra.BackgroundJobs;

/// <summary>
/// Trading 模块后台任务服务扩展方法。
/// </summary>
public static class TradingBackgroundJobsExtensions
{
    /// <summary>
    /// 添加 Trading 模块后台任务服务。
    /// </summary>
    public static IServiceCollection AddTradingBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 启动时执行的 HostedService
        services.AddHostedService<TradingAccountBootstrapWorker>();
        services.AddHostedService<OrderStateRecoveryWorker>();

        // 持续运行的后台服务
        services.AddHostedService<OrderReconciliationWorker>();
        services.AddHostedService<UserOrderEventWorker>();
        services.AddHostedService<KillSwitchWorker>();
        services.AddHostedService<UnhedgedExposureWorker>();
        services.AddHostedService<AccountSyncWorker>();

        // Hangfire Recurring Jobs
        services.AddScoped<TradingRetentionJob>();
        services.AddSingleton<IRecurringJobConfigurator, TradingJobConfigurator>();

        return services;
    }
}
