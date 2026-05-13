using Autotrade.OpportunityDiscovery.Application.Contract;

namespace Autotrade.OpportunityDiscovery.Application;

public sealed class ExecutableOpportunityPolicyFeed : IExecutableOpportunityPolicyFeed
{
    private readonly IOpportunityV2Repository _repository;

    public ExecutableOpportunityPolicyFeed(IOpportunityV2Repository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<ExecutableOpportunityPolicyDto>> GetExecutableAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var policies = await _repository
            .ListExecutablePoliciesAsync(DateTimeOffset.UtcNow, Math.Clamp(limit, 1, 500), cancellationToken)
            .ConfigureAwait(false);
        return policies.Select(OpportunityV2Mapper.ToDto).ToList();
    }
}
