using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.MarketData.Application.Tape;

public sealed class ClobRestMarketTapeGapRepairService : IMarketTapeGapRepairService
{
    public const string SourceName = "polymarket-clob-rest-gap-repair";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMarketCatalogReader _marketCatalog;
    private readonly IPolymarketClobClient _clobClient;
    private readonly IMarketTapeWriter _tapeWriter;
    private readonly MarketTapeGapRepairOptions _options;
    private readonly ILogger<ClobRestMarketTapeGapRepairService> _logger;

    public ClobRestMarketTapeGapRepairService(
        IMarketCatalogReader marketCatalog,
        IPolymarketClobClient clobClient,
        IMarketTapeWriter tapeWriter,
        IOptions<MarketTapeGapRepairOptions> options,
        ILogger<ClobRestMarketTapeGapRepairService> logger)
    {
        _marketCatalog = marketCatalog ?? throw new ArgumentNullException(nameof(marketCatalog));
        _clobClient = clobClient ?? throw new ArgumentNullException(nameof(clobClient));
        _tapeWriter = tapeWriter ?? throw new ArgumentNullException(nameof(tapeWriter));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MarketTapeGapRepairResult> RepairAsync(
        MarketTapeGapRepairRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _options.Validate();
        ValidateRequest(request);

        var observedAtUtc = (request.ObservedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var markets = SelectMarkets(request);
        var tokenResults = new List<MarketTapeGapRepairTokenResult>();

        foreach (var market in markets)
        {
            var tokenIds = SelectTokenIds(market, request);
            foreach (var tokenId in tokenIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tokenResults.Add(await RepairTokenAsync(market, tokenId, observedAtUtc, cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        return new MarketTapeGapRepairResult(
            observedAtUtc,
            markets.Count,
            tokenResults.Count,
            tokenResults.Count(result => result.Status == "Recorded"),
            tokenResults.Count(result => result.Status == "Failed"),
            tokenResults);
    }

    private async Task<MarketTapeGapRepairTokenResult> RepairTokenAsync(
        MarketInfoDto market,
        string tokenId,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await _clobClient.GetOrderBookAsync(tokenId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            var reason = result.Error?.Message ?? $"CLOB book request failed with status {result.StatusCode}.";
            _logger.LogWarning(
                "CLOB REST tape gap repair skipped token {TokenId} for market {MarketId}: {Reason}",
                tokenId,
                market.MarketId,
                reason);
            return Failed(market.MarketId, market.ConditionId, tokenId, reason);
        }

        var book = result.Data;
        var timestampUtc = ParseTimestamp(book.Timestamp, observedAtUtc);
        var tapeMarketId = ResolveTapeMarketId(market, book);
        var bookTokenId = string.IsNullOrWhiteSpace(book.AssetId) ? tokenId : book.AssetId.Trim();
        var bids = NormalizeLevels(book.Bids, isBid: true);
        var asks = NormalizeLevels(book.Asks, isBid: false);
        var bestBid = bids.FirstOrDefault();
        var bestAsk = asks.FirstOrDefault();
        var rawJson = JsonSerializer.Serialize(book, JsonOptions);
        var sourceSequence = string.IsNullOrWhiteSpace(book.Hash)
            ? BuildSnapshotHash(tapeMarketId, bookTokenId, bids, asks)
            : book.Hash.Trim();

        await _tapeWriter.AppendOrderBookDepthSnapshotsAsync(
                new[]
                {
                    new OrderBookDepthSnapshotDto(
                        Guid.Empty,
                        tapeMarketId,
                        bookTokenId,
                        timestampUtc,
                        sourceSequence,
                        bids,
                        asks,
                        SourceName,
                        rawJson,
                        observedAtUtc)
                },
                cancellationToken)
            .ConfigureAwait(false);

        await _tapeWriter.AppendOrderBookTopTicksAsync(
                new[]
                {
                    new OrderBookTopTickDto(
                        Guid.Empty,
                        tapeMarketId,
                        bookTokenId,
                        timestampUtc,
                        bestBid?.Price,
                        bestBid?.Size,
                        bestAsk?.Price,
                        bestAsk?.Size,
                        bestBid is not null && bestAsk is not null ? bestAsk.Price - bestBid.Price : null,
                        SourceName,
                        sourceSequence,
                        rawJson,
                        observedAtUtc)
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new MarketTapeGapRepairTokenResult(
            market.MarketId,
            tapeMarketId,
            bookTokenId,
            "Recorded",
            sourceSequence,
            timestampUtc,
            null);
    }

    private IReadOnlyList<MarketInfoDto> SelectMarkets(MarketTapeGapRepairRequest request)
    {
        var maxMarkets = request.MaxMarkets ?? _options.MaxMarketsPerRun;
        if (!string.IsNullOrWhiteSpace(request.MarketId))
        {
            var market = _marketCatalog.GetMarket(request.MarketId.Trim());
            return market is null ? Array.Empty<MarketInfoDto>() : new[] { market };
        }

        var minVolume = request.MinVolume24h ?? _options.MinVolume24h;
        var minLiquidity = request.MinLiquidity ?? _options.MinLiquidity;

        return _marketCatalog.GetActiveMarkets()
            .Where(market => market.TokenIds.Count > 0)
            .Where(market => market.Volume24h >= minVolume)
            .Where(market => market.Liquidity >= minLiquidity)
            .OrderByDescending(market => market.Volume24h)
            .ThenByDescending(market => market.Liquidity)
            .ThenBy(market => market.MarketId, StringComparer.Ordinal)
            .Take(Math.Max(1, maxMarkets))
            .ToArray();
    }

    private IReadOnlyList<string> SelectTokenIds(
        MarketInfoDto market,
        MarketTapeGapRepairRequest request)
    {
        var maxTokens = request.MaxTokensPerMarket ?? _options.MaxTokensPerMarket;
        var tokens = request.TokenIds is { Count: > 0 }
            ? request.TokenIds
            : market.TokenIds;

        return tokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(1, maxTokens))
            .ToArray();
    }

    private IReadOnlyList<OrderBookDepthLevelDto> NormalizeLevels(
        IEnumerable<OrderSummary> levels,
        bool isBid)
    {
        var ordered = levels
            .Select(level => TryCreateLevel(level, isBid))
            .Where(level => level is not null)
            .Select(level => level!)
            .Where(level => level.Price >= 0m && level.Price <= 1m && level.Size > 0m);

        ordered = isBid
            ? ordered.OrderByDescending(level => level.Price)
            : ordered.OrderBy(level => level.Price);

        return ordered
            .Take(_options.MaxDepthLevels)
            .ToArray();
    }

    private static OrderBookDepthLevelDto? TryCreateLevel(OrderSummary level, bool isBid)
    {
        if (!TryParseDecimal(level.Price, out var price) || !TryParseDecimal(level.Size, out var size))
        {
            return null;
        }

        return new OrderBookDepthLevelDto(price, size, isBid);
    }

    private static bool TryParseDecimal(string value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static DateTimeOffset ParseTimestamp(string? timestamp, DateTimeOffset fallback)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
        {
            return fallback;
        }

        if (long.TryParse(timestamp.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
                : DateTimeOffset.FromUnixTimeSeconds(numeric);
        }

        return DateTimeOffset.TryParse(
            timestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : fallback;
    }

    private static string ResolveTapeMarketId(MarketInfoDto market, OrderBookSummary book)
    {
        if (!string.IsNullOrWhiteSpace(book.Market))
        {
            return book.Market.Trim();
        }

        return !string.IsNullOrWhiteSpace(market.ConditionId)
            ? market.ConditionId.Trim()
            : market.MarketId.Trim();
    }

    private static string BuildSnapshotHash(
        string marketId,
        string tokenId,
        IReadOnlyList<OrderBookDepthLevelDto> bids,
        IReadOnlyList<OrderBookDepthLevelDto> asks)
    {
        var payload = JsonSerializer.Serialize(new
        {
            marketId,
            tokenId,
            bids,
            asks
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static void ValidateRequest(MarketTapeGapRepairRequest request)
    {
        if (request.TokenIds is { Count: > 0 } && string.IsNullOrWhiteSpace(request.MarketId))
        {
            throw new ArgumentException("TokenIds can only be supplied with a MarketId.", nameof(request));
        }

        if (request.MaxMarkets is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxMarkets must be positive.");
        }

        if (request.MaxTokensPerMarket is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxTokensPerMarket must be positive.");
        }

        if (request.MinVolume24h is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MinVolume24h cannot be negative.");
        }

        if (request.MinLiquidity is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MinLiquidity cannot be negative.");
        }
    }

    private static MarketTapeGapRepairTokenResult Failed(
        string catalogMarketId,
        string tapeMarketId,
        string tokenId,
        string reason)
        => new(
            catalogMarketId,
            tapeMarketId,
            tokenId,
            "Failed",
            null,
            null,
            reason);
}
