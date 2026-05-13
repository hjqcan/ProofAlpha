using System.Text.Json;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.MarketData.Domain.Entities;
using Autotrade.MarketData.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Autotrade.MarketData.Infra.Data.Repositories;

public sealed class EfMarketTapeRepository :
    IMarketTapeWriter,
    IMarketTapeReader,
    IMarketReplayReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const int MaxReplayLimit = 50000;

    private readonly MarketDataContext _context;

    public EfMarketTapeRepository(MarketDataContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AppendMarketPriceTicksAsync(
        IReadOnlyList<MarketPriceTickDto> ticks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        if (ticks.Count == 0)
        {
            return;
        }

        var entities = new List<MarketPriceTick>();
        foreach (var tick in DeduplicatePriceTicks(ticks))
        {
            if (!await PriceTickExistsAsync(tick, cancellationToken).ConfigureAwait(false))
            {
                entities.Add(ToEntity(tick));
            }
        }

        await AddRangeAndSaveAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendOrderBookTopTicksAsync(
        IReadOnlyList<OrderBookTopTickDto> ticks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        if (ticks.Count == 0)
        {
            return;
        }

        var entities = new List<OrderBookTopTick>();
        foreach (var tick in DeduplicateTopTicks(ticks))
        {
            if (!await TopTickExistsAsync(tick, cancellationToken).ConfigureAwait(false))
            {
                entities.Add(ToEntity(tick));
            }
        }

        await AddRangeAndSaveAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendOrderBookDepthSnapshotsAsync(
        IReadOnlyList<OrderBookDepthSnapshotDto> snapshots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count == 0)
        {
            return;
        }

        var entities = new List<OrderBookDepthSnapshot>();
        foreach (var snapshot in DeduplicateDepthSnapshots(snapshots))
        {
            var exists = await _context.OrderBookDepthSnapshots.AnyAsync(
                    item => item.MarketId == snapshot.MarketId
                        && item.TokenId == snapshot.TokenId
                        && item.SnapshotHash == snapshot.SnapshotHash
                        && item.TimestampUnixMilliseconds == snapshot.TimestampUtc.ToUnixTimeMilliseconds(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
            {
                entities.Add(ToEntity(snapshot));
            }
        }

        await AddRangeAndSaveAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendClobTradeTicksAsync(
        IReadOnlyList<ClobTradeTickDto> ticks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        if (ticks.Count == 0)
        {
            return;
        }

        var entities = new List<ClobTradeTick>();
        foreach (var tick in DeduplicateTradeTicks(ticks))
        {
            var exists = await _context.ClobTradeTicks.AnyAsync(
                    item => item.MarketId == tick.MarketId
                        && item.TokenId == tick.TokenId
                        && item.ExchangeTradeId == tick.ExchangeTradeId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
            {
                entities.Add(ToEntity(tick));
            }
        }

        await AddRangeAndSaveAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendMarketResolutionEventsAsync(
        IReadOnlyList<MarketResolutionEventDto> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        var entities = new List<MarketResolutionEvent>();
        foreach (var item in DeduplicateResolutionEvents(events))
        {
            var exists = await _context.MarketResolutionEvents.AnyAsync(
                    existing => existing.MarketId == item.MarketId
                        && existing.ResolvedUnixMilliseconds == item.ResolvedAtUtc.ToUnixTimeMilliseconds()
                        && existing.Outcome == item.Outcome
                        && existing.SourceName == item.SourceName,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
            {
                entities.Add(ToEntity(item));
            }
        }

        await AddRangeAndSaveAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrderBookTopTickDto>> GetTopTicksAsync(
        MarketTapeQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(query);
        var dbQuery = ApplyCommonFilters(_context.OrderBookTopTicks.AsNoTracking(), normalized);

        return await dbQuery
            .OrderBy(item => item.TimestampUnixMilliseconds)
            .ThenBy(item => item.Id)
            .Take(normalized.Limit)
            .Select(item => ToDto(item))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrderBookDepthSnapshotDto>> GetDepthSnapshotsAsync(
        MarketTapeQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(query);
        var dbQuery = ApplyCommonFilters(_context.OrderBookDepthSnapshots.AsNoTracking(), normalized);

        var snapshots = await dbQuery
            .OrderBy(item => item.TimestampUnixMilliseconds)
            .ThenBy(item => item.Id)
            .Take(normalized.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return snapshots.Select(ToDto).ToList();
    }

    public async Task<MarketTapeReplaySlice> GetReplaySliceAsync(
        MarketTapeQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(query);
        var notes = BuildReplayNotes(query, normalized);

        var priceTask = LoadPriceTicksAsync(normalized, cancellationToken);
        var topTask = GetTopTicksAsync(normalized, cancellationToken);
        var depthTask = GetDepthSnapshotsAsync(normalized, cancellationToken);
        var tradeTask = LoadTradeTicksAsync(normalized, cancellationToken);
        var resolutionTask = LoadResolutionEventsAsync(normalized, cancellationToken);

        await Task.WhenAll(priceTask, topTask, depthTask, tradeTask, resolutionTask).ConfigureAwait(false);

        return new MarketTapeReplaySlice(
            normalized,
            await priceTask.ConfigureAwait(false),
            await topTask.ConfigureAwait(false),
            await depthTask.ConfigureAwait(false),
            await tradeTask.ConfigureAwait(false),
            await resolutionTask.ConfigureAwait(false),
            notes);
    }

    private async Task<IReadOnlyList<MarketPriceTickDto>> LoadPriceTicksAsync(
        MarketTapeQuery query,
        CancellationToken cancellationToken)
    {
        var dbQuery = ApplyCommonFilters(_context.MarketPriceTicks.AsNoTracking(), query);
        return await dbQuery
            .OrderBy(item => item.TimestampUnixMilliseconds)
            .ThenBy(item => item.Id)
            .Take(query.Limit)
            .Select(item => ToDto(item))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ClobTradeTickDto>> LoadTradeTicksAsync(
        MarketTapeQuery query,
        CancellationToken cancellationToken)
    {
        var dbQuery = ApplyCommonFilters(_context.ClobTradeTicks.AsNoTracking(), query);
        return await dbQuery
            .OrderBy(item => item.TimestampUnixMilliseconds)
            .ThenBy(item => item.Id)
            .Take(query.Limit)
            .Select(item => ToDto(item))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MarketResolutionEventDto>> LoadResolutionEventsAsync(
        MarketTapeQuery query,
        CancellationToken cancellationToken)
    {
        var dbQuery = _context.MarketResolutionEvents
            .AsNoTracking()
            .Where(item => item.MarketId == query.MarketId);
        if (query.FromUtc.HasValue)
        {
            var fromUnix = query.FromUtc.Value.ToUnixTimeMilliseconds();
            dbQuery = dbQuery.Where(item => item.ResolvedUnixMilliseconds >= fromUnix);
        }

        if (query.ToUtc.HasValue)
        {
            var toUnix = query.ToUtc.Value.ToUnixTimeMilliseconds();
            dbQuery = dbQuery.Where(item => item.ResolvedUnixMilliseconds <= toUnix);
        }

        if (query.AsOfUtc.HasValue)
        {
            var asOfUnix = query.AsOfUtc.Value.ToUnixTimeMilliseconds();
            dbQuery = dbQuery.Where(item => item.ResolvedUnixMilliseconds <= asOfUnix);
        }

        return await dbQuery
            .OrderBy(item => item.ResolvedUnixMilliseconds)
            .ThenBy(item => item.Id)
            .Take(query.Limit)
            .Select(item => ToDto(item))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static IQueryable<T> ApplyCommonFilters<T>(IQueryable<T> query, MarketTapeQuery tapeQuery)
        where T : class
    {
        query = query.Where(item => EF.Property<string>(item, "MarketId") == tapeQuery.MarketId);
        if (!string.IsNullOrWhiteSpace(tapeQuery.TokenId))
        {
            query = query.Where(item => EF.Property<string>(item, "TokenId") == tapeQuery.TokenId);
        }

        if (tapeQuery.FromUtc.HasValue)
        {
            var fromUnix = tapeQuery.FromUtc.Value.ToUnixTimeMilliseconds();
            query = query.Where(item => EF.Property<long>(item, "TimestampUnixMilliseconds") >= fromUnix);
        }

        if (tapeQuery.ToUtc.HasValue)
        {
            var toUnix = tapeQuery.ToUtc.Value.ToUnixTimeMilliseconds();
            query = query.Where(item => EF.Property<long>(item, "TimestampUnixMilliseconds") <= toUnix);
        }

        if (tapeQuery.AsOfUtc.HasValue)
        {
            var asOfUnix = tapeQuery.AsOfUtc.Value.ToUnixTimeMilliseconds();
            query = query.Where(item => EF.Property<long>(item, "TimestampUnixMilliseconds") <= asOfUnix);
        }

        return query;
    }

    private async Task AddRangeAndSaveAsync<TEntity>(
        IReadOnlyList<TEntity> entities,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (entities.Count == 0)
        {
            return;
        }

        _context.Set<TEntity>().AddRange(entities);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PriceTickExistsAsync(MarketPriceTickDto tick, CancellationToken cancellationToken)
    {
        var query = _context.MarketPriceTicks.AsNoTracking()
            .Where(item => item.MarketId == tick.MarketId
                && item.TokenId == tick.TokenId
                && item.TimestampUnixMilliseconds == tick.TimestampUtc.ToUnixTimeMilliseconds()
                && item.SourceName == tick.SourceName);

        query = string.IsNullOrWhiteSpace(tick.SourceSequence)
            ? query.Where(item => item.SourceSequence == null
                && item.Price == tick.Price
                && item.Size == tick.Size)
            : query.Where(item => item.SourceSequence == tick.SourceSequence);

        return await query.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TopTickExistsAsync(OrderBookTopTickDto tick, CancellationToken cancellationToken)
    {
        var query = _context.OrderBookTopTicks.AsNoTracking()
            .Where(item => item.MarketId == tick.MarketId
                && item.TokenId == tick.TokenId
                && item.TimestampUnixMilliseconds == tick.TimestampUtc.ToUnixTimeMilliseconds()
                && item.SourceName == tick.SourceName);

        query = string.IsNullOrWhiteSpace(tick.SourceSequence)
            ? query.Where(item => item.SourceSequence == null
                && item.BestBidPrice == tick.BestBidPrice
                && item.BestBidSize == tick.BestBidSize
                && item.BestAskPrice == tick.BestAskPrice
                && item.BestAskSize == tick.BestAskSize)
            : query.Where(item => item.SourceSequence == tick.SourceSequence);

        return await query.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    private static MarketTapeQuery Normalize(MarketTapeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.MarketId))
        {
            throw new ArgumentException("MarketId cannot be empty.", nameof(query));
        }

        var toUtc = query.ToUtc;
        if (query.AsOfUtc.HasValue && (!toUtc.HasValue || toUtc.Value > query.AsOfUtc.Value))
        {
            toUtc = query.AsOfUtc.Value;
        }

        return query with
        {
            MarketId = query.MarketId.Trim(),
            TokenId = string.IsNullOrWhiteSpace(query.TokenId) ? null : query.TokenId.Trim(),
            FromUtc = query.FromUtc?.ToUniversalTime(),
            ToUtc = toUtc?.ToUniversalTime(),
            AsOfUtc = query.AsOfUtc?.ToUniversalTime(),
            Limit = Math.Clamp(query.Limit, 1, MaxReplayLimit)
        };
    }

    private static IReadOnlyList<string> BuildReplayNotes(MarketTapeQuery original, MarketTapeQuery normalized)
    {
        var notes = new List<string>();
        if (original.AsOfUtc.HasValue && original.ToUtc.HasValue && original.ToUtc.Value > original.AsOfUtc.Value)
        {
            notes.Add($"Replay toUtc was clamped to asOfUtc {normalized.AsOfUtc:O}.");
        }

        if (normalized.FromUtc.HasValue && normalized.ToUtc.HasValue && normalized.FromUtc.Value > normalized.ToUtc.Value)
        {
            notes.Add("Replay window is empty because fromUtc is after the effective toUtc.");
        }

        return notes;
    }

    private static IEnumerable<MarketPriceTickDto> DeduplicatePriceTicks(IEnumerable<MarketPriceTickDto> ticks)
        => ticks.GroupBy(
                tick => new
                {
                    tick.MarketId,
                    tick.TokenId,
                    tick.TimestampUtc,
                    tick.Price,
                    tick.Size,
                    tick.SourceName,
                    tick.SourceSequence
                })
            .Select(group => group.First());

    private static IEnumerable<OrderBookTopTickDto> DeduplicateTopTicks(IEnumerable<OrderBookTopTickDto> ticks)
        => ticks.GroupBy(
                tick => new
                {
                    tick.MarketId,
                    tick.TokenId,
                    tick.TimestampUtc,
                    tick.BestBidPrice,
                    tick.BestBidSize,
                    tick.BestAskPrice,
                    tick.BestAskSize,
                    tick.SourceName,
                    tick.SourceSequence
                })
            .Select(group => group.First());

    private static IEnumerable<OrderBookDepthSnapshotDto> DeduplicateDepthSnapshots(
        IEnumerable<OrderBookDepthSnapshotDto> snapshots)
        => snapshots.GroupBy(
                snapshot => new
                {
                    snapshot.MarketId,
                    snapshot.TokenId,
                    snapshot.TimestampUtc,
                    snapshot.SnapshotHash
                })
            .Select(group => group.First());

    private static IEnumerable<ClobTradeTickDto> DeduplicateTradeTicks(IEnumerable<ClobTradeTickDto> ticks)
        => ticks.GroupBy(tick => new { tick.MarketId, tick.TokenId, tick.ExchangeTradeId })
            .Select(group => group.First());

    private static IEnumerable<MarketResolutionEventDto> DeduplicateResolutionEvents(
        IEnumerable<MarketResolutionEventDto> events)
        => events.GroupBy(item => new { item.MarketId, item.ResolvedAtUtc, item.Outcome, item.SourceName })
            .Select(group => group.First());

    private static MarketPriceTick ToEntity(MarketPriceTickDto dto)
    {
        var entity = new MarketPriceTick(
            dto.MarketId,
            dto.TokenId,
            dto.TimestampUtc,
            dto.Price,
            dto.Size,
            dto.SourceName,
            dto.SourceSequence,
            dto.RawJson,
            dto.CreatedAtUtc);
        return WithId(entity, dto.Id);
    }

    private static OrderBookTopTick ToEntity(OrderBookTopTickDto dto)
    {
        var entity = new OrderBookTopTick(
            dto.MarketId,
            dto.TokenId,
            dto.TimestampUtc,
            dto.BestBidPrice,
            dto.BestBidSize,
            dto.BestAskPrice,
            dto.BestAskSize,
            dto.SourceName,
            dto.SourceSequence,
            dto.RawJson,
            dto.CreatedAtUtc);
        return WithId(entity, dto.Id);
    }

    private static OrderBookDepthSnapshot ToEntity(OrderBookDepthSnapshotDto dto)
    {
        var entity = new OrderBookDepthSnapshot(
            dto.MarketId,
            dto.TokenId,
            dto.TimestampUtc,
            dto.SnapshotHash,
            JsonSerializer.Serialize(dto.Bids, JsonOptions),
            JsonSerializer.Serialize(dto.Asks, JsonOptions),
            dto.SourceName,
            dto.RawJson,
            dto.CreatedAtUtc);
        return WithId(entity, dto.Id);
    }

    private static ClobTradeTick ToEntity(ClobTradeTickDto dto)
    {
        var entity = new ClobTradeTick(
            dto.MarketId,
            dto.TokenId,
            dto.ExchangeTradeId,
            dto.TimestampUtc,
            dto.Price,
            dto.Size,
            dto.Side,
            dto.FeeRateBps,
            dto.SourceName,
            dto.RawJson,
            dto.CreatedAtUtc);
        return WithId(entity, dto.Id);
    }

    private static MarketResolutionEvent ToEntity(MarketResolutionEventDto dto)
    {
        var entity = new MarketResolutionEvent(
            dto.MarketId,
            dto.ResolvedAtUtc,
            dto.Outcome,
            dto.SourceName,
            dto.RawJson,
            dto.CreatedAtUtc);
        return WithId(entity, dto.Id);
    }

    private static TEntity WithId<TEntity>(TEntity entity, Guid id)
        where TEntity : NetDevPack.Domain.Entity
    {
        if (id != Guid.Empty)
        {
            entity.Id = id;
        }

        return entity;
    }

    private static MarketPriceTickDto ToDto(MarketPriceTick tick)
        => new(
            tick.Id,
            tick.MarketId,
            tick.TokenId,
            tick.TimestampUtc,
            tick.Price,
            tick.Size,
            tick.SourceName,
            tick.SourceSequence,
            tick.RawJson,
            tick.CreatedAtUtc);

    private static OrderBookTopTickDto ToDto(OrderBookTopTick tick)
        => new(
            tick.Id,
            tick.MarketId,
            tick.TokenId,
            tick.TimestampUtc,
            tick.BestBidPrice,
            tick.BestBidSize,
            tick.BestAskPrice,
            tick.BestAskSize,
            tick.Spread,
            tick.SourceName,
            tick.SourceSequence,
            tick.RawJson,
            tick.CreatedAtUtc);

    private static OrderBookDepthSnapshotDto ToDto(OrderBookDepthSnapshot snapshot)
        => new(
            snapshot.Id,
            snapshot.MarketId,
            snapshot.TokenId,
            snapshot.TimestampUtc,
            snapshot.SnapshotHash,
            DeserializeDepth(snapshot.BidsJson),
            DeserializeDepth(snapshot.AsksJson),
            snapshot.SourceName,
            snapshot.RawJson,
            snapshot.CreatedAtUtc);

    private static ClobTradeTickDto ToDto(ClobTradeTick tick)
        => new(
            tick.Id,
            tick.MarketId,
            tick.TokenId,
            tick.ExchangeTradeId,
            tick.TimestampUtc,
            tick.Price,
            tick.Size,
            tick.Side,
            tick.FeeRateBps,
            tick.SourceName,
            tick.RawJson,
            tick.CreatedAtUtc);

    private static MarketResolutionEventDto ToDto(MarketResolutionEvent item)
        => new(
            item.Id,
            item.MarketId,
            item.ResolvedAtUtc,
            item.Outcome,
            item.SourceName,
            item.RawJson,
            item.CreatedAtUtc);

    private static IReadOnlyList<OrderBookDepthLevelDto> DeserializeDepth(string json)
        => string.IsNullOrWhiteSpace(json)
            ? Array.Empty<OrderBookDepthLevelDto>()
            : JsonSerializer.Deserialize<IReadOnlyList<OrderBookDepthLevelDto>>(json, JsonOptions)
              ?? Array.Empty<OrderBookDepthLevelDto>();
}
