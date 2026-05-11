using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Application.Contract.Spot;
using Autotrade.MarketData.Application.Contract.WebSocket;
using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Autotrade.MarketData.Application.Contract.Windows;
using Autotrade.MarketData.Application.Snapshots;
using Autotrade.MarketData.Application.Spot;
using Autotrade.MarketData.Application.WebSocket.Core;
using Autotrade.MarketData.Application.WebSocket.Rtds;
using Autotrade.MarketData.Application.Windows;
using Autotrade.MarketData.Domain.Shared.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autotrade.MarketData.Tests;

public sealed class SpotAndWindowSpecTests
{
    [Fact]
    public void SpotStore_KeepsLatestMonotonicAndPreservesOutOfOrderHistory()
    {
        var store = CreateStore();
        var t0 = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);

        var first = store.UpdateTick(new SpotPriceTick("btcusdt", 100m, t0.AddSeconds(2), "test"));
        var outOfOrder = store.UpdateTick(new SpotPriceTick("BTCUSDT", 99m, t0.AddSeconds(1), "test"));

        Assert.True(first.Accepted);
        Assert.True(first.UpdatedLatest);
        Assert.True(outOfOrder.Accepted);
        Assert.False(outOfOrder.UpdatedLatest);

        Assert.True(store.TryGetLatest("btcusdt", out var latest));
        Assert.Equal(100m, latest.Price);
        Assert.Equal("BTCUSDT", latest.Symbol);

        Assert.True(store.TryGetAtOrBefore("BTCUSDT", t0.AddSeconds(1), TimeSpan.FromSeconds(5), out var baseline));
        Assert.Equal(99m, baseline.Price);
    }

    [Fact]
    public void MarketWindowSpecParser_ParsesCryptoUpDown15mSlug()
    {
        var market = CreateMarket("btc-updown-15m-1777881600");

        var spec = MarketWindowSpecParser.TryParseCryptoUpDown15m(
            market,
            "polymarket-rtds-crypto-prices",
            "start-vs-end",
            MarketWindowOracleStatus.Confirmed);

        Assert.NotNull(spec);
        Assert.Equal("BTCUSDT", spec.UnderlyingSymbol);
        Assert.Equal(new DateTimeOffset(2026, 5, 4, 8, 0, 0, TimeSpan.Zero), spec.WindowStartUtc);
        Assert.Equal(new DateTimeOffset(2026, 5, 4, 8, 15, 0, TimeSpan.Zero), spec.WindowEndUtc);
        Assert.True(spec.IsOracleConfirmed);
        Assert.Equal("yes-token", spec.TokenMap.YesTokenId);
        Assert.Equal("no-token", spec.TokenMap.NoTokenId);
    }

    [Fact]
    public void SnapshotReader_ReturnsSpotOrderBookAndWindowSpecWithStaleness()
    {
        var windowStartUtc = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeSeconds());
        var market = CreateMarket($"btc-updown-15m-{windowStartUtc.ToUnixTimeSeconds()}");
        var catalog = new FakeMarketCatalogReader(market);
        var orderBook = new FakeOrderBookReader(
            yes: Top("yes-token", 0.42m, 10m, 0.45m, 11m),
            no: Top("no-token", 0.52m, 9m, 0.55m, 8m));
        var store = CreateStore();
        var provider = new MarketWindowSpecProvider(
            catalog,
            Options.Create(new MarketWindowSpecOptions { OracleStatus = MarketWindowOracleStatus.Confirmed }));
        var reader = new MarketDataSnapshotReader(catalog, orderBook, store, provider);

        store.UpdateTick(new SpotPriceTick("BTCUSDT", 100m, windowStartUtc, "test"));
        store.UpdateTick(new SpotPriceTick("BTCUSDT", 102m, DateTimeOffset.UtcNow, "test"));

        var snapshot = reader.GetSnapshot(
            market.MarketId,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.WindowSpec);
        Assert.Equal(102m, snapshot.LatestSpot!.Price);
        Assert.Equal(100m, snapshot.BaselineSpot!.Price);
        Assert.True(snapshot.SpotStaleness.IsFresh);
        Assert.True(snapshot.BaselineSpotStaleness.IsFresh);
        Assert.True(snapshot.OrderBookStaleness.IsFresh);
        Assert.Equal(0.45m, snapshot.YesTopOfBook!.BestAskPrice!.Value);
        Assert.Single(snapshot.YesDepth);
    }

    [Fact]
    public void SnapshotReader_MarksBaselineStale_WhenWindowStartTickIsOutsideConfiguredAge()
    {
        var windowStartUtc = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeSeconds());
        var market = CreateMarket($"btc-updown-15m-{windowStartUtc.ToUnixTimeSeconds()}");
        var catalog = new FakeMarketCatalogReader(market);
        var orderBook = new FakeOrderBookReader(
            yes: Top("yes-token", 0.42m, 10m, 0.45m, 11m),
            no: Top("no-token", 0.52m, 9m, 0.55m, 8m));
        var store = CreateStore();
        var provider = new MarketWindowSpecProvider(
            catalog,
            Options.Create(new MarketWindowSpecOptions { OracleStatus = MarketWindowOracleStatus.Confirmed }));
        var reader = new MarketDataSnapshotReader(catalog, orderBook, store, provider);
        store.UpdateTick(new SpotPriceTick("BTCUSDT", 100m, windowStartUtc.AddSeconds(-20), "test"));
        store.UpdateTick(new SpotPriceTick("BTCUSDT", 102m, DateTimeOffset.UtcNow, "test"));

        var snapshot = reader.GetSnapshot(
            market.MarketId,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            maxBaselineSpotAge: TimeSpan.FromSeconds(5));

        Assert.NotNull(snapshot);
        Assert.Null(snapshot.BaselineSpot);
        Assert.False(snapshot.BaselineSpotStaleness.IsFresh);
        Assert.Equal("baseline_spot_missing_or_stale", snapshot.BaselineSpotStaleness.Reason);
    }

    [Fact]
    public async Task RtdsSpotPriceFeed_UsesSingleChainlinkSubscriptionAndFiltersRequestedSymbols()
    {
        var client = new FakeRtdsClient();
        var store = CreateStore();
        var feed = new RtdsSpotPriceFeed(
            client,
            store,
            Options.Create(new RtdsSpotPriceFeedOptions
            {
                UseChainlinkTopic = true,
                DefaultSymbols = ["BTCUSDT", "ETHUSDT"]
            }),
            NullLogger<RtdsSpotPriceFeed>.Instance);

        await feed.StartAsync(["BTCUSDT", "ETHUSDT"], CancellationToken.None);
        await client.PublishCryptoPriceAsync("BTCUSDT", 100m);
        await client.PublishCryptoPriceAsync("SOLUSDT", 200m);

        Assert.Single(client.ChainlinkSubscriptions);
        Assert.Null(client.ChainlinkSubscriptions[0]);
        Assert.True(store.TryGetLatest("BTCUSDT", out var btc));
        Assert.Equal(100m, btc.Price);
        Assert.False(store.TryGetLatest("SOLUSDT", out _));

        await feed.StopAsync(CancellationToken.None);
    }

    private static InMemorySpotPriceStore CreateStore()
        => new(Options.Create(new SpotPriceStoreOptions
        {
            MaxTicksPerSymbol = 32,
            MaxHistoryMinutes = 24 * 60
        }));

    private static MarketInfoDto CreateMarket(string slug)
        => new()
        {
            MarketId = "market-1",
            ConditionId = "condition-1",
            Name = "Bitcoin Up or Down - May 4, 8:00AM-8:15AM",
            Slug = slug,
            Status = "Active",
            Liquidity = 1000m,
            Volume24h = 500m,
            TokenIds = ["yes-token", "no-token"]
        };

    private static TopOfBookDto Top(string assetId, decimal bid, decimal bidSize, decimal ask, decimal askSize)
        => new(
            assetId,
            new Price(bid),
            new Quantity(bidSize),
            new Price(ask),
            new Quantity(askSize),
            ask - bid,
            DateTimeOffset.UtcNow);

    private sealed class FakeMarketCatalogReader : IMarketCatalogReader
    {
        private readonly MarketInfoDto _market;

        public FakeMarketCatalogReader(MarketInfoDto market)
        {
            _market = market;
        }

        public MarketInfoDto? GetMarket(string marketId)
            => string.Equals(marketId, _market.MarketId, StringComparison.OrdinalIgnoreCase) ? _market : null;

        public IReadOnlyList<MarketInfoDto> GetAllMarkets() => [_market];

        public IReadOnlyList<MarketInfoDto> GetActiveMarkets() => [_market];

        public IReadOnlyList<MarketInfoDto> GetLiquidMarkets(decimal minVolume) => [_market];

        public IReadOnlyList<MarketInfoDto> GetExpiringMarkets(TimeSpan within) => [_market];
    }

    private sealed class FakeOrderBookReader : IOrderBookReader
    {
        private readonly TopOfBookDto _yes;
        private readonly TopOfBookDto _no;

        public FakeOrderBookReader(TopOfBookDto yes, TopOfBookDto no)
        {
            _yes = yes;
            _no = no;
        }

        public TopOfBookDto? GetTopOfBook(string assetId)
            => assetId == _yes.AssetId ? _yes : assetId == _no.AssetId ? _no : null;

        public IReadOnlyList<PriceLevelDto> GetDepth(string assetId, int levels = 10)
            => GetTopOfBook(assetId) is null ? [] : [new PriceLevelDto(0.45m, 1m, false)];
    }

    private sealed class FakeRtdsClient : IRtdsClient
    {
        private Func<CryptoPriceEvent, Task>? _cryptoPriceCallback;

        public List<string?> ChainlinkSubscriptions { get; } = [];

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        public bool IsConnected { get; private set; }

        public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

        public event EventHandler<WebSocketErrorEventArgs>? Error
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            State = ConnectionState.Connected;
            IsConnected = true;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(ConnectionState.Disconnected, State));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            State = ConnectionState.Disconnected;
            IsConnected = false;
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(ConnectionState.Connected, State));
            return Task.CompletedTask;
        }

        public Task SubscribeCryptoPricesAsync(
            IEnumerable<string>? symbols = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UnsubscribeCryptoPricesAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SubscribeCryptoPricesChainlinkAsync(
            string? symbol = null,
            CancellationToken cancellationToken = default)
        {
            ChainlinkSubscriptions.Add(symbol);
            return Task.CompletedTask;
        }

        public Task UnsubscribeCryptoPricesChainlinkAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public IDisposable OnCryptoPrice(Func<CryptoPriceEvent, Task> callback)
        {
            _cryptoPriceCallback = callback;
            return new CallbackDisposer(() => _cryptoPriceCallback = null);
        }

        public Task PublishCryptoPriceAsync(string symbol, decimal value)
        {
            if (_cryptoPriceCallback is null)
            {
                return Task.CompletedTask;
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return _cryptoPriceCallback(new CryptoPriceEvent
            {
                Topic = "crypto_prices_chainlink",
                Type = "update",
                Timestamp = nowMs,
                Payload = new CryptoPricePayload
                {
                    Symbol = symbol,
                    Timestamp = nowMs,
                    Value = value
                }
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class CallbackDisposer : IDisposable
        {
            private readonly Action _dispose;

            public CallbackDisposer(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose() => _dispose();
        }
    }
}
