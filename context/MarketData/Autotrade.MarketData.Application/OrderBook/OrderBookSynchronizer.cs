using System.Collections.Concurrent;
using Autotrade.MarketData.Application.Contract.WebSocket;
using Autotrade.MarketData.Application.Contract.WebSocket.Events;
using Autotrade.MarketData.Application.Contract.Tape;
using Autotrade.MarketData.Application.Observability;
using Autotrade.MarketData.Application.WebSocket.Clob;
using Autotrade.MarketData.Application.WebSocket.Core;
using Microsoft.Extensions.Logging;

namespace Autotrade.MarketData.Application.OrderBook;

/// <summary>
/// 订单簿同步器，负责管理快照与增量更新的序列化处理。
/// 实现快照优先、增量缓冲、一致性校验和自动重同步。
/// </summary>
public sealed class OrderBookSynchronizer : IOrderBookSynchronizer, IDisposable
{
    private readonly ILocalOrderBookStore _orderBookStore;
    private readonly IClobMarketClient _clobMarketClient;
    private readonly ILogger<OrderBookSynchronizer> _logger;
    private readonly IMarketTapeRecorder? _tapeRecorder;

    /// <summary>
    /// 每个资产的同步状态。
    /// </summary>
    private readonly ConcurrentDictionary<string, AssetSyncState> _syncStates = new();

    /// <summary>
    /// 用于保护缓冲区操作的锁对象。
    /// </summary>
    private readonly object _bufferLock = new();

    /// <summary>
    /// 缓冲区最大容量，超过此值触发重同步。
    /// </summary>
    private const int MaxBufferSize = 100;

    /// <summary>
    /// 订单簿更新事件。
    /// </summary>
    public event EventHandler<OrderBookUpdatedEventArgs>? OrderBookUpdated;

    public OrderBookSynchronizer(
        ILocalOrderBookStore orderBookStore,
        IClobMarketClient clobMarketClient,
        ILogger<OrderBookSynchronizer> logger,
        IMarketTapeRecorder? tapeRecorder = null)
    {
        _orderBookStore = orderBookStore ?? throw new ArgumentNullException(nameof(orderBookStore));
        _clobMarketClient = clobMarketClient ?? throw new ArgumentNullException(nameof(clobMarketClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tapeRecorder = tapeRecorder;

        // 修复：订阅连接状态变更，断线时重置所有资产的快照状态
        _clobMarketClient.StateChanged += OnConnectionStateChanged;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> SynchronizedAssets =>
        _syncStates.Where(kv => kv.Value.HasSnapshot).Select(kv => kv.Key).ToList();

    /// <inheritdoc />
    public async Task StartSyncAsync(IEnumerable<string> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        var assetList = assetIds.ToList();

        foreach (var assetId in assetList)
        {
            // 修复：如果已存在状态，先释放旧的回调和清理本地订单簿
            if (_syncStates.TryGetValue(assetId, out var existingState))
            {
                existingState.Dispose();
                // 修复：重新注册时清理旧订单簿，避免消费者读取到旧快照
                _orderBookStore.Clear(assetId);
            }

            var state = new AssetSyncState(assetId);
            _syncStates[assetId] = state;

            // 注册回调
            state.BookDisposable = _clobMarketClient.OnBook(assetId, async bookEvent =>
            {
                await HandleBookEventAsync(assetId, bookEvent).ConfigureAwait(false);
            });

            state.PriceChangeDisposable = _clobMarketClient.OnPriceChange(assetId, async priceChangeEvent =>
            {
                await HandlePriceChangeEventAsync(assetId, priceChangeEvent).ConfigureAwait(false);
            });

            if (_tapeRecorder is not null)
            {
                state.LastTradePriceDisposable = _clobMarketClient.OnLastTradePrice(async tradeEvent =>
                {
                    if (string.Equals(tradeEvent.AssetId, assetId, StringComparison.OrdinalIgnoreCase))
                    {
                        await _tapeRecorder.RecordLastTradePriceEventAsync(tradeEvent).ConfigureAwait(false);
                    }
                });
            }
        }

        // 订阅 WebSocket
        await _clobMarketClient.SubscribeAsync(assetList, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("开始同步 {Count} 个资产的订单簿", assetList.Count);
        MarketDataMetrics.SetSubscribedAssetsCount(_clobMarketClient.SubscribedAssets.Count);
    }

    /// <inheritdoc />
    public async Task StopSyncAsync(IEnumerable<string> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        var assetList = assetIds.ToList();

        foreach (var assetId in assetList)
        {
            if (_syncStates.TryRemove(assetId, out var state))
            {
                state.Dispose();
                _orderBookStore.Clear(assetId);
            }
        }

        // 尝试取消订阅，但不因连接问题而失败
        try
        {
            await _clobMarketClient.UnsubscribeAsync(assetList, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // WebSocket 可能已断开，忽略此错误
            _logger.LogDebug("停止同步时 WebSocket 已断开");
        }

        _logger.LogInformation("停止同步 {Count} 个资产的订单簿", assetList.Count);
        MarketDataMetrics.SetSubscribedAssetsCount(_clobMarketClient.SubscribedAssets.Count);
    }

    /// <inheritdoc />
    public AssetSyncStatus? GetSyncStatus(string assetId)
    {
        if (!_syncStates.TryGetValue(assetId, out var state))
        {
            return null;
        }

        int bufferedCount;
        lock (_bufferLock)
        {
            bufferedCount = state.BufferedDeltas.Count;
        }

        return new AssetSyncStatus(
            AssetId: assetId,
            HasSnapshot: state.HasSnapshot,
            LastSnapshotUtc: state.LastSnapshotUtc,
            LastUpdateUtc: state.LastUpdateUtc,
            DeltaCount: state.DeltaCount,
            BufferedDeltaCount: bufferedCount,
            ResyncCount: state.ResyncCount,
            ConsistencyErrorCount: state.ConsistencyErrorCount);
    }

    /// <inheritdoc />
    public async Task RequestResyncAsync(string assetId, CancellationToken cancellationToken = default)
    {
        if (_syncStates.TryGetValue(assetId, out var state))
        {
            // 清空本地状态
            state.HasSnapshot = false;
            state.LastSnapshotHash = null;

            lock (_bufferLock)
            {
                state.BufferedDeltas.Clear();
            }

            state.ResyncCount++;
            _orderBookStore.Clear(assetId);

            _logger.LogInformation("已请求重同步资产: {AssetId}，重同步次数: {ResyncCount}",
                assetId, state.ResyncCount);

            MarketDataMetrics.ResyncRequests.Add(1, new KeyValuePair<string, object?>("asset_id", assetId));

            // 触发重新订阅以获取新快照
            await _clobMarketClient.UnsubscribeAsync(new[] { assetId }, cancellationToken).ConfigureAwait(false);
            await _clobMarketClient.SubscribeAsync(new[] { assetId }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 订阅连接状态变更，断线/重连时强制进入"等待快照"状态。
    /// </summary>
    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        // 当连接断开或进入重连状态时，完全重置所有资产的状态
        if (e.CurrentState is ConnectionState.Disconnected or ConnectionState.Reconnecting)
        {
            _logger.LogWarning("WebSocket 连接状态变更: {PreviousState} -> {CurrentState}，完全重置所有资产状态",
                e.PreviousState, e.CurrentState);

            foreach (var state in _syncStates.Values)
            {
                state.HasSnapshot = false;
                // 修复：同时清空 LastSnapshotHash，确保重连后首个快照不会被当作重复跳过
                state.LastSnapshotHash = null;
                state.LastSnapshotUtc = null;
                state.LastUpdateUtc = null;
                
                // 清空缓冲区，等待新快照
                lock (_bufferLock)
                {
                    state.BufferedDeltas.Clear();
                }

                // 修复：清空本地订单簿，避免消费者读取到旧数据
                _orderBookStore.Clear(state.AssetId);
            }

            MarketDataMetrics.Reconnections.Add(1);
        }
        else if (e.CurrentState == ConnectionState.Connected && e.PreviousState is ConnectionState.Reconnecting or ConnectionState.Disconnected)
        {
            _logger.LogInformation("WebSocket 已重新连接，等待新快照");
        }
    }

    private async Task HandleBookEventAsync(string assetId, ClobBookEvent bookEvent)
    {
        using var activity = MarketDataActivitySource.StartOrderBookUpdate(assetId, isSnapshot: true);

        if (!_syncStates.TryGetValue(assetId, out var state))
        {
            return;
        }

        try
        {
            // 校验 hash（如果有上一次快照的 hash）
            if (state.LastSnapshotHash != null && state.LastSnapshotHash == bookEvent.Hash)
            {
                // 相同的快照，可能是重复推送，跳过
                _logger.LogDebug("收到重复快照（Hash 相同），跳过: {AssetId}, Hash={Hash}", assetId, bookEvent.Hash);
                return;
            }

            // 应用快照
            _orderBookStore.ApplySnapshot(bookEvent);

            state.HasSnapshot = true;
            state.LastSnapshotUtc = DateTimeOffset.UtcNow;
            state.LastSnapshotHash = bookEvent.Hash;
            state.LastUpdateUtc = DateTimeOffset.UtcNow;

            MarketDataMetrics.OrderBookSnapshots.Add(1, new KeyValuePair<string, object?>("asset_id", assetId));

            _logger.LogDebug("已应用订单簿快照: {AssetId}, Hash={Hash}, Bids={BidCount}, Asks={AskCount}",
                assetId, bookEvent.Hash, bookEvent.Bids.Count, bookEvent.Asks.Count);

            // 原子操作处理缓冲的增量更新
            List<ClobPriceChangeEvent> bufferedDeltas;
            lock (_bufferLock)
            {
                bufferedDeltas = new List<ClobPriceChangeEvent>(state.BufferedDeltas.Count);
                while (state.BufferedDeltas.TryDequeue(out var delta))
                {
                    bufferedDeltas.Add(delta);
                }
            }

            foreach (var delta in bufferedDeltas)
            {
                _orderBookStore.ApplyDelta(delta);
                state.DeltaCount++;
            }

            if (bufferedDeltas.Count > 0)
            {
                _logger.LogDebug("已应用 {Count} 个缓冲的增量更新: {AssetId}", bufferedDeltas.Count, assetId);
            }

            // 触发事件
            OnOrderBookUpdated(assetId, isSnapshot: true);
            if (_tapeRecorder is not null)
            {
                await _tapeRecorder.RecordBookEventAsync(bookEvent).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理订单簿快照异常: {AssetId}", assetId);
            MarketDataMetrics.Errors.Add(1, new KeyValuePair<string, object?>("type", "snapshot_processing"));
        }
    }

    private async Task HandlePriceChangeEventAsync(string assetId, ClobPriceChangeEvent priceChangeEvent)
    {
        using var activity = MarketDataActivitySource.StartOrderBookUpdate(assetId, isSnapshot: false);

        if (!_syncStates.TryGetValue(assetId, out var state))
        {
            return;
        }

        try
        {
            if (!state.HasSnapshot)
            {
                // 没有快照，缓冲增量（带锁保护）
                bool bufferOverflow = false;
                lock (_bufferLock)
                {
                    // 修复：缓冲区溢出时触发重同步而非静默丢弃
                    if (state.BufferedDeltas.Count >= MaxBufferSize)
                    {
                        bufferOverflow = true;
                    }
                    else
                    {
                        state.BufferedDeltas.Enqueue(priceChangeEvent);
                    }
                }

                if (bufferOverflow)
                {
                    _logger.LogWarning("缓冲区溢出（等待快照时增量过多），触发重同步: {AssetId}, 缓冲数量: {Count}",
                        assetId, MaxBufferSize);
                    
                    MarketDataMetrics.Errors.Add(1, new KeyValuePair<string, object?>("type", "buffer_overflow"));
                    
                    // 修复：使用 try/catch 处理异常，避免 fire-and-forget 静默失败
                    await SafeRequestResyncAsync(assetId).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("缓冲增量更新（等待快照）: {AssetId}, 缓冲数量: {Count}",
                        assetId, state.BufferedDeltas.Count);
                }

                return;
            }

            // 应用增量
            _orderBookStore.ApplyDelta(priceChangeEvent);
            state.DeltaCount++;
            state.LastUpdateUtc = DateTimeOffset.UtcNow;

            // 修复：在应用增量后进行一致性校验，避免合法价格变动被误判
            if (priceChangeEvent.PriceChanges.Count > 0)
            {
                var change = priceChangeEvent.PriceChanges[0];
                if (!ValidateConsistency(assetId, change, state))
                {
                    // 一致性校验失败，触发重同步
                    _logger.LogWarning("一致性校验失败，触发重同步: {AssetId}", assetId);
                    await SafeRequestResyncAsync(assetId).ConfigureAwait(false);
                    return;
                }
            }

            MarketDataMetrics.OrderBookUpdates.Add(1, new KeyValuePair<string, object?>("asset_id", assetId));

            // 触发事件
            OnOrderBookUpdated(assetId, isSnapshot: false);
            if (_tapeRecorder is not null)
            {
                await _tapeRecorder.RecordPriceChangeEventAsync(priceChangeEvent).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理价格变动异常: {AssetId}", assetId);
            MarketDataMetrics.Errors.Add(1, new KeyValuePair<string, object?>("type", "delta_processing"));

            // 触发重同步
            await SafeRequestResyncAsync(assetId).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 使用 PriceChange 的 BestBid/BestAsk 与本地订单簿比对，检测不一致。
    /// 采用严格相等校验，符合强一致性目标。
    /// </summary>
    private bool ValidateConsistency(string assetId, PriceChange change, AssetSyncState state)
    {
        // 获取本地 top-of-book
        var topOfBook = _orderBookStore.GetTopOfBook(assetId);
        if (topOfBook == null)
        {
            // 没有本地数据，无法校验
            return true;
        }

        var remoteBestBid = change.BestBidDecimal;
        var remoteBestAsk = change.BestAskDecimal;
        var localBestBid = topOfBook.BestBidPrice?.Value ?? 0m;
        // Polymarket CLOB 约定：best_bid=0 表示无买单；best_ask=1 表示无卖单（见 docs/polymarket_websocket_reference.md）
        var localBestAsk = topOfBook.BestAskPrice?.Value ?? 1m;

        // 修复：严格相等校验（而非漂移阈值）
        // 同时覆盖远端为 0/1（无挂单）但本地仍有挂单的情况
        
        // Best Bid 一致性检查
        if (remoteBestBid != localBestBid)
        {
            // 允许本地还未更新的情况：如果这次变更会让本地变成远端值，则不算不一致
            // 但如果远端 best 与本地 best 已经不同，说明丢失了中间状态
            
            // 远端有挂单但本地没有，或远端没有但本地有，或两者都有但不相等
            var remoteBidExists = remoteBestBid > 0;
            var localBidExists = localBestBid > 0;
            
            if (remoteBidExists != localBidExists || (remoteBidExists && localBidExists && remoteBestBid != localBestBid))
            {
                state.ConsistencyErrorCount++;
                _logger.LogWarning(
                    "Best Bid 不一致: {AssetId}, 本地={LocalBid}, 远端={RemoteBid}",
                    assetId, localBestBid, remoteBestBid);
                
                MarketDataMetrics.Errors.Add(1, new KeyValuePair<string, object?>("type", "consistency_mismatch"));
                return false;
            }
        }

        // Best Ask 一致性检查（best_ask=1 表示无卖单）
        if (remoteBestAsk != localBestAsk)
        {
            var remoteAskExists = remoteBestAsk < 1m;
            var localAskExists = localBestAsk < 1m;
            
            if (remoteAskExists != localAskExists || (remoteAskExists && localAskExists && remoteBestAsk != localBestAsk))
            {
                state.ConsistencyErrorCount++;
                _logger.LogWarning(
                    "Best Ask 不一致: {AssetId}, 本地={LocalAsk}, 远端={RemoteAsk}",
                    assetId, localBestAsk, remoteBestAsk);
                
                MarketDataMetrics.Errors.Add(1, new KeyValuePair<string, object?>("type", "consistency_mismatch"));
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 修复：安全地请求重同步，捕获并记录异常。
    /// </summary>
    private async Task SafeRequestResyncAsync(string assetId)
    {
        try
        {
            await RequestResyncAsync(assetId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "请求重同步失败: {AssetId}", assetId);
            MarketDataMetrics.Errors.Add(1, new KeyValuePair<string, object?>("type", "resync_failed"));
        }
    }

    private void OnOrderBookUpdated(string assetId, bool isSnapshot)
    {
        OrderBookUpdated?.Invoke(this, new OrderBookUpdatedEventArgs(assetId, isSnapshot));
    }

    public void Dispose()
    {
        // 取消订阅连接状态变更
        _clobMarketClient.StateChanged -= OnConnectionStateChanged;

        foreach (var state in _syncStates.Values)
        {
            state.Dispose();
        }
        _syncStates.Clear();
    }

    /// <summary>
    /// 资产同步状态。
    /// </summary>
    private sealed class AssetSyncState : IDisposable
    {
        public string AssetId { get; }
        public bool HasSnapshot { get; set; }
        public string? LastSnapshotHash { get; set; }
        public DateTimeOffset? LastSnapshotUtc { get; set; }
        public DateTimeOffset? LastUpdateUtc { get; set; }
        public long DeltaCount { get; set; }
        public int ResyncCount { get; set; }
        public int ConsistencyErrorCount { get; set; }

        public ConcurrentQueue<ClobPriceChangeEvent> BufferedDeltas { get; } = new();

        public IDisposable? BookDisposable { get; set; }
        public IDisposable? PriceChangeDisposable { get; set; }
        public IDisposable? LastTradePriceDisposable { get; set; }

        public AssetSyncState(string assetId)
        {
            AssetId = assetId;
        }

        public void Dispose()
        {
            BookDisposable?.Dispose();
            PriceChangeDisposable?.Dispose();
            LastTradePriceDisposable?.Dispose();
        }
    }
}

/// <summary>
/// 订单簿同步器接口。
/// </summary>
public interface IOrderBookSynchronizer : IDisposable
{
    /// <summary>
    /// 已同步的资产 ID 列表。
    /// </summary>
    IReadOnlyCollection<string> SynchronizedAssets { get; }

    /// <summary>
    /// 订单簿更新事件。
    /// </summary>
    event EventHandler<OrderBookUpdatedEventArgs>? OrderBookUpdated;

    /// <summary>
    /// 开始同步指定资产的订单簿。
    /// </summary>
    Task StartSyncAsync(IEnumerable<string> assetIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止同步指定资产的订单簿。
    /// </summary>
    Task StopSyncAsync(IEnumerable<string> assetIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取资产的同步状态。
    /// </summary>
    AssetSyncStatus? GetSyncStatus(string assetId);

    /// <summary>
    /// 请求重同步指定资产（会触发重新订阅以获取新快照）。
    /// </summary>
    Task RequestResyncAsync(string assetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 资产同步状态。
/// </summary>
public sealed record AssetSyncStatus(
    string AssetId,
    bool HasSnapshot,
    DateTimeOffset? LastSnapshotUtc,
    DateTimeOffset? LastUpdateUtc,
    long DeltaCount,
    int BufferedDeltaCount,
    int ResyncCount,
    int ConsistencyErrorCount);

/// <summary>
/// 订单簿更新事件参数。
/// </summary>
public sealed class OrderBookUpdatedEventArgs : EventArgs
{
    public string AssetId { get; }
    public bool IsSnapshot { get; }
    public DateTimeOffset UpdatedAtUtc { get; }

    public OrderBookUpdatedEventArgs(string assetId, bool isSnapshot)
    {
        AssetId = assetId;
        IsSnapshot = isSnapshot;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
