using System.Text.Json;
using Autotrade.OpportunityDiscovery.Application.Contract;
using Autotrade.OpportunityDiscovery.Application.Evidence;
using Autotrade.OpportunityDiscovery.Domain.Entities;

namespace Autotrade.OpportunityDiscovery.Application;

public sealed class SourceRegistryService : ISourceRegistryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISourceProfileRepository _sourceProfiles;

    public SourceRegistryService(ISourceProfileRepository sourceProfiles)
    {
        _sourceProfiles = sourceProfiles ?? throw new ArgumentNullException(nameof(sourceProfiles));
    }

    public async Task<IReadOnlyList<SourceProfileDto>> EnsureDefaultSourceProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var created = new List<SourceProfile>();
        var now = DateTimeOffset.UtcNow;
        foreach (var definition in SourcePackCatalog.Defaults)
        {
            var existing = await _sourceProfiles
                .GetLatestByKeyAsync(definition.SourceKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                continue;
            }

            var profile = definition.ToProfile(now);
            await _sourceProfiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);
            created.Add(profile);
        }

        if (created.Count > 0)
        {
            return created.Select(SourceRegistryMapper.ToDto).ToList();
        }

        return (await _sourceProfiles.ListCurrentAsync(cancellationToken).ConfigureAwait(false))
            .Select(SourceRegistryMapper.ToDto)
            .ToList();
    }

    public async Task<SourceProfileDto> AppendSourceProfileVersionAsync(
        AppendSourceProfileVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceKey = SourceProfileKeys.Normalize(request.SourceKey);
        var coveredCategoriesJson = JsonSerializer.Serialize(request.CoveredCategories, JsonOptions);
        var latest = await _sourceProfiles.GetLatestByKeyAsync(sourceKey, cancellationToken).ConfigureAwait(false);
        SourceProfile next = latest is null
            ? new SourceProfile(
                sourceKey,
                request.SourceKind,
                request.SourceName,
                request.AuthorityKind,
                request.IsOfficial,
                request.ExpectedLatencySeconds,
                coveredCategoriesJson,
                request.HistoricalConflictRate,
                request.HistoricalPassedGateContribution,
                request.ReliabilityScore,
                1,
                null,
                request.ChangeReason,
                DateTimeOffset.UtcNow)
            : latest.CreateNextVersion(
                request.AuthorityKind,
                request.IsOfficial,
                request.ExpectedLatencySeconds,
                coveredCategoriesJson,
                request.HistoricalConflictRate,
                request.HistoricalPassedGateContribution,
                request.ReliabilityScore,
                request.ChangeReason,
                DateTimeOffset.UtcNow);

        await _sourceProfiles.AddAsync(next, cancellationToken).ConfigureAwait(false);
        return SourceRegistryMapper.ToDto(next);
    }

    public async Task<IReadOnlyList<SourceProfileDto>> ListCurrentProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        return (await _sourceProfiles.ListCurrentAsync(cancellationToken).ConfigureAwait(false))
            .Select(SourceRegistryMapper.ToDto)
            .ToList();
    }
}
