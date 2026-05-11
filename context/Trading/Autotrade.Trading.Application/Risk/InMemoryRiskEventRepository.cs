using System.Collections.Concurrent;
using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Autotrade.Trading.Application.Risk;

/// <summary>
/// In-memory risk event repository (with logging, for development/testing).
/// Production should use <see cref="EfRiskEventRepository"/>.
/// </summary>
public sealed class InMemoryRiskEventRepository : IRiskEventRepository
{
    private readonly ILogger<InMemoryRiskEventRepository> _logger;
    private readonly ConcurrentQueue<RiskEventRecord> _events = new();
    private const int MaxEvents = 10000;

    public InMemoryRiskEventRepository(ILogger<InMemoryRiskEventRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task AddAsync(
        string code,
        RiskSeverity severity,
        string message,
        string? strategyId = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        var record = new RiskEventRecord(
            Guid.NewGuid(),
            code,
            severity,
            message,
            strategyId,
            contextJson,
            DateTimeOffset.UtcNow);

        _events.Enqueue(record);

        // 限制内存中的事件数量
        while (_events.Count > MaxEvents && _events.TryDequeue(out _))
        {
            // discard oldest
        }

        _logger.LogInformation(
            "RiskEvent: Code={Code}, Severity={Severity}, Strategy={StrategyId}, Message={Message}",
            code,
            severity,
            strategyId ?? "global",
            message);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RiskEventRecord>> QueryAsync(
        string? strategyId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = _events.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(strategyId))
        {
            query = query.Where(e => string.Equals(e.StrategyId, strategyId, StringComparison.OrdinalIgnoreCase));
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(e => e.CreatedAtUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(e => e.CreatedAtUtc <= toUtc.Value);
        }

        var result = query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<RiskEventRecord>>(result);
    }

    public Task<RiskEventRecord?> GetAsync(Guid riskEventId, CancellationToken cancellationToken = default)
    {
        if (riskEventId == Guid.Empty)
        {
            return Task.FromResult<RiskEventRecord?>(null);
        }

        return Task.FromResult(_events.FirstOrDefault(e => e.Id == riskEventId));
    }
}
