using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.SelfImprove.Infra.BackgroundJobs.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autotrade.SelfImprove.Infra.BackgroundJobs;

public static class SelfImproveBackgroundJobsExtensions
{
    public static IServiceCollection AddSelfImproveBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<SelfImproveAnalysisJob>();
        services.AddSingleton<IRecurringJobConfigurator, SelfImproveJobConfigurator>();
        return services;
    }
}
