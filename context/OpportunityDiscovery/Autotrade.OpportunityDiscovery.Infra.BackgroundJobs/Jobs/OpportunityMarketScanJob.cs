using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Autotrade.OpportunityDiscovery.Infra.BackgroundJobs.Jobs;

public sealed class OpportunityMarketScanJob : JobBase<OpportunityMarketScanJob>
{
    private readonly IOpportunityDiscoveryService _service;
    private readonly IConfiguration _configuration;

    public OpportunityMarketScanJob(
        IOpportunityDiscoveryService service,
        IConfiguration configuration,
        ILogger<OpportunityMarketScanJob> logger)
        : base(logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    protected override async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        var section = _configuration.GetSection("BackgroundJobs:OpportunityMarketScan");
        await _service.ScanAsync(
                new OpportunityScanRequest(
                    Trigger: "market-scan",
                    MinVolume24h: section.GetValue("MinVolume24h", 500m),
                    MinLiquidity: section.GetValue("MinLiquidity", 500m),
                    MaxMarkets: section.GetValue("MaxMarkets", 20)),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
