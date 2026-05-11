using Autotrade.Trading.Application.Contract.Risk;
using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Autotrade.Trading.Infra.CrossCutting.IoC;

/// <summary>
/// EF Core 实现的风控事件仓储，将事件持久化到数据库。
/// 使用 IServiceScopeFactory 确保在 Singleton 服务中可以正确获取 Scoped DbContext。
/// </summary>
public sealed class EfRiskEventRepository : IRiskEventRepository
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EfRiskEventRepository> _logger;

    public EfRiskEventRepository(IServiceScopeFactory scopeFactory, ILogger<EfRiskEventRepository> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task AddAsync(
        string code,
        RiskSeverity severity,
        string message,
        string? strategyId = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TradingContext>();

            var entity = new RiskEventLog(code, severity, message, strategyId, null, contextJson);
            context.RiskEventLogs.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "RiskEvent persisted: Id={Id}, Code={Code}, Severity={Severity}, Strategy={StrategyId}",
                entity.Id, code, severity, strategyId ?? "global");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 日志记录但不抛出异常，避免影响主流程
            _logger.LogError(ex,
                "Failed to persist RiskEvent: Code={Code}, Severity={Severity}, Message={Message}",
                code, severity, message);
        }
    }

    public async Task<IReadOnlyList<RiskEventRecord>> QueryAsync(
        string? strategyId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TradingContext>();

            var query = context.RiskEventLogs.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(strategyId))
            {
                query = query.Where(e => e.StrategyId == strategyId);
            }

            if (fromUtc.HasValue)
            {
                query = query.Where(e => e.CreatedAtUtc >= fromUtc.Value);
            }

            if (toUtc.HasValue)
            {
                query = query.Where(e => e.CreatedAtUtc <= toUtc.Value);
            }

            var entities = await query
                .OrderByDescending(e => e.CreatedAtUtc)
                .Take(limit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return entities
                .Select(ToRecord)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to query RiskEvents");
            return Array.Empty<RiskEventRecord>();
        }
    }

    public async Task<RiskEventRecord?> GetAsync(Guid riskEventId, CancellationToken cancellationToken = default)
    {
        if (riskEventId == Guid.Empty)
        {
            return null;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TradingContext>();
            var entity = await context.RiskEventLogs
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == riskEventId, cancellationToken)
                .ConfigureAwait(false);

            return entity is null ? null : ToRecord(entity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to get RiskEvent {RiskEventId}", riskEventId);
            return null;
        }
    }

    private static RiskEventRecord ToRecord(RiskEventLog entity)
        => new(
            entity.Id,
            entity.Code,
            entity.Severity,
            entity.Message,
            entity.StrategyId,
            entity.ContextJson,
            entity.CreatedAtUtc,
            entity.MarketId);
}
