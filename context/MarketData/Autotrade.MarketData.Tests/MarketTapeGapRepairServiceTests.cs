using System.Globalization;
using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.MarketData.Application.Tape;
using Autotrade.MarketData.Infra.Data.Repositories;
using Autotrade.Polymarket.Abstractions;
using Autotrade.Polymarket.Models;
using Autotrade.Testing.Db;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autotrade.MarketData.Tests;

public sealed class MarketTapeGapRepairServiceTests
{
    [Fact]
    public async Task RepairAsync_PollsActiveMarketBooksAndPersistsTopAndDepthSnapshots()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var context = TestDbContextFactory.CreateMarketDataContextSqlite(db.Connection);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfMarketTapeRepository(context);
        var observedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var catalog = new FakeMarketCatalogReader(
        [
            Market("gamma-market-1", "condition-1", 10_000m, 5_000m, ["yes-token", "no-token"]),
            Market("low-volume-market", "condition-low", 1m, 5_000m, ["low-token"])
        ]);
        var clobClient = new FakeClobClient();
        clobClient.Books["yes-token"] = PolymarketApiResult<OrderBookSummary>.Success(
            200,
            Book(
                "condition-1",
                "yes-token",
                observedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                "hash-yes",
                bids: [("0.49", "12"), ("0.48", "20")],
                asks: [("0.51", "13"), ("0.52", "14")]));
        clobClient.Books["no-token"] = PolymarketApiResult<OrderBookSummary>.Success(
            200,
            Book(
                "condition-1",
                "no-token",
                observedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                "hash-no",
                bids: [("0.44", "21")],
                asks: [("0.56", "22")]));
        var service = CreateService(catalog, clobClient, repository);

        var result = await service.RepairAsync(
            new MarketTapeGapRepairRequest(ObservedAtUtc: observedAt),
            CancellationToken.None);

        Assert.Equal(1, result.MarketsExamined);
        Assert.Equal(2, result.TokensRequested);
        Assert.Equal(2, result.TokensRecorded);
        Assert.Equal(0, result.TokensFailed);
        Assert.Equal(["yes-token", "no-token"], clobClient.RequestedTokenIds);

        var topTicks = await repository.GetTopTicksAsync(
            new MarketTapeQuery("condition-1"),
            CancellationToken.None);
        Assert.Equal(2, topTicks.Count);
        Assert.Contains(topTicks, tick =>
            tick.TokenId == "yes-token" &&
            tick.BestBidPrice == 0.49m &&
            tick.BestBidSize == 12m &&
            tick.BestAskPrice == 0.51m &&
            tick.BestAskSize == 13m &&
            tick.SourceName == ClobRestMarketTapeGapRepairService.SourceName);

        var depthSnapshots = await repository.GetDepthSnapshotsAsync(
            new MarketTapeQuery("condition-1", "yes-token"),
            CancellationToken.None);
        var yesDepth = Assert.Single(depthSnapshots);
        Assert.Equal("hash-yes", yesDepth.SnapshotHash);
        Assert.Equal(0.49m, yesDepth.Bids[0].Price);
        Assert.Equal(0.51m, yesDepth.Asks[0].Price);
    }

    [Fact]
    public async Task RepairAsync_ContinuesAfterPerTokenBookFailure()
    {
        await using var db = new SqliteInMemoryDatabase();
        await using var context = TestDbContextFactory.CreateMarketDataContextSqlite(db.Connection);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfMarketTapeRepository(context);
        var observedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var catalog = new FakeMarketCatalogReader(
        [
            Market("gamma-market-1", "condition-1", 10_000m, 5_000m, ["yes-token", "no-token"])
        ]);
        var clobClient = new FakeClobClient();
        clobClient.Books["yes-token"] = PolymarketApiResult<OrderBookSummary>.Success(
            200,
            Book(
                "condition-1",
                "yes-token",
                observedAt.ToString("O", CultureInfo.InvariantCulture),
                "hash-yes",
                bids: [("0.49", "12")],
                asks: [("0.51", "13")]));
        clobClient.Books["no-token"] = PolymarketApiResult<OrderBookSummary>.Failure(
            503,
            "service unavailable",
            rawBody: null);
        var service = CreateService(catalog, clobClient, repository);

        var result = await service.RepairAsync(
            new MarketTapeGapRepairRequest(ObservedAtUtc: observedAt),
            CancellationToken.None);

        Assert.Equal(2, result.TokensRequested);
        Assert.Equal(1, result.TokensRecorded);
        Assert.Equal(1, result.TokensFailed);
        Assert.Contains(result.Tokens, token => token.TokenId == "no-token" && token.Status == "Failed");

        var topTicks = await repository.GetTopTicksAsync(
            new MarketTapeQuery("condition-1"),
            CancellationToken.None);
        var tick = Assert.Single(topTicks);
        Assert.Equal("yes-token", tick.TokenId);
    }

    private static ClobRestMarketTapeGapRepairService CreateService(
        IMarketCatalogReader catalog,
        IPolymarketClobClient clobClient,
        IMarketTapeWriter writer)
        => new(
            catalog,
            clobClient,
            writer,
            Options.Create(new MarketTapeGapRepairOptions
            {
                Enabled = true,
                MaxMarketsPerRun = 10,
                MaxTokensPerMarket = 2,
                MaxDepthLevels = 5,
                MinVolume24h = 100m,
                MinLiquidity = 100m
            }),
            NullLogger<ClobRestMarketTapeGapRepairService>.Instance);

    private static MarketInfoDto Market(
        string marketId,
        string conditionId,
        decimal volume24h,
        decimal liquidity,
        IReadOnlyList<string> tokenIds)
        => new()
        {
            MarketId = marketId,
            ConditionId = conditionId,
            Name = marketId,
            Status = "Active",
            Volume24h = volume24h,
            Liquidity = liquidity,
            TokenIds = tokenIds
        };

    private static OrderBookSummary Book(
        string market,
        string assetId,
        string timestamp,
        string hash,
        IReadOnlyList<(string Price, string Size)> bids,
        IReadOnlyList<(string Price, string Size)> asks)
        => new()
        {
            Market = market,
            AssetId = assetId,
            Timestamp = timestamp,
            Hash = hash,
            Bids = bids.Select(level => new OrderSummary { Price = level.Price, Size = level.Size }).ToList(),
            Asks = asks.Select(level => new OrderSummary { Price = level.Price, Size = level.Size }).ToList()
        };

    private sealed class FakeMarketCatalogReader : IMarketCatalogReader
    {
        private readonly IReadOnlyList<MarketInfoDto> _markets;

        public FakeMarketCatalogReader(IReadOnlyList<MarketInfoDto> markets)
        {
            _markets = markets;
        }

        public MarketInfoDto? GetMarket(string marketId)
            => _markets.FirstOrDefault(market =>
                string.Equals(market.MarketId, marketId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(market.ConditionId, marketId, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<MarketInfoDto> GetAllMarkets() => _markets;

        public IReadOnlyList<MarketInfoDto> GetActiveMarkets() => _markets;

        public IReadOnlyList<MarketInfoDto> GetLiquidMarkets(decimal minVolume)
            => _markets.Where(market => market.Volume24h >= minVolume).ToArray();

        public IReadOnlyList<MarketInfoDto> GetExpiringMarkets(TimeSpan within)
            => _markets.Where(market => market.ExpiresAtUtc <= DateTimeOffset.UtcNow.Add(within)).ToArray();
    }

    private sealed class FakeClobClient : IPolymarketClobClient
    {
        public Dictionary<string, PolymarketApiResult<OrderBookSummary>> Books { get; } = new(StringComparer.Ordinal);

        public List<string> RequestedTokenIds { get; } = [];

        public Task<PolymarketApiResult<OrderBookSummary>> GetOrderBookAsync(
            string tokenId,
            CancellationToken cancellationToken = default)
        {
            RequestedTokenIds.Add(tokenId);
            return Task.FromResult(Books.TryGetValue(tokenId, out var result)
                ? result
                : PolymarketApiResult<OrderBookSummary>.Failure(404, "book not found", rawBody: null));
        }

        public Task<PolymarketApiResult<long>> GetServerTimeAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<ApiKeyCreds>> CreateApiKeyAsync(
            int? nonce = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<ApiKeyCreds>> DeriveApiKeyAsync(
            int? nonce = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<ApiKeysResponse>> GetApiKeysAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<BanStatus>> GetClosedOnlyModeAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<string>> DeleteApiKeyAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<IReadOnlyList<MarketInfo>>> GetMarketsAsync(
            string? nextCursor = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<MarketInfo>> GetMarketAsync(
            string conditionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<OrderResponse>> PlaceOrderAsync(
            OrderRequest request,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<OrderResponse>> PlaceOrderAsync(
            PostOrderRequest request,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<IReadOnlyList<OrderResponse>>> PlaceOrdersAsync(
            IReadOnlyList<PostOrderRequest> requests,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<CancelOrderResponse>> CancelOrderAsync(
            string orderId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<CancelOrderResponse>> CancelAllOrdersAsync(
            string? market = null,
            string? assetId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<OrderInfo>> GetOrderAsync(
            string orderId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<IReadOnlyList<OrderInfo>>> GetOpenOrdersAsync(
            string? market = null,
            string? assetId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<IReadOnlyList<TradeInfo>>> GetTradesAsync(
            string? market = null,
            string? nextCursor = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<IReadOnlyList<BuilderTradeInfo>>> GetBuilderTradesAsync(
            string builderCode,
            string? market = null,
            string? assetId = null,
            string? tradeId = null,
            string? before = null,
            string? after = null,
            string? nextCursor = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolymarketApiResult<BalanceAllowance>> GetBalanceAllowanceAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
