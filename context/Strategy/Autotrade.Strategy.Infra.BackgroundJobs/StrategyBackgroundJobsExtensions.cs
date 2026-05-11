using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Infra.BackgroundJobs.Workers;
using Autotrade.Strategy.Infra.BackgroundJobs.Jobs;
using Autotrade.Infra.BackgroundJobs.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Strategy.Infra.BackgroundJobs;

/// <summary>
/// Strategy 模块后台任务服务扩展方法。
/// </summary>
public static class StrategyBackgroundJobsExtensions
{
    /// <summary>
    /// 添加 Strategy 模块后台任务服务。
    /// </summary>
    public static IServiceCollection AddStrategyBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 策略引擎管理器（同时也是 IStrategyManager 的实现）
        // 使用单例模式，确保 IStrategyManager 和 HostedService 是同一个实例
        services.AddSingleton<StrategyManagerWorker>();
        services.AddSingleton<IStrategyManager>(sp => sp.GetRequiredService<StrategyManagerWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<StrategyManagerWorker>());

        // 策略数据保留服务
        services.AddScoped<StrategyRetentionJob>();
        services.AddSingleton<IRecurringJobConfigurator, StrategyJobConfigurator>();

        return services;
    }
}
