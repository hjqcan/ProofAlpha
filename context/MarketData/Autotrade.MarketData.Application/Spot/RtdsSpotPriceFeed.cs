using Autotrade.MarketData.Application.Contract.Spot;
using Autotrade.MarketData.Application.Contract.WebSocket;
using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Autotrade.MarketData.Application.Observability;
using Autotrade.MarketData.Application.WebSocket.Core;
using Autotrade.MarketData.Application.WebSocket.Rtds;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autotrade.MarketData.Application.Spot;

public sealed class RtdsSpotPriceFeed : ISpotPriceFeed
{
    private readonly IRtdsClient _client;
    private readonly ISpotPriceStore _store;
    private readonly RtdsSpotPriceFeedOptions _options;
    private readonly ILogger<RtdsSpotPriceFeed> _logger;
    private readonly object _sync = new();
    private IDisposable? _subscription;
    private IReadOnlySet<string> _activeSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool _isRunning;

    public RtdsSpotPriceFeed(
        IRtdsClient client,
        ISpotPriceStore store,
        IOptions<RtdsSpotPriceFeedOptions> options,
        ILogger<RtdsSpotPriceFeed> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options.Validate();

        _client.StateChanged += OnStateChanged;
        _client.Error += OnError;
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _isRunning;
            }
        }
    }

    public async Task StartAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        var normalizedSymbols = NormalizeSymbols(symbols).ToArray();
        if (normalizedSymbols.Length == 0)
        {
            normalizedSymbols = NormalizeSymbols(_options.DefaultSymbols).ToArray();
        }

        lock (_sync)
        {
            if (_isRunning)
            {
                return;
            }

            _subscription = _client.OnCryptoPrice(HandleCryptoPriceAsync);
            _activeSymbols = normalizedSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _isRunning = true;
        }

        if (!_client.IsConnected)
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_options.UseChainlinkTopic)
        {
            await _client.SubscribeCryptoPricesChainlinkAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _client.SubscribeCryptoPricesAsync(normalizedSymbols, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "RTDS spot price feed started. Symbols={Symbols}, Topic={Topic}",
            string.Join(",", normalizedSymbols),
            _options.UseChainlinkTopic ? "crypto_prices_chainlink" : "crypto_prices");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        IDisposable? subscription;
        lock (_sync)
        {
            if (!_isRunning)
            {
                return;
            }

            subscription = _subscription;
            _subscription = null;
            _activeSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _isRunning = false;
        }

        subscription?.Dispose();

        if (_options.UseChainlinkTopic)
        {
            await _client.UnsubscribeCryptoPricesChainlinkAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _client.UnsubscribeCryptoPricesAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("RTDS spot price feed stopped.");
    }

    private Task HandleCryptoPriceAsync(CryptoPriceEvent priceEvent)
    {
        var payload = priceEvent.Payload;
        IReadOnlySet<string> activeSymbols;
        lock (_sync)
        {
            activeSymbols = _activeSymbols;
        }

        if (activeSymbols.Count > 0 && !activeSymbols.Contains(payload.Symbol))
        {
            return Task.CompletedTask;
        }

        var timestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(payload.Timestamp);
        var source = priceEvent.Topic switch
        {
            "crypto_prices_chainlink" => "rtds:crypto_prices_chainlink",
            "crypto_prices" => "rtds:crypto_prices",
            _ => $"rtds:{priceEvent.Topic}"
        };

        var result = _store.UpdateTick(new SpotPriceTick(
            payload.Symbol,
            payload.Value,
            timestampUtc,
            source));

        if (result.Accepted)
        {
            MarketDataMetrics.CryptoPriceUpdates.Add(1,
                new KeyValuePair<string, object?>("source", source));
        }
        else
        {
            _logger.LogWarning(
                "Rejected spot price tick. Symbol={Symbol}, Source={Source}, Reason={Reason}",
                payload.Symbol,
                source,
                result.RejectedReason);
        }

        return Task.CompletedTask;
    }

    private void OnStateChanged(object? sender, ConnectionStateChangedEventArgs args)
    {
        MarketDataMetrics.SetRtdsConnectionStatus(args.CurrentState == ConnectionState.Connected);
        if (args.CurrentState == ConnectionState.Reconnecting)
        {
            MarketDataMetrics.Reconnections.Add(1,
                new KeyValuePair<string, object?>("channel", "rtds"));
        }
    }

    private void OnError(object? sender, WebSocketErrorEventArgs args)
    {
        MarketDataMetrics.Errors.Add(1,
            new KeyValuePair<string, object?>("channel", "rtds"),
            new KeyValuePair<string, object?>("context", args.Context ?? "unknown"));
    }

    private static IEnumerable<string> NormalizeSymbols(IEnumerable<string> symbols)
        => symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal);
}
