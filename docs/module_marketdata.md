# MarketData 模块详细设计

本文档详细描述 MarketData 模块的内部实现、数据流、配置项和扩展点。

---

## 目录

1. [模块概述](#1-模块概述)
2. [核心组件](#2-核心组件)
3. [市场目录同步](#3-市场目录同步)
4. [订单簿订阅与同步](#4-订单簿订阅与同步)
5. [WebSocket 连接管理](#5-websocket-连接管理)
6. [配置项说明](#6-配置项说明)
7. [指标与监控](#7-指标与监控)
8. [常见问题排查](#8-常见问题排查)

---

## 1. 模块概述

MarketData 模块负责从 Polymarket 获取市场数据，包括：

- **市场目录 (Catalog)**：从 Gamma API 获取所有市场的元数据
- **订单簿 (OrderBook)**：通过 WebSocket 实时订阅订单簿更新

### 1.1 模块边界

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           MarketData 模块                                │
│                                                                         │
│  输入:                                                                   │
│  ├── Gamma API (REST): 市场列表、元数据                                  │
│  └── CLOB WebSocket: 订单簿快照、增量更新                                │
│                                                                         │
│  输出:                                                                   │
│  ├── IMarketCatalog: 市场元数据查询接口                                  │
│  ├── IOrderBookReader: 订单簿查询接口                                    │
│  └── IMarketSnapshotProvider: 组合快照提供接口                           │
│                                                                         │
│  消费者:                                                                 │
│  └── Strategy 模块 (通过上述接口读取数据)                                 │
└─────────────────────────────────────────────────────────────────────────┘
```

### 1.2 项目结构

```
context/MarketData/
├── Autotrade.MarketData.Application/
│   ├── Catalog/
│   │   ├── IMarketCatalog.cs              # 市场目录接口
│   │   ├── InMemoryMarketCatalog.cs       # 内存实现
│   │   └── MarketCatalogSyncService.cs    # 同步后台服务
│   ├── OrderBook/
│   │   ├── IOrderBookReader.cs            # 订单簿读取接口
│   │   ├── LocalOrderBookStore.cs         # 内存订单簿存储
│   │   ├── OrderBookSubscriptionService.cs # 订阅管理服务
│   │   └── OrderBookSynchronizer.cs       # 快照/增量同步器
│   ├── WebSocket/
│   │   └── Clob/
│   │       ├── IClobMarketClient.cs       # WS 客户端接口
│   │       └── ClobMarketClient.cs        # WS 客户端实现
│   └── Observability/
│       └── MarketDataMetrics.cs           # 指标定义
├── Autotrade.MarketData.Application.Contract/
│   ├── Catalog/
│   │   └── MarketInfo.cs                  # 市场信息 DTO
│   └── OrderBook/
│       ├── OrderBookSnapshot.cs           # 订单簿快照 DTO
│       └── TopOfBook.cs                   # 最优价格 DTO
├── Autotrade.MarketData.Domain/
│   └── (预留实体)
├── Autotrade.MarketData.Infra.Data/
│   └── Context/
│       └── MarketDataContext.cs           # EF 上下文 (预留)
└── Autotrade.MarketData.Infra.CrossCutting.IoC/
    └── MarketDataServiceCollectionExtensions.cs
```

---

## 2. 核心组件

### 2.1 组件依赖关系

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│  ┌───────────────────────┐                                                  │
│  │ MarketCatalogSyncSvc  │────────────────┐                                 │
│  │ (后台服务)            │                │                                 │
│  └───────────┬───────────┘                │                                 │
│              │ 更新                        │                                 │
│              ▼                            │                                 │
│  ┌───────────────────────┐                │                                 │
│  │ InMemoryMarketCatalog │                │ 触发订阅更新                    │
│  │ (单例)                │                │                                 │
│  └───────────────────────┘                │                                 │
│              │                            ▼                                 │
│              │ 提供市场信息    ┌───────────────────────┐                    │
│              │                │ OrderBookSubscription │                    │
│              │                │       Service         │                    │
│              │                │ (后台服务)            │                    │
│              │                └───────────┬───────────┘                    │
│              │                            │ 订阅/取消订阅                   │
│              │                            ▼                                 │
│              │                ┌───────────────────────┐                    │
│              │                │   ClobMarketClient    │                    │
│              │                │   (WebSocket)         │                    │
│              │                └───────────┬───────────┘                    │
│              │                            │ 消息                           │
│              │                            ▼                                 │
│              │                ┌───────────────────────┐                    │
│              │                │ OrderBookSynchronizer │                    │
│              │                │ (消息处理)            │                    │
│              │                └───────────┬───────────┘                    │
│              │                            │ 更新                           │
│              │                            ▼                                 │
│              │                ┌───────────────────────┐                    │
│              └───────────────►│  LocalOrderBookStore  │                    │
│                提供元数据      │  (单例, 内存缓存)     │                    │
│                               └───────────────────────┘                    │
│                                           │                                 │
│                                           │ 提供 Top-of-Book               │
│                                           ▼                                 │
│                               ┌───────────────────────┐                    │
│                               │ MarketSnapshotProvider│                    │
│                               │ (组合快照)            │──────► Strategy    │
│                               └───────────────────────┘                    │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 接口定义

#### IMarketCatalog

```csharp
public interface IMarketCatalog
{
    /// <summary>获取指定市场信息</summary>
    MarketInfo? GetMarket(string marketId);

    /// <summary>获取所有活跃市场</summary>
    IReadOnlyList<MarketInfo> GetActiveMarkets();

    /// <summary>根据 TokenId 获取市场信息</summary>
    MarketInfo? GetMarketByTokenId(string tokenId);

    /// <summary>目录更新事件</summary>
    event EventHandler<CatalogUpdatedEventArgs>? OnCatalogUpdated;
}
```

#### IOrderBookReader

```csharp
public interface IOrderBookReader
{
    /// <summary>获取指定资产的 Top-of-Book</summary>
    TopOfBook? GetTopOfBook(string tokenId);

    /// <summary>获取指定资产的完整订单簿快照</summary>
    OrderBookSnapshot? GetSnapshot(string tokenId);

    /// <summary>检查资产是否已同步</summary>
    bool IsSynced(string tokenId);
}
```

---

## 3. 市场目录同步

### 3.1 同步流程详解

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                       MarketCatalogSyncService                               │
│                                                                             │
│  ExecuteAsync (后台循环):                                                    │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  while (!cancellationToken.IsCancellationRequested)                 │   │
│  │  {                                                                  │   │
│  │      1. 调用 FetchMarketsAsync()                                    │   │
│  │      2. 更新 InMemoryMarketCatalog                                  │   │
│  │      3. 触发 OnCatalogUpdated 事件                                  │   │
│  │      4. 等待 RefreshIntervalSeconds                                 │   │
│  │  }                                                                  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  FetchMarketsAsync():                                                       │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  for (int page = 0; page < MaxPages; page++)                        │   │
│  │  {                                                                  │   │
│  │      // 调用 Gamma API                                              │   │
│  │      var response = await gammaClient.GetMarketsAsync(              │   │
│  │          limit: PageSize,       // 默认 100                         │   │
│  │          offset: page * PageSize,                                   │   │
│  │          closed: IncludeClosed  // 默认 false                       │   │
│  │      );                                                             │   │
│  │                                                                     │   │
│  │      // 映射为 MarketInfo                                           │   │
│  │      foreach (var market in response.Data)                          │   │
│  │      {                                                              │   │
│  │          yield return MapToMarketInfo(market);                      │   │
│  │      }                                                              │   │
│  │                                                                     │   │
│  │      // 如果返回数量 < PageSize，说明已到最后一页                     │   │
│  │      if (response.Data.Count < PageSize) break;                     │   │
│  │  }                                                                  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 MarketInfo 数据结构

```csharp
public sealed record MarketInfo(
    string MarketId,           // 市场 ID (如 "601700")
    string ConditionId,        // 条件 ID (链上标识)
    string Question,           // 市场问题描述
    string? Description,       // 详细描述
    DateTimeOffset? EndDateUtc,// 到期时间
    bool IsActive,             // 是否活跃
    bool IsClosed,             // 是否已关闭
    decimal Liquidity,         // 流动性 (USDC)
    decimal Volume24h,         // 24h 成交量
    IReadOnlyList<TokenInfo> Tokens // YES/NO Token 列表
);

public sealed record TokenInfo(
    string TokenId,            // Token ID (很长的数字字符串)
    string Outcome,            // "Yes" 或 "No"
    decimal? Price             // 当前价格 (来自 Gamma API)
);
```

### 3.3 Gamma API 响应示例

```json
// GET https://gamma-api.polymarket.com/markets?limit=100&offset=0
{
  "data": [
    {
      "id": "601700",
      "condition_id": "0x...",
      "question": "Will BTC reach $100k by end of 2024?",
      "description": "...",
      "end_date_iso": "2024-12-31T23:59:59Z",
      "active": true,
      "closed": false,
      "liquidity": "125000.50",
      "volume_24hr": "5000.25",
      "clob_token_ids": [
        "16419649354067298412736919830777830730026677464626899811394461690794060330642",
        "42139849929574046088630785796780813725435914859433767469767950066058132350666"
      ],
      "outcomes": ["Yes", "No"],
      "outcome_prices": ["0.65", "0.35"]
    }
    // ... more markets
  ]
}
```

---

## 4. 订单簿订阅与同步

### 4.1 订阅决策流程

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     OrderBookSubscriptionService                             │
│                                                                             │
│  定期检查 (或 CatalogUpdated 事件触发):                                      │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  1. 收集策略需要的 TokenId                                          │   │
│  │                                                                     │   │
│  │     var neededTokenIds = new HashSet<string>();                     │   │
│  │     foreach (var strategy in strategyManager.GetRunningStrategies())│   │
│  │     {                                                               │   │
│  │         foreach (var marketId in strategy.GetActiveMarkets())       │   │
│  │         {                                                           │   │
│  │             var market = catalog.GetMarket(marketId);               │   │
│  │             foreach (var token in market.Tokens)                    │   │
│  │             {                                                       │   │
│  │                 neededTokenIds.Add(token.TokenId);                  │   │
│  │             }                                                       │   │
│  │         }                                                           │   │
│  │     }                                                               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  2. 计算订阅差集                                                    │   │
│  │                                                                     │   │
│  │     var currentlySubscribed = clobMarketClient.GetSubscribedAssets();│   │
│  │     var toSubscribe = neededTokenIds.Except(currentlySubscribed);   │   │
│  │     var toUnsubscribe = currentlySubscribed.Except(neededTokenIds); │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  3. 执行订阅变更                                                    │   │
│  │                                                                     │   │
│  │     if (toUnsubscribe.Any())                                        │   │
│  │     {                                                               │   │
│  │         await clobMarketClient.UnsubscribeAsync(toUnsubscribe);     │   │
│  │         logger.LogInformation("已取消订阅 {Count} 个资产",          │   │
│  │             toUnsubscribe.Count());                                 │   │
│  │     }                                                               │   │
│  │                                                                     │   │
│  │     if (toSubscribe.Any())                                          │   │
│  │     {                                                               │   │
│  │         await clobMarketClient.SubscribeAsync(toSubscribe);         │   │
│  │         logger.LogInformation("已订阅 {Count} 个资产: {Ids}",       │   │
│  │             toSubscribe.Count(), string.Join(", ", toSubscribe));   │   │
│  │     }                                                               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 订单簿同步器消息处理

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        OrderBookSynchronizer                                 │
│                                                                             │
│  消息类型处理:                                                               │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  "book" (快照消息):                                                 │   │
│  │                                                                     │   │
│  │  {                                                                  │   │
│  │    "event_type": "book",                                            │   │
│  │    "asset_id": "16419649...",                                       │   │
│  │    "market": "601700",                                              │   │
│  │    "timestamp": "1704067200000",                                    │   │
│  │    "hash": "abc123...",                                             │   │
│  │    "bids": [                                                        │   │
│  │      { "price": "0.65", "size": "1000" },                           │   │
│  │      { "price": "0.64", "size": "500" }                             │   │
│  │    ],                                                               │   │
│  │    "asks": [                                                        │   │
│  │      { "price": "0.66", "size": "800" },                            │   │
│  │      { "price": "0.67", "size": "1200" }                            │   │
│  │    ]                                                                │   │
│  │  }                                                                  │   │
│  │                                                                     │   │
│  │  处理: 完全替换本地订单簿                                            │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  "price_change" (增量消息):                                         │   │
│  │                                                                     │   │
│  │  {                                                                  │   │
│  │    "event_type": "price_change",                                    │   │
│  │    "asset_id": "16419649...",                                       │   │
│  │    "market": "601700",                                              │   │
│  │    "side": "BUY",                                                   │   │
│  │    "price": "0.65",                                                 │   │
│  │    "size": "1500",        // 新数量，0 表示删除                      │   │
│  │    "timestamp": "1704067205000",                                    │   │
│  │    "hash": "def456...",                                             │   │
│  │    "best_bid": "0.65",                                              │   │
│  │    "best_ask": "0.66"                                               │   │
│  │  }                                                                  │   │
│  │                                                                     │   │
│  │  处理:                                                              │   │
│  │  1. 应用价格变更到本地订单簿                                        │   │
│  │  2. 一致性校验: 对比本地 best_bid/ask 与消息中的值                   │   │
│  │  3. 如果不一致 → 触发 resync                                        │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.3 本地订单簿数据结构

```csharp
public sealed class LocalOrderBookStore : IOrderBookReader
{
    // 按 TokenId 索引的订单簿
    private readonly ConcurrentDictionary<string, OrderBookState> _books = new();

    private sealed class OrderBookState
    {
        public string TokenId { get; init; }
        public string MarketId { get; set; }

        // 按价格排序的买单 (降序)
        public SortedDictionary<decimal, decimal> Bids { get; } = new(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));

        // 按价格排序的卖单 (升序)
        public SortedDictionary<decimal, decimal> Asks { get; } = new();

        public decimal? BestBidPrice => Bids.Count > 0 ? Bids.Keys.First() : null;
        public decimal? BestAskPrice => Asks.Count > 0 ? Asks.Keys.First() : null;

        public string? Hash { get; set; }
        public DateTimeOffset LastUpdatedUtc { get; set; }
        public bool IsSynced { get; set; }
    }
}
```

---

## 5. WebSocket 连接管理

### 5.1 连接生命周期

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          ClobMarketClient                                    │
│                                                                             │
│  连接状态机:                                                                 │
│                                                                             │
│       ┌───────────┐      ConnectAsync()      ┌───────────┐                  │
│       │Disconnected│─────────────────────────►│ Connecting│                  │
│       └───────────┘                          └─────┬─────┘                  │
│            ▲                                       │                         │
│            │                                       │ 连接成功                │
│            │                                       ▼                         │
│            │                                 ┌───────────┐                  │
│            │                                 │ Connected │                  │
│            │                                 └─────┬─────┘                  │
│            │                                       │                         │
│            │     ┌─────────────────────────────────┼─────────────────┐      │
│            │     │                                 │                 │      │
│            │     ▼                                 ▼                 ▼      │
│            │  心跳超时              接收消息              主动断开          │
│            │     │                     │                    │               │
│            │     ▼                     ▼                    ▼               │
│            │ ┌────────┐          处理消息            ┌───────────┐          │
│            │ │Reconnect│             │              │Disconnecting│          │
│            │ └────┬───┘              │              └─────┬─────┘          │
│            │      │                  │                    │                 │
│            │      │ AutoReconnect    │                    │                 │
│            └──────┴──────────────────┴────────────────────┘                 │
│                                                                             │
│  重连策略:                                                                   │
│  - 初始延迟: ReconnectDelayMs (默认 1000ms)                                 │
│  - 最大延迟: MaxReconnectDelayMs (默认 30000ms)                             │
│  - 指数退避: delay = min(delay * 2, MaxReconnectDelayMs)                    │
│  - 最大重试: MaxReconnectAttempts (默认 100)                                │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 消息收发格式

```
发送 - 订阅请求:
{
  "type": "subscribe",
  "channel": "market",
  "assets_ids": ["16419649...", "42139849...", ...]
}

发送 - 取消订阅:
{
  "type": "unsubscribe",
  "channel": "market",
  "assets_ids": ["16419649...", ...]
}

接收 - 订阅确认:
{
  "type": "subscribed",
  "channel": "market",
  "assets_ids": ["16419649...", ...]
}

接收 - 心跳:
{
  "type": "heartbeat"
}
```

---

## 6. 配置项说明

### 6.1 MarketData 配置

```json
{
  "MarketData": {
    "CatalogSync": {
      "Enabled": true, // 是否启用目录同步
      "RefreshIntervalSeconds": 300, // 刷新间隔 (秒)
      "PageSize": 100, // 每页市场数
      "MaxPages": 200, // 最大页数
      "IncludeClosed": false // 是否包含已关闭市场
    }
  }
}
```

### 6.2 Polymarket WebSocket 配置

```json
{
  "Polymarket": {
    "WebSocket": {
      "ClobMarketUrl": "wss://ws-subscriptions-clob.polymarket.com/ws/market",
      "AutoReconnect": true, // 自动重连
      "MaxReconnectAttempts": 100, // 最大重试次数
      "ReconnectDelayMs": 1000, // 初始重连延迟
      "MaxReconnectDelayMs": 30000, // 最大重连延迟
      "ClobHeartbeatIntervalMs": 30000, // 心跳间隔
      "ConnectionTimeoutMs": 10000, // 连接超时
      "ReceiveBufferSize": 65536 // 接收缓冲区大小
    }
  }
}
```

### 6.3 Gamma API 配置

```json
{
  "Polymarket": {
    "Gamma": {
      "Host": "https://gamma-api.polymarket.com",
      "Timeout": "00:00:10" // 请求超时
    }
  }
}
```

---

## 7. 指标与监控

### 7.1 可观测指标

```csharp
public static class MarketDataMetrics
{
    // 市场目录刷新
    public static void RecordCatalogRefresh(int marketCount, double durationMs);

    // 订单簿更新
    public static void RecordOrderBookUpdate(string tokenId, string updateType);

    // WebSocket 连接状态
    public static void RecordWebSocketState(string state);  // connected/disconnected/reconnecting

    // 一致性校验
    public static void RecordConsistencyCheck(string tokenId, bool passed);

    // 重同步
    public static void RecordResync(string tokenId, int resyncCount);
}
```

### 7.2 日志关键点

| 日志内容                               | 级别 | 含义             |
| -------------------------------------- | ---- | ---------------- |
| `市场目录更新: 新增=X, 更新=Y, 总数=Z` | INFO | 目录同步完成     |
| `正在连接 WebSocket: wss://...`        | INFO | 开始建立 WS 连接 |
| `WebSocket 已连接: wss://...`          | INFO | WS 连接成功      |
| `已订阅 N 个资产: ...`                 | INFO | 订阅成功         |
| `已取消订阅 N 个资产`                  | INFO | 取消订阅         |
| `Best Bid/Ask 不一致: ...`             | WARN | 一致性校验失败   |
| `一致性校验失败，触发重同步`           | WARN | 触发 resync      |
| `WebSocket 连接断开，正在重连`         | WARN | 连接断开         |

---

## 8. 常见问题排查

### 8.1 "已取消订阅 N 个资产" 频繁出现

**可能原因**：

1. 策略选择的市场在变化（流动性/成交量排序变化）
2. 市场目录刷新后，部分市场不再满足筛选条件

**排查步骤**：

1. 检查 `MaxMarkets` 配置，考虑适当增大
2. 检查 `MinLiquidity` / `MinVolume24h` 阈值
3. 观察日志中 `市场目录更新` 前后的订阅变化

### 8.2 "Best Bid/Ask 不一致" 告警

**可能原因**：

1. 网络延迟导致消息乱序
2. 本地处理速度跟不上消息推送速度
3. WebSocket 连接不稳定丢包

**排查步骤**：

1. 检查网络延迟（Polymarket API 响应时间）
2. 观察 resync 频率，偶发是正常的
3. 如果持续高频，检查 `ReceiveBufferSize` 配置

### 8.3 WebSocket 频繁重连

**可能原因**：

1. 网络不稳定
2. 心跳超时配置过短
3. Polymarket 服务端限流

**排查步骤**：

1. 检查 `ClobHeartbeatIntervalMs` 是否过短
2. 检查 Polymarket API 延迟
3. 检查是否有 429 Too Many Requests 错误

### 8.4 市场目录同步慢

**可能原因**：

1. Gamma API 响应慢
2. 网络延迟
3. 拉取页数过多

**排查步骤**：

1. 观察日志中 `MarketCatalog refreshed: ... durationMs=XXX`
2. 考虑减少 `MaxPages` 或增加 `PageSize`
3. 如果不需要所有市场，使用 `IncludeClosed: false`
