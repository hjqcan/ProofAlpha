using Autotrade.Strategy.Application.Parameters;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Strategy.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.Strategy.Infra.Data.Repositories;

public sealed class StrategyParameterVersionRepository : IStrategyParameterVersionRepository
{
    private readonly StrategyContext _context;

    public StrategyParameterVersionRepository(StrategyContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(StrategyParameterVersion version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        await _context.StrategyParameterVersions.AddAsync(version, cancellationToken).ConfigureAwait(false);
    }

    public Task<StrategyParameterVersion?> GetAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        return _context.StrategyParameterVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(version => version.Id == versionId, cancellationToken);
    }

    public Task<StrategyParameterVersion?> GetLatestAsync(
        string strategyId,
        CancellationToken cancellationToken = default)
    {
        return _context.StrategyParameterVersions
            .AsNoTracking()
            .Where(version => version.StrategyId == strategyId)
            .OrderByDescending(version => version.CreatedAtUtc)
            .ThenByDescending(version => version.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StrategyParameterVersion>> GetLatestByStrategyIdsAsync(
        IReadOnlyCollection<string> strategyIds,
        CancellationToken cancellationToken = default)
    {
        if (strategyIds.Count == 0)
        {
            return [];
        }

        var versions = await _context.StrategyParameterVersions
            .AsNoTracking()
            .Where(version => strategyIds.Contains(version.StrategyId))
            .OrderByDescending(version => version.CreatedAtUtc)
            .ThenByDescending(version => version.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return versions
            .GroupBy(version => version.StrategyId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    public async Task<IReadOnlyList<StrategyParameterVersion>> GetRecentAsync(
        string strategyId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _context.StrategyParameterVersions
            .AsNoTracking()
            .Where(version => version.StrategyId == strategyId)
            .OrderByDescending(version => version.CreatedAtUtc)
            .ThenByDescending(version => version.Id)
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
