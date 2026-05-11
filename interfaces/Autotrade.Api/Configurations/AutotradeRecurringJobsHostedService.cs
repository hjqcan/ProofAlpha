using Autotrade.Infra.BackgroundJobs.Core;

namespace Autotrade.Api.Configurations;

public sealed class AutotradeRecurringJobsHostedService : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly IEnumerable<IRecurringJobConfigurator> _configurators;
    private readonly ILogger<AutotradeRecurringJobsHostedService> _logger;

    public AutotradeRecurringJobsHostedService(
        IConfiguration configuration,
        IEnumerable<IRecurringJobConfigurator> configurators,
        ILogger<AutotradeRecurringJobsHostedService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configurators = configurators ?? throw new ArgumentNullException(nameof(configurators));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("BackgroundJobs:Enabled", true))
        {
            _logger.LogInformation("Hangfire background jobs disabled by configuration.");
            return Task.CompletedTask;
        }

        var configurators = _configurators.ToArray();
        _logger.LogInformation("Configuring Hangfire recurring jobs: {Count} configurators", configurators.Length);
        HangfireServiceExtensions.ConfigureRecurringJobs(_configuration, configurators);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
