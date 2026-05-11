using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.Infra.BackgroundJobs.Core;

/// <summary>
/// Hangfire 核心服务扩展方法（通用配置）
/// </summary>
public static class HangfireServiceExtensions
{
    /// <summary>
    /// 添加 Hangfire 核心服务（PostgreSQL 存储 + 服务器）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象</param>
    /// <param name="connectionStringKey">数据库连接字符串的配置键（默认：AutotradeDatabase）</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddHangfireCore(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringKey = "AutotradeDatabase")
    {
        // 获取数据库连接字符串
        var connectionString = configuration.GetConnectionString(connectionStringKey);
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException($"未找到数据库连接字符串 '{connectionStringKey}'");
        }

        // 配置 Hangfire 使用 PostgreSQL 存储
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(connectionString)));

        // 添加 Hangfire 服务器
        services.AddHangfireServer(options =>
        {
            options.WorkerCount = configuration.GetValue<int>("BackgroundJobs:WorkerCount", 5);
            options.SchedulePollingInterval = TimeSpan.FromSeconds(
                configuration.GetValue<int>("BackgroundJobs:SchedulePollingIntervalSeconds", 15));
        });

        return services;
    }

    /// <summary>
    /// 使用 Hangfire Dashboard（仅在 ASP.NET Core Web 应用中使用）
    /// </summary>
    /// <param name="app">应用构建器</param>
    /// <param name="configuration">配置对象</param>
    /// <param name="dashboardTitle">Dashboard 标题（默认：Autotrade 后台任务管理）</param>
    /// <returns>应用构建器</returns>
    public static IApplicationBuilder UseHangfireDashboardWithConfig(
        this IApplicationBuilder app,
        IConfiguration configuration,
        string dashboardTitle = "Autotrade 后台任务管理")
    {
        var dashboardEnabled = configuration.GetValue<bool>("BackgroundJobs:Dashboard:Enabled", true);
        if (!dashboardEnabled)
        {
            return app;
        }

        var dashboardPath = configuration["BackgroundJobs:Dashboard:Path"] ?? "/hangfire";

        app.UseHangfireDashboard(dashboardPath, new DashboardOptions
        {
            // 安全提醒：Dashboard 暴露会导致敏感信息泄露。
            // 若要启用，请在宿主 WebApp 中自行配置 Authorization 过滤器并限制访问来源。
            DashboardTitle = dashboardTitle,
            DisplayStorageConnectionString = false
        });

        return app;
    }

    /// <summary>
    /// 配置定时任务（使用配置器模式）
    /// </summary>
    /// <param name="app">应用构建器</param>
    /// <param name="configuration">配置对象</param>
    /// <param name="configurators">任务配置器列表</param>
    /// <returns>应用构建器</returns>
    public static IApplicationBuilder UseRecurringJobs(
        this IApplicationBuilder app,
        IConfiguration configuration,
        params IRecurringJobConfigurator[] configurators)
    {
        foreach (var configurator in configurators)
        {
            configurator.ConfigureJobs(configuration);
        }

        return app;
    }

    /// <summary>
    /// 配置定时任务（用于非 ASP.NET Core 主机，如 IHost）
    /// </summary>
    /// <param name="configuration">配置对象</param>
    /// <param name="configurators">任务配置器列表</param>
    public static void ConfigureRecurringJobs(
        IConfiguration configuration,
        params IRecurringJobConfigurator[] configurators)
    {
        foreach (var configurator in configurators)
        {
            configurator.ConfigureJobs(configuration);
        }
    }
}
