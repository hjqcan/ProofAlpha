using Autotrade.Infra.BackgroundJobs.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Autotrade.Cli.HostedServices;

/// <summary>
/// Hangfire 定时任务启动服务（用于 Console Host）。
/// - 读取 DI 中注册的 <see cref="IRecurringJobConfigurator"/> 并配置 Recurring Jobs
/// - 可选：对指定 JobId 触发一次启动执行（由各 configurator 决定）
/// </summary>
public sealed class HangfireRecurringJobsHostedService : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly IEnumerable<IRecurringJobConfigurator> _configurators;
    private readonly ILogger<HangfireRecurringJobsHostedService> _logger;

    public HangfireRecurringJobsHostedService(
        IConfiguration configuration,
        IEnumerable<IRecurringJobConfigurator> configurators,
        ILogger<HangfireRecurringJobsHostedService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configurators = configurators ?? throw new ArgumentNullException(nameof(configurators));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue<bool>("BackgroundJobs:Enabled", true))
        {
            _logger.LogInformation("Hangfire background jobs disabled (BackgroundJobs:Enabled=false)");
            return Task.CompletedTask;
        }

        var list = _configurators as IRecurringJobConfigurator[] ?? _configurators.ToArray();
        _logger.LogInformation("Configuring Hangfire recurring jobs: {Count} configurators", list.Length);

        HangfireServiceExtensions.ConfigureRecurringJobs(_configuration, list);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

