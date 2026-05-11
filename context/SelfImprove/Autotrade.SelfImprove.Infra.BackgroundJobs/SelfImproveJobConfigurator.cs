using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.SelfImprove.Infra.BackgroundJobs.Jobs;
using Microsoft.Extensions.Configuration;

namespace Autotrade.SelfImprove.Infra.BackgroundJobs;

public sealed class SelfImproveJobConfigurator : IRecurringJobConfigurator
{
    public void ConfigureJobs(IConfiguration configuration)
    {
        RecurringJobHelper.AddOrUpdateJob<SelfImproveAnalysisJob>(
            configuration,
            jobId: "self-improve-analysis",
            jobExpression: job => job.ExecuteAsync(CancellationToken.None),
            configSection: "BackgroundJobs:SelfImproveAnalysis",
            defaultCronExpression: "*/30 * * * *");
    }
}
