using Autotrade.Infra.BackgroundJobs.Core;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Microsoft.Extensions.Logging;

namespace Autotrade.OpportunityDiscovery.Infra.BackgroundJobs.Jobs;

public sealed class OpportunityExpirationJob : JobBase<OpportunityExpirationJob>
{
    private readonly IOpportunityDiscoveryService _service;

    public OpportunityExpirationJob(
        IOpportunityDiscoveryService service,
        ILogger<OpportunityExpirationJob> logger)
        : base(logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    protected override async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        var expired = await _service.ExpireStaleAsync(cancellationToken).ConfigureAwait(false);
        Logger.LogInformation("Expired {ExpiredCount} stale opportunities", expired);
    }
}
