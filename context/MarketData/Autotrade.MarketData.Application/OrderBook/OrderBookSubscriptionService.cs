using Autotrade.MarketData.Application.Contract.Catalog;
using Autotrade.MarketData.Application.Contract.OrderBook;
using Autotrade.MarketData.Application.WebSocket.Clob;
using Microsoft.Extensions.Logging;

namespace Autotrade.MarketData.Application.OrderBook;

/// <summary>
/// 订单簿订阅管理：将策略选择的市场（MarketId）映射为 TokenId，并确保 CLOB 订单簿已订阅。
/// </summary>
public sealed class OrderBookSubscriptionService : IOrderBookSubscriptionService
{
    private readonly IMarketCatalogReader _catalogReader;
    private readonly IOrderBookSynchronizer _synchronizer;
    private readonly IClobMarketClient _clobClient;
    private readonly ILogger<OrderBookSubscriptionService> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _subscribedAssets = new(StringComparer.Ordinal);
    private volatile int _cachedSubscribedAssetCount;

    public OrderBookSubscriptionService(
        IMarketCatalogReader catalogReader,
        IOrderBookSynchronizer synchronizer,
        IClobMarketClient clobClient,
        ILogger<OrderBookSubscriptionService> logger)
    {
        _catalogReader = catalogReader ?? throw new ArgumentNullException(nameof(catalogReader));
        _synchronizer = synchronizer ?? throw new ArgumentNullException(nameof(synchronizer));
        _clobClient = clobClient ?? throw new ArgumentNullException(nameof(clobClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 非阻塞获取订阅资产数（返回上次更新的缓存值）。
    /// </summary>
    public int SubscribedAssetCount => _cachedSubscribedAssetCount;

    public async Task EnsureSubscribedMarketsAsync(
        IReadOnlyCollection<string> marketIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marketIds);

        // 1) MarketId -> TokenId
        var desired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var marketId in marketIds)
        {
            if (string.IsNullOrWhiteSpace(marketId))
            {
                continue;
            }

            var market = _catalogReader.GetMarket(marketId);
            if (market?.TokenIds is null || market.TokenIds.Count == 0)
            {
                continue;
            }

            foreach (var tokenId in market.TokenIds)
            {
                if (!string.IsNullOrWhiteSpace(tokenId))
                {
                    desired.Add(tokenId);
                }
            }
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var toAdd = new List<string>();
            foreach (var assetId in desired)
            {
                if (_subscribedAssets.Add(assetId))
                {
                    toAdd.Add(assetId);
                }
            }

            var toRemove = new List<string>();
            foreach (var existing in _subscribedAssets.ToArray())
            {
                if (!desired.Contains(existing))
                {
                    toRemove.Add(existing);
                }
            }

            foreach (var assetId in toRemove)
            {
                _subscribedAssets.Remove(assetId);
            }

            if (toAdd.Count == 0 && toRemove.Count == 0)
            {
                return;
            }

            // 2) 确保连接（只在需要订阅/取消订阅时触发）
            if (!_clobClient.IsConnected)
            {
                try
                {
                    await _clobClient.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "CLOB WebSocket connect failed; subscriptions will be retried");
                    return;
                }
            }

            // 3) Apply diff
            if (toRemove.Count > 0)
            {
                await _synchronizer.StopSyncAsync(toRemove, cancellationToken).ConfigureAwait(false);
            }

            if (toAdd.Count > 0)
            {
                await _synchronizer.StartSyncAsync(toAdd, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogDebug(
                "OrderBook subscriptions updated: +{Add} -{Remove} now={Total}",
                toAdd.Count,
                toRemove.Count,
                _subscribedAssets.Count);

            // 更新缓存以供非阻塞读取
            _cachedSubscribedAssetCount = _subscribedAssets.Count;
        }
        finally
        {
            _gate.Release();
        }
    }
}

