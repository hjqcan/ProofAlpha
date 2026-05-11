using System.Text.Json;
using Autotrade.Strategy.Application.Engine;
using Autotrade.Strategy.Application.Contract.Strategies;
using Autotrade.Strategy.Domain.Entities;
using Autotrade.Strategy.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using InfraStrategyContext = Autotrade.Strategy.Infra.Data.Context.StrategyContext;

namespace Autotrade.Strategy.Infra.Data.Repositories;

public sealed class StrategyRunStateRepository : IStrategyRunStateRepository
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StrategyRunStateRepository> _logger;

    public StrategyRunStateRepository(IServiceScopeFactory scopeFactory, ILogger<StrategyRunStateRepository> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task UpsertAsync(StrategyStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InfraStrategyContext>();

        var existing = await context.StrategyRunStates
            .FirstOrDefaultAsync(x => x.StrategyId == status.StrategyId, cancellationToken)
            .ConfigureAwait(false);

        var activeMarketsJson = status.ActiveMarkets is { Count: > 0 }
            ? JsonSerializer.Serialize(status.ActiveMarkets)
            : null;
        var desiredState = status.DesiredState?.ToString();
        var blockedReason = status.BlockedReason;

        if (existing is null)
        {
            var entity = new StrategyRunState(
                status.StrategyId,
                status.Name,
                status.State.ToString(),
                status.Enabled,
                status.ConfigVersion,
                status.RestartCount,
                status.StartedAtUtc,
                status.LastDecisionAtUtc,
                status.LastHeartbeatUtc,
                status.LastError,
                activeMarketsJson,
                status.CycleCount,
                status.SnapshotsProcessed,
                status.ChannelBacklog,
                desiredState,
                blockedReason?.Kind.ToString(),
                blockedReason?.Code,
                blockedReason?.Message);

            context.StrategyRunStates.Add(entity);
        }
        else
        {
            existing.Update(
                status.State.ToString(),
                status.Enabled,
                status.ConfigVersion,
                status.RestartCount,
                status.StartedAtUtc,
                status.LastDecisionAtUtc,
                status.LastHeartbeatUtc,
                status.LastError,
                activeMarketsJson,
                status.CycleCount,
                status.SnapshotsProcessed,
                status.ChannelBacklog,
                desiredState,
                blockedReason?.Kind.ToString(),
                blockedReason?.Code,
                blockedReason?.Message);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Failed to upsert strategy run state: {StrategyId}", status.StrategyId);
            throw;
        }
    }

    public async Task SetDesiredStateAsync(
        string strategyId,
        StrategyState desiredState,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            throw new ArgumentException("StrategyId cannot be empty.", nameof(strategyId));
        }

        if (desiredState is StrategyState.Created or StrategyState.Faulted)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desiredState),
                desiredState,
                "Desired state must be Running, Paused, or Stopped.");
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InfraStrategyContext>();

        var entity = await context.StrategyRunStates
            .FirstOrDefaultAsync(x => x.StrategyId == strategyId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new InvalidOperationException($"Strategy run state does not exist for {strategyId}.");
        }

        entity.Update(
            entity.State,
            entity.Enabled,
            entity.ConfigVersion,
            entity.RestartCount,
            entity.StartedAtUtc,
            entity.LastDecisionAtUtc,
            entity.LastHeartbeatAtUtc,
            entity.LastError,
            entity.ActiveMarketsJson,
            entity.CycleCount,
            entity.SnapshotsProcessed,
            entity.ChannelBacklog,
            desiredState.ToString(),
            entity.BlockedReasonKind,
            entity.BlockedReasonCode,
            entity.BlockedReasonMessage);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<StrategyStatus?> GetAsync(string strategyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InfraStrategyContext>();

        var entity = await context.StrategyRunStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StrategyId == strategyId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        var activeMarkets = ParseActiveMarkets(entity.ActiveMarketsJson);

        return new StrategyStatus(
            entity.StrategyId,
            entity.Name,
            Enum.TryParse(entity.State, out StrategyState state) ? state : StrategyState.Created,
            entity.Enabled,
            entity.ConfigVersion,
            entity.RestartCount,
            entity.StartedAtUtc,
            entity.LastDecisionAtUtc,
            entity.LastHeartbeatAtUtc,
            entity.LastError,
            activeMarkets,
            entity.CycleCount,
            entity.SnapshotsProcessed,
            entity.ChannelBacklog,
            DesiredState: Enum.TryParse(entity.DesiredState, out StrategyState desiredState) ? desiredState : null,
            BlockedReason: ParseBlockedReason(entity));
    }

    public async Task<IReadOnlyList<StrategyStatus>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InfraStrategyContext>();

        var entities = await context.StrategyRunStates.AsNoTracking()
            .OrderBy(x => x.StrategyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(entity =>
            {
                var activeMarkets = ParseActiveMarkets(entity.ActiveMarketsJson);
                return new StrategyStatus(
                    entity.StrategyId,
                    entity.Name,
                    Enum.TryParse(entity.State, out StrategyState state) ? state : StrategyState.Created,
                    entity.Enabled,
                    entity.ConfigVersion,
                    entity.RestartCount,
                    entity.StartedAtUtc,
                    entity.LastDecisionAtUtc,
                    entity.LastHeartbeatAtUtc,
                    entity.LastError,
                    activeMarkets,
                    entity.CycleCount,
                    entity.SnapshotsProcessed,
                    entity.ChannelBacklog,
                    DesiredState: Enum.TryParse(entity.DesiredState, out StrategyState desiredState) ? desiredState : null,
                    BlockedReason: ParseBlockedReason(entity));
            })
            .ToList();
    }

    private static IReadOnlyList<string>? ParseActiveMarkets(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static StrategyBlockedReason? ParseBlockedReason(StrategyRunState entity)
    {
        if (string.IsNullOrWhiteSpace(entity.BlockedReasonKind))
        {
            return null;
        }

        if (!Enum.TryParse(entity.BlockedReasonKind, out StrategyBlockedReasonKind kind) ||
            kind == StrategyBlockedReasonKind.None)
        {
            return null;
        }

        return new StrategyBlockedReason(
            kind,
            string.IsNullOrWhiteSpace(entity.BlockedReasonCode)
                ? kind.ToString()
                : entity.BlockedReasonCode,
            string.IsNullOrWhiteSpace(entity.BlockedReasonMessage)
                ? kind.ToString()
                : entity.BlockedReasonMessage);
    }
}
