using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.OpportunityDiscovery.Infra.BackgroundJobs.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.OpportunityDiscovery.Infra.BackgroundJobs;

public static class OpportunityDiscoveryBackgroundJobsExtensions
{
    public static IServiceCollection AddOpportunityDiscoveryBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<OpportunityMarketScanJob>();
        services.AddScoped<OpportunityEvidenceRefreshJob>();
        services.AddScoped<OpportunityExpirationJob>();
        services.AddSingleton<IRecurringJobConfigurator, OpportunityDiscoveryJobConfigurator>();
        return services;
    }
}
