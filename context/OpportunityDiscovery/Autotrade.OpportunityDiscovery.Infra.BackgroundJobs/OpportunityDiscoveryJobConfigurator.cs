using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.OpportunityDiscovery.Infra.BackgroundJobs.Jobs;
using Microsoft.Extensions.Configuration;

namespace Autotrade.OpportunityDiscovery.Infra.BackgroundJobs;

public sealed class OpportunityDiscoveryJobConfigurator : IRecurringJobConfigurator
{
    public void ConfigureJobs(IConfiguration configuration)
    {
        RecurringJobHelper.AddOrUpdateJob<OpportunityMarketScanJob>(
            configuration,
            "opportunity-market-scan",
            job => job.ExecuteAsync(CancellationToken.None),
            "BackgroundJobs:OpportunityMarketScan",
            "*/15 * * * *");

        RecurringJobHelper.AddOrUpdateJob<OpportunityEvidenceRefreshJob>(
            configuration,
            "opportunity-evidence-refresh",
            job => job.ExecuteAsync(CancellationToken.None),
            "BackgroundJobs:OpportunityEvidenceRefresh",
            "*/30 * * * *");

        RecurringJobHelper.AddOrUpdateJob<OpportunityExpirationJob>(
            configuration,
            "opportunity-expiration",
            job => job.ExecuteAsync(CancellationToken.None),
            "BackgroundJobs:OpportunityExpiration",
            "*/5 * * * *");
    }
}
