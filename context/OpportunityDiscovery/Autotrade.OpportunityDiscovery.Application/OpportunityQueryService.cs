using System.Text.Json;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Domain.Shared.Enums;

namespace Autotrade.OpportunityDiscovery.Application;

public sealed class OpportunityQueryService : IOpportunityQueryService, IPublishedOpportunityFeed
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMarketOpportunityRepository _opportunityRepository;
    private readonly IEvidenceItemRepository _evidenceRepository;

    public OpportunityQueryService(
        IMarketOpportunityRepository opportunityRepository,
        IEvidenceItemRepository evidenceRepository)
    {
        _opportunityRepository = opportunityRepository ?? throw new ArgumentNullException(nameof(opportunityRepository));
        _evidenceRepository = evidenceRepository ?? throw new ArgumentNullException(nameof(evidenceRepository));
    }

    public async Task<IReadOnlyList<MarketOpportunityDto>> ListOpportunitiesAsync(
        OpportunityStatus? status,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var opportunities = await _opportunityRepository
            .ListAsync(status, Math.Clamp(limit, 1, 500), cancellationToken)
            .ConfigureAwait(false);
        return opportunities.Select(OpportunityMapper.ToDto).ToList();
    }

    public async Task<MarketOpportunityDto?> GetOpportunityAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await _opportunityRepository.GetAsync(opportunityId, cancellationToken)
            .ConfigureAwait(false);
        return opportunity is null ? null : OpportunityMapper.ToDto(opportunity);
    }

    public async Task<IReadOnlyList<EvidenceItemDto>> GetEvidenceAsync(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await _opportunityRepository.GetAsync(opportunityId, cancellationToken)
            .ConfigureAwait(false);
        if (opportunity is null)
        {
            return Array.Empty<EvidenceItemDto>();
        }

        var evidenceIds = JsonSerializer.Deserialize<List<Guid>>(opportunity.EvidenceIdsJson, JsonOptions)
            ?? new List<Guid>();
        var evidence = await _evidenceRepository.GetByIdsAsync(evidenceIds, cancellationToken).ConfigureAwait(false);
        return evidence.Select(OpportunityMapper.ToDto).ToList();
    }

    public async Task<IReadOnlyList<PublishedOpportunityDto>> GetPublishedAsync(
        CancellationToken cancellationToken = default)
    {
        var opportunities = await _opportunityRepository.ListPublishedAsync(cancellationToken).ConfigureAwait(false);
        return opportunities
            .Where(opportunity => opportunity.ValidUntilUtc > DateTimeOffset.UtcNow)
            .Select(OpportunityMapper.ToPublishedDto)
            .ToList();
    }
}
