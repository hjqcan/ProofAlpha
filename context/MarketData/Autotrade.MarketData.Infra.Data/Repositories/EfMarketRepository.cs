using Autotrade.MarketData.Application.Contract.Repositories;
using Autotrade.MarketData.Domain.Entities;
using Autotrade.MarketData.Domain.Shared.Enums;
using Autotrade.MarketData.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.MarketData.Infra.Data.Repositories;

/// <summary>
/// 市场仓储 EF Core 实现。
/// </summary>
public sealed class EfMarketRepository : IMarketRepository
{
    private readonly MarketDataContext _context;

    public EfMarketRepository(MarketDataContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<MarketDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var market = await _context.Markets
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return market is null ? null : ToDto(market);
    }

    public async Task<MarketDto?> GetByMarketIdAsync(string marketId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            return null;
        }

        var market = await _context.Markets
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.MarketId == marketId, cancellationToken)
            .ConfigureAwait(false);

        return market is null ? null : ToDto(market);
    }

    public async Task<IReadOnlyList<MarketDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var markets = await _context.Markets
            .AsNoTracking()
            .OrderBy(m => m.MarketId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return markets.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<MarketDto>> GetByStatusAsync(MarketStatus status, CancellationToken cancellationToken = default)
    {
        var markets = await _context.Markets
            .AsNoTracking()
            .Where(m => m.Status == status)
            .OrderBy(m => m.MarketId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return markets.Select(ToDto).ToList();
    }

    public async Task UpsertRangeAsync(IEnumerable<MarketDto> markets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markets);

        var dtoList = markets
            .Select(NormalizeDtoOrNull)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();
        if (dtoList.Count == 0)
        {
            return;
        }

        // 同一批次可能包含重复 MarketId：按 MarketId 去重，避免违反唯一索引。
        // 规则：同 MarketId 取最后一条（保证调用方可覆盖前值）。
        var deduped = new Dictionary<string, MarketDto>(StringComparer.Ordinal);
        foreach (var dto in dtoList)
        {
            deduped[dto.MarketId] = dto;
        }

        var finalDtos = deduped.Values.ToList();
        var marketIds = deduped.Keys.ToList();

        // 并发：多进程同时 Upsert 可能遇到唯一索引冲突。这里做一次轻量重试。
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await UpsertInternalAsync(marketIds, finalDtos, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                // 清理 ChangeTracker，避免残留的 Added/Modified 实体影响重试
                _context.ChangeTracker.Clear();
            }
        }
    }

    private async Task UpsertInternalAsync(
        IReadOnlyList<string> marketIds,
        IReadOnlyList<MarketDto> dtos,
        CancellationToken cancellationToken)
    {
        // 查询已存在的市场（按 MarketId 唯一键）
        var existingEntities = await _context.Markets
            .Where(m => marketIds.Contains(m.MarketId))
            .ToDictionaryAsync(m => m.MarketId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var dto in dtos)
        {
            if (existingEntities.TryGetValue(dto.MarketId, out var existing))
            {
                // 仅在变更时更新，减少无谓写入
                if (!string.Equals(existing.Name, dto.Name, StringComparison.Ordinal))
                {
                    existing.Rename(dto.Name);
                }

                if (existing.Status != dto.Status)
                {
                    existing.UpdateStatus(dto.Status);
                }

                if (existing.ExpiresAtUtc != dto.ExpiresAtUtc)
                {
                    existing.UpdateExpiresAtUtc(dto.ExpiresAtUtc);
                }
            }
            else
            {
                // 新增
                var entity = new Market(dto.MarketId, dto.Name, dto.ExpiresAtUtc);
                entity.UpdateStatus(dto.Status);

                // 设置 Id
                var idProperty = typeof(NetDevPack.Domain.Entity).GetProperty("Id");
                idProperty?.SetValue(entity, dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id);

                await _context.Markets.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                existingEntities[dto.MarketId] = entity;
            }
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static MarketDto? NormalizeDtoOrNull(MarketDto dto)
    {
        if (dto is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.MarketId))
        {
            return null;
        }

        // MarketId 在 DB 侧有唯一索引：必须做规范化（Trim）以避免“同一 MarketId 不同空白”的脏数据
        var marketId = dto.MarketId.Trim();
        if (marketId.Length == 0)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(dto.Name) ? marketId : dto.Name.Trim();

        return dto with
        {
            MarketId = marketId,
            Name = name
        };
    }

    public async Task AddAsync(MarketDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var normalized = NormalizeDtoOrNull(dto)
                         ?? throw new ArgumentException("market dto is invalid.", nameof(dto));

        var entity = new Market(normalized.MarketId, normalized.Name, normalized.ExpiresAtUtc);
        entity.UpdateStatus(dto.Status);

        var idProperty = typeof(NetDevPack.Domain.Entity).GetProperty("Id");
        idProperty?.SetValue(entity, dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id);

        await _context.Markets.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(MarketDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var normalized = NormalizeDtoOrNull(dto)
                         ?? throw new ArgumentException("market dto is invalid.", nameof(dto));

        var entity = await _context.Markets
            .FirstOrDefaultAsync(m => m.Id == dto.Id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new InvalidOperationException($"Market with ID {dto.Id} not found");
        }

        if (!string.Equals(entity.MarketId, normalized.MarketId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MarketId mismatch for ID {dto.Id}. Existing='{entity.MarketId}', New='{normalized.MarketId}'.");
        }

        if (!string.Equals(entity.Name, normalized.Name, StringComparison.Ordinal))
        {
            entity.Rename(normalized.Name);
        }

        if (entity.Status != normalized.Status)
        {
            entity.UpdateStatus(normalized.Status);
        }

        if (entity.ExpiresAtUtc != normalized.ExpiresAtUtc)
        {
            entity.UpdateExpiresAtUtc(normalized.ExpiresAtUtc);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Markets.CountAsync(cancellationToken).ConfigureAwait(false);
    }

    private static MarketDto ToDto(Market m) => new(
        m.Id,
        m.MarketId,
        m.Name,
        m.Status,
        m.ExpiresAtUtc,
        m.CreatedAtUtc,
        m.UpdatedAtUtc);
}
