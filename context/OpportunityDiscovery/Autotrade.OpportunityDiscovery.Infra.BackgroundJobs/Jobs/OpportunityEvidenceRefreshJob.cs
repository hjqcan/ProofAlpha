using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Autotrade.OpportunityDiscovery.Infra.BackgroundJobs.Jobs;

public sealed class OpportunityEvidenceRefreshJob : JobBase<OpportunityEvidenceRefreshJob>
{
    private readonly IOpportunityDiscoveryService _service;
    private readonly IConfiguration _configuration;

    public OpportunityEvidenceRefreshJob(
        IOpportunityDiscoveryService service,
        IConfiguration configuration,
        ILogger<OpportunityEvidenceRefreshJob> logger)
        : base(logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    protected override async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        var section = _configuration.GetSection("BackgroundJobs:OpportunityEvidenceRefresh");
        await _service.ScanAsync(
                new OpportunityScanRequest(
                    Trigger: "evidence-refresh",
                    MinVolume24h: section.GetValue("MinVolume24h", 1000m),
                    MinLiquidity: section.GetValue("MinLiquidity", 1000m),
                    MaxMarkets: section.GetValue("MaxMarkets", 10)),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
