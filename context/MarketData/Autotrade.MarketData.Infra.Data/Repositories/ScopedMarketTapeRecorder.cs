using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Autotrade.MarketData.Infra.Data.Repositories;

public sealed class ScopedMarketTapeRecorder(
    IServiceScopeFactory scopeFactory,
    ILogger<ScopedMarketTapeRecorder> logger) : IMarketTapeRecorder
{
    private const string SourceName = "polymarket-clob-ws";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordBookEventAsync(
        ClobBookEvent bookEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bookEvent);
        try
        {
            var timestamp = ParseTimestamp(bookEvent.Timestamp);
            var bestBid = bookEvent.Bids
                .Where(level => level.SizeDecimal > 0m)
                .OrderByDescending(level => level.PriceDecimal)
                .FirstOrDefault();
            var bestAsk = bookEvent.Asks
                .Where(level => level.SizeDecimal > 0m)
                .OrderBy(level => level.PriceDecimal)
                .FirstOrDefault();
            var rawJson = JsonSerializer.Serialize(bookEvent, JsonOptions);

            using var scope = scopeFactory.CreateScope();
            var writer = scope.ServiceProvider.GetRequiredService<IMarketTapeWriter>();
            await writer.AppendOrderBookDepthSnapshotsAsync(
                    new[]
                    {
                        new OrderBookDepthSnapshotDto(
                            Guid.Empty,
                            bookEvent.Market,
                            bookEvent.AssetId,
                            timestamp,
                            bookEvent.Hash,
                            bookEvent.Bids.Select(level => new OrderBookDepthLevelDto(
                                level.PriceDecimal,
                                level.SizeDecimal,
                                IsBid: true)).ToArray(),
                            bookEvent.Asks.Select(level => new OrderBookDepthLevelDto(
                                level.PriceDecimal,
                                level.SizeDecimal,
                                IsBid: false)).ToArray(),
                            SourceName,
                            rawJson,
                            DateTimeOffset.UtcNow)
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            await writer.AppendOrderBookTopTicksAsync(
                    new[]
                    {
                        new OrderBookTopTickDto(
                            Guid.Empty,
                            bookEvent.Market,
                            bookEvent.AssetId,
                            timestamp,
                            bestBid?.PriceDecimal,
                            bestBid?.SizeDecimal,
                            bestAsk?.PriceDecimal,
                            bestAsk?.SizeDecimal,
                            bestBid is not null && bestAsk is not null ? bestAsk.PriceDecimal - bestBid.PriceDecimal : null,
                            SourceName,
                            bookEvent.Hash,
                            rawJson,
                            DateTimeOffset.UtcNow)
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record CLOB book event to durable market tape for {AssetId}", bookEvent.AssetId);
        }
    }

    public async Task RecordPriceChangeEventAsync(
        ClobPriceChangeEvent priceChangeEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(priceChangeEvent);
        try
        {
            var timestamp = ParseTimestamp(priceChangeEvent.Timestamp);
            var rawJson = JsonSerializer.Serialize(priceChangeEvent, JsonOptions);
            var ticks = priceChangeEvent.PriceChanges
                .Select(change => new OrderBookTopTickDto(
                    Guid.Empty,
                    priceChangeEvent.Market,
                    change.AssetId,
                    timestamp,
                    change.BestBidDecimal > 0m ? change.BestBidDecimal : null,
                    null,
                    change.BestAskDecimal is > 0m and < 1m ? change.BestAskDecimal : null,
                    null,
                    change.BestBidDecimal > 0m && change.BestAskDecimal is > 0m and < 1m
                        ? change.BestAskDecimal - change.BestBidDecimal
                        : null,
                    SourceName,
                    change.Hash,
                    rawJson,
                    DateTimeOffset.UtcNow))
                .ToArray();
            if (ticks.Length == 0)
            {
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var writer = scope.ServiceProvider.GetRequiredService<IMarketTapeWriter>();
            await writer.AppendOrderBookTopTicksAsync(ticks, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record CLOB price change event to durable market tape for {Market}", priceChangeEvent.Market);
        }
    }

    public async Task RecordLastTradePriceEventAsync(
        ClobLastTradePriceEvent tradeEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tradeEvent);
        try
        {
            var timestamp = ParseTimestamp(tradeEvent.Timestamp);
            var rawJson = JsonSerializer.Serialize(tradeEvent, JsonOptions);
            var trade = new ClobTradeTickDto(
                Guid.Empty,
                tradeEvent.Market,
                tradeEvent.AssetId,
                BuildSyntheticTradeId(tradeEvent),
                timestamp,
                tradeEvent.PriceDecimal,
                tradeEvent.SizeDecimal,
                tradeEvent.Side,
                ParseDecimalOrNull(tradeEvent.FeeRateBps),
                SourceName,
                rawJson,
                DateTimeOffset.UtcNow);

            using var scope = scopeFactory.CreateScope();
            var writer = scope.ServiceProvider.GetRequiredService<IMarketTapeWriter>();
            await writer.AppendClobTradeTicksAsync(new[] { trade }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record CLOB last trade event to durable market tape for {AssetId}", tradeEvent.AssetId);
        }
    }

    private static DateTimeOffset ParseTimestamp(string timestamp)
    {
        if (long.TryParse(timestamp, out var numeric))
        {
            return numeric > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
                : DateTimeOffset.FromUnixTimeSeconds(numeric);
        }

        return DateTimeOffset.TryParse(timestamp, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.UtcNow;
    }

    private static decimal? ParseDecimalOrNull(string value)
        => decimal.TryParse(value, out var parsed) ? parsed : null;

    private static string BuildSyntheticTradeId(ClobLastTradePriceEvent tradeEvent)
    {
        var identity = string.Join(
            "|",
            tradeEvent.Market,
            tradeEvent.AssetId,
            tradeEvent.Timestamp,
            tradeEvent.Price,
            tradeEvent.Size,
            tradeEvent.Side);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
}
