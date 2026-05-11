using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Autotrade.MarketData.Application.Observability;

/// <summary>
/// MarketData 模块的指标定义。
/// </summary>
public static class MarketDataMetrics
{
    private static readonly Meter Meter = new("Autotrade.MarketData", "1.0.0");

    /// <summary>
    /// WebSocket 消息接收计数。
    /// </summary>
    public static readonly Counter<long> MessagesReceived = Meter.CreateCounter<long>(
        "marketdata.websocket.messages_received",
        unit: "messages",
        description: "Number of WebSocket messages received");

    /// <summary>
    /// WebSocket 消息处理延迟。
    /// </summary>
    public static readonly Histogram<double> MessageProcessingDuration = Meter.CreateHistogram<double>(
        "marketdata.websocket.message_processing_duration",
        unit: "ms",
        description: "Duration of WebSocket message processing");

    /// <summary>
    /// WebSocket 消息延迟（订阅 lag）：消息自带时间戳到本地接收时间的差值。
    /// </summary>
    public static readonly Histogram<double> MessageLagMs = Meter.CreateHistogram<double>(
        "marketdata.websocket.message_lag_ms",
        unit: "ms",
        description: "Lag between event timestamp and receive time");

    /// <summary>
    /// 订单簿更新计数。
    /// </summary>
    public static readonly Counter<long> OrderBookUpdates = Meter.CreateCounter<long>(
        "marketdata.orderbook.updates",
        unit: "updates",
        description: "Number of order book updates applied");

    /// <summary>
    /// 订单簿快照计数。
    /// </summary>
    public static readonly Counter<long> OrderBookSnapshots = Meter.CreateCounter<long>(
        "marketdata.orderbook.snapshots",
        unit: "snapshots",
        description: "Number of order book snapshots received");

    /// <summary>
    /// 当前订阅的资产数量。
    /// </summary>
    public static readonly ObservableGauge<int> SubscribedAssetsGauge = Meter.CreateObservableGauge<int>(
        "marketdata.subscriptions.assets",
        observeValue: () => _currentSubscribedAssets,
        unit: "assets",
        description: "Number of currently subscribed assets");

    /// <summary>
    /// 加密货币价格更新计数。
    /// </summary>
    public static readonly Counter<long> CryptoPriceUpdates = Meter.CreateCounter<long>(
        "marketdata.crypto_prices.updates",
        unit: "updates",
        description: "Number of crypto price updates received");

    /// <summary>
    /// WebSocket 重连次数。
    /// </summary>
    public static readonly Counter<long> Reconnections = Meter.CreateCounter<long>(
        "marketdata.websocket.reconnections",
        unit: "reconnections",
        description: "Number of WebSocket reconnection attempts");

    /// <summary>
    /// WebSocket 错误计数。
    /// </summary>
    public static readonly Counter<long> Errors = Meter.CreateCounter<long>(
        "marketdata.websocket.errors",
        unit: "errors",
        description: "Number of WebSocket errors");

    /// <summary>
    /// 订单簿重同步请求计数。
    /// </summary>
    public static readonly Counter<long> ResyncRequests = Meter.CreateCounter<long>(
        "marketdata.orderbook.resync_requests",
        unit: "requests",
        description: "Number of order book resync requests");

    /// <summary>
    /// CLOB 连接状态。
    /// </summary>
    public static readonly ObservableGauge<int> ClobConnectionGauge = Meter.CreateObservableGauge<int>(
        "marketdata.websocket.clob_connected",
        observeValue: () => _clobConnected ? 1 : 0,
        unit: "{status}",
        description: "CLOB WebSocket connection status (1=connected, 0=disconnected)");

    /// <summary>
    /// RTDS 连接状态。
    /// </summary>
    public static readonly ObservableGauge<int> RtdsConnectionGauge = Meter.CreateObservableGauge<int>(
        "marketdata.websocket.rtds_connected",
        observeValue: () => _rtdsConnected ? 1 : 0,
        unit: "{status}",
        description: "RTDS WebSocket connection status (1=connected, 0=disconnected)");

    private static int _currentSubscribedAssets;
    private static bool _clobConnected;
    private static bool _rtdsConnected;

    /// <summary>
    /// 设置当前订阅的资产数量。
    /// </summary>
    public static void SetSubscribedAssetsCount(int count)
    {
        Interlocked.Exchange(ref _currentSubscribedAssets, count);
    }

    /// <summary>
    /// 设置 CLOB 连接状态。
    /// </summary>
    public static void SetClobConnectionStatus(bool connected)
    {
        _clobConnected = connected;
    }

    /// <summary>
    /// 设置 RTDS 连接状态。
    /// </summary>
    public static void SetRtdsConnectionStatus(bool connected)
    {
        _rtdsConnected = connected;
    }
}

/// <summary>
/// MarketData 模块的 Activity 源。
/// </summary>
public static class MarketDataActivitySource
{
    /// <summary>
    /// Activity 源名称。
    /// </summary>
    public const string SourceName = "Autotrade.MarketData";

    /// <summary>
    /// Activity 源实例。
    /// </summary>
    public static readonly ActivitySource Source = new(SourceName, "1.0.0");

    /// <summary>
    /// 开始 WebSocket 消息处理追踪。
    /// </summary>
    public static Activity? StartMessageProcessing(string eventType, string? assetId = null)
    {
        var activity = Source.StartActivity("ProcessWebSocketMessage", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "polymarket_websocket");
        activity?.SetTag("messaging.operation", "receive");
        activity?.SetTag("event.type", eventType);

        if (assetId != null)
        {
            activity?.SetTag("asset.id", assetId);
        }

        return activity;
    }

    /// <summary>
    /// 开始订单簿更新追踪。
    /// </summary>
    public static Activity? StartOrderBookUpdate(string assetId, bool isSnapshot)
    {
        var activity = Source.StartActivity("UpdateOrderBook", ActivityKind.Internal);
        activity?.SetTag("asset.id", assetId);
        activity?.SetTag("update.type", isSnapshot ? "snapshot" : "delta");
        return activity;
    }
}
