using Autotrade.Trading.Application.Contract.Repositories;
using Autotrade.Trading.Domain.Entities;
using Autotrade.Trading.Domain.Shared.Enums;
using Autotrade.Trading.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;

namespace Autotrade.Trading.Infra.Data.Repositories;

/// <summary>
/// 持仓仓储 EF Core 实现。
/// </summary>
public sealed class EfPositionRepository : IPositionRepository
{
    private readonly TradingContext _context;

    public EfPositionRepository(TradingContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PositionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var position = await _context.Positions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return position is null ? null : ToDto(position);
    }

    public async Task<PositionDto?> GetByMarketAndOutcomeAsync(
        Guid tradingAccountId,
        string marketId,
        OutcomeSide outcome,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(marketId))
        {
            return null;
        }

        var position = await _context.Positions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.TradingAccountId == tradingAccountId && p.MarketId == marketId && p.Outcome == outcome,
                cancellationToken)
            .ConfigureAwait(false);

        return position is null ? null : ToDto(position);
    }

    public async Task<IReadOnlyList<PositionDto>> GetByTradingAccountIdAsync(
        Guid tradingAccountId,
        CancellationToken cancellationToken = default)
    {
        var positions = await _context.Positions
            .AsNoTracking()
            .Where(p => p.TradingAccountId == tradingAccountId)
            .OrderBy(p => p.MarketId)
            .ThenBy(p => p.Outcome)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return positions.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PositionDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var positions = await _context.Positions
            .AsNoTracking()
            .OrderBy(p => p.MarketId)
            .ThenBy(p => p.Outcome)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return positions.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PositionDto>> GetNonZeroAsync(CancellationToken cancellationToken = default)
    {
        var positions = await _context.Positions
            .AsNoTracking()
            .Where(p => p.Quantity.Value != 0)
            .OrderBy(p => p.MarketId)
            .ThenBy(p => p.Outcome)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return positions.Select(ToDto).ToList();
    }

    public async Task<PositionDto> GetOrCreateAsync(
        Guid tradingAccountId,
        string marketId,
        OutcomeSide outcome,
        CancellationToken cancellationToken = default)
    {
        if (tradingAccountId == Guid.Empty)
        {
            throw new ArgumentException("tradingAccountId cannot be empty.", nameof(tradingAccountId));
        }

        if (string.IsNullOrWhiteSpace(marketId))
        {
            throw new ArgumentException("marketId cannot be empty.", nameof(marketId));
        }

        var normalizedMarketId = marketId.Trim();

        var existing = await _context.Positions
            .FirstOrDefaultAsync(
                p => p.TradingAccountId == tradingAccountId && p.MarketId == normalizedMarketId && p.Outcome == outcome,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return ToDto(existing);
        }

        // 创建新持仓
        var position = new Position(tradingAccountId, normalizedMarketId, outcome);

        // 设置 Id（通过反射）
        var idProperty = typeof(NetDevPack.Domain.Entity).GetProperty("Id");
        idProperty?.SetValue(position, Guid.NewGuid());

        try
        {
            await _context.Positions.AddAsync(position, cancellationToken).ConfigureAwait(false);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToDto(position);
        }
        catch (DbUpdateException)
        {
            // 并发下可能同时创建同一持仓：依赖唯一索引，冲突时回读现有记录并返回。
            // 注：SaveChanges 失败后该 entity 仍可能处于 Added 状态，需清理以避免污染后续操作。
            _context.ChangeTracker.Clear();

            var existingAfterConflict = await _context.Positions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    p => p.TradingAccountId == tradingAccountId && p.MarketId == normalizedMarketId && p.Outcome == outcome,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existingAfterConflict is null)
            {
                throw;
            }

            return ToDto(existingAfterConflict);
        }
    }

    public async Task AddAsync(PositionDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var position = new Position(dto.TradingAccountId, dto.MarketId, dto.Outcome);

        // 设置 Id
        var idProperty = typeof(NetDevPack.Domain.Entity).GetProperty("Id");
        idProperty?.SetValue(position, dto.Id);

        // 应用数量和成本（通过 ApplyBuy）
        if (dto.Quantity > 0m)
        {
            position.ApplyBuy(
                new Domain.Shared.ValueObjects.Quantity(dto.Quantity),
                new Domain.Shared.ValueObjects.Price(dto.AverageCost));
        }

        await _context.Positions.AddAsync(position, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PositionDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var entity = await _context.Positions
            .FirstOrDefaultAsync(p => p.Id == dto.Id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new InvalidOperationException($"Position with ID {dto.Id} not found");
        }

        // 使用反射更新持仓字段（因为 Position 的属性是 private set）
        var quantityProp = typeof(Position).GetProperty("Quantity");
        var avgCostProp = typeof(Position).GetProperty("AverageCost");
        var realizedPnlProp = typeof(Position).GetProperty("RealizedPnl");
        var updatedAtProp = typeof(Position).GetProperty("UpdatedAtUtc");

        quantityProp?.SetValue(entity, new Domain.Shared.ValueObjects.Quantity(dto.Quantity));
        avgCostProp?.SetValue(entity, new Domain.Shared.ValueObjects.Price(dto.AverageCost));
        realizedPnlProp?.SetValue(entity, dto.RealizedPnl);
        updatedAtProp?.SetValue(entity, DateTimeOffset.UtcNow);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static PositionDto ToDto(Position p) => new(
        p.Id,
        p.TradingAccountId,
        p.MarketId,
        p.Outcome,
        p.Quantity.Value,
        p.AverageCost.Value,
        p.RealizedPnl,
        p.UpdatedAtUtc);
}
