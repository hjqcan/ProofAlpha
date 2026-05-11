# Polymarket WebSocket 参考文档

> 本文档整理自 `third-party/polymarket-websocket-client` 和 `third-party/polymarket-orderbook-watcher` 两个开源项目，为我们实现 **Task 3（WebSocket Market Data Provider）** 和 **Task 11-12（Repricing Lag Arbitrage Strategy）** 提供协议参考。

---

## 📦 参考项目

| 项目                             | 路径                                        | 说明                                                                  |
| -------------------------------- | ------------------------------------------- | --------------------------------------------------------------------- |
| **polymarket-websocket-client**  | `third-party/polymarket-websocket-client/`  | TypeScript WebSocket 客户端，封装了 CLOB Market/User 和 RTDS 三类通道 |
| **polymarket-orderbook-watcher** | `third-party/polymarket-orderbook-watcher/` | BTC 15 分钟 Up/Down 市场的实时订单簿观察工具                          |

---

## 1. CLOB WebSocket（订单簿与用户订单）

### 1.1 连接地址

| 通道           | URL                                                    | 鉴权         |
| -------------- | ------------------------------------------------------ | ------------ |
| Market（公开） | `wss://ws-subscriptions-clob.polymarket.com/ws/market` | 无需         |
| User（私有）   | `wss://ws-subscriptions-clob.polymarket.com/ws/user`   | 需要 API Key |

### 1.2 订阅协议

**初始订阅消息（连接后发送）**

```json
{
  "type": "MARKET", // 或 "USER"
  "assets_ids": ["<tokenId1>", "<tokenId2>"], // Market 通道用 token IDs
  "markets": ["<conditionId1>"], // User 通道用 condition IDs
  "auth": {
    // 仅 User 通道需要
    "apiKey": "...",
    "secret": "...",
    "passphrase": "..."
  }
}
```

**动态订阅/取消订阅（连接后追加）**

```json
{
  "assets_ids": ["<tokenId>"],
  "operation": "subscribe" // 或 "unsubscribe"
}
```

### 1.3 Market 通道事件

#### `book` - 订单簿快照

首次订阅或有成交影响订单簿时推送。

```json
{
  "event_type": "book",
  "asset_id": "71321045679252212594626385532706912750332728571942532289631379312455583992563",
  "market": "0xbd31dc8a20211944f6b70f31557f1001557b59905b7738480ca09bd4532f84af",
  "bids": [
    { "price": "0.48", "size": "30" },
    { "price": "0.49", "size": "20" }
  ],
  "asks": [
    { "price": "0.52", "size": "25" },
    { "price": "0.53", "size": "60" }
  ],
  "timestamp": "1757908892351",
  "hash": "0x..."
}
```

#### `price_change` - 增量价格变更

订单挂单/撤单时推送。

```json
{
  "event_type": "price_change",
  "market": "0x5f65177b394277fd294cd75650044e32ba009a95022d88a0c1d565897d72f8f1",
  "price_changes": [
    {
      "asset_id": "71321045679252212594626385532706912750332728571942532289631379312455583992563",
      "price": "0.5",
      "size": "200",
      "side": "BUY",
      "hash": "56621a121a47ed9333273e21c83b660cff37ae50",
      "best_bid": "0.5",
      "best_ask": "1"
    }
  ],
  "timestamp": "1757908892351"
}
```

#### `last_trade_price` - 最后成交价

订单撮合时推送。

```json
{
  "event_type": "last_trade_price",
  "asset_id": "114122071509644379678018727908709560226618148003371446110114509806601493071694",
  "market": "0x6a67b9d828d53862160e470329ffea5246f338ecfffdf2cab45211ec578b0347",
  "price": "0.456",
  "side": "BUY",
  "size": "219.217767",
  "fee_rate_bps": "0",
  "timestamp": "1750428146322"
}
```

#### `tick_size_change` - 最小价格变动单位变更

价格接近 0.04 或 0.96 时推送。

```json
{
  "event_type": "tick_size_change",
  "asset_id": "65818619657568813474341868652308942079804919287380422192892211131408793125422",
  "market": "0xbd31dc8a20211944f6b70f31557f1001557b59905b7738480ca09bd4532f84af",
  "old_tick_size": "0.01",
  "new_tick_size": "0.001",
  "timestamp": "100000000"
}
```

### 1.4 User 通道事件

#### `trade` - 成交事件

```json
{
  "event_type": "trade",
  "type": "TRADE",
  "id": "...",
  "asset_id": "...",
  "market": "...",
  "side": "BUY",
  "price": "0.5",
  "size": "100",
  "outcome": "Yes",
  "status": "MATCHED",   // MATCHED → MINED → CONFIRMED/FAILED
  "owner": "0x...",
  "trade_owner": "0x...",
  "taker_order_id": "...",
  "maker_orders": [...],
  "matchtime": "...",
  "last_update": "...",
  "timestamp": "..."
}
```

#### `order` - 订单事件

```json
{
  "event_type": "order",
  "type": "PLACEMENT", // PLACEMENT / UPDATE / CANCELLATION
  "id": "...",
  "asset_id": "...",
  "market": "...",
  "side": "BUY",
  "price": "0.5",
  "original_size": "100",
  "size_matched": "0",
  "outcome": "Yes",
  "owner": "0x...",
  "order_owner": "0x...",
  "associate_trades": null,
  "timestamp": "..."
}
```

### 1.5 心跳

- **CLOB 通道**：建议每 **30 秒**发送 `"ping"` 消息
- 服务端会返回 `pong` 或保持连接

---

## 2. RTDS WebSocket（实时数据流）

### 2.1 连接地址

- **URL**: `wss://ws-live-data.polymarket.com`
- **心跳**: 建议每 **5 秒**发送 `{ "type": "PING" }`

### 2.2 订阅协议

```json
{
  "action": "subscribe",
  "subscriptions": [
    {
      "topic": "crypto_prices",
      "type": "update",
      "filters": "{\"symbol\":\"BTCUSDT\"}" // 可选，JSON 对象格式，符号大写
    }
  ]
}
```

> ℹ️ **filters 格式**（来自 [Polymarket/real-time-data-client](https://github.com/Polymarket/real-time-data-client)）：
>
> - `crypto_prices` / `crypto_prices_chainlink`：`{"symbol":"BTCUSDT"}`
> - `equity_prices`：`{"symbol":"AAPL"}`

### 2.3 crypto_prices - Binance 实时价格

> ⚠️ **这是实现"重定价延迟套利"策略的关键数据源！**

**订阅消息（带 filters）**

```json
{
  "action": "subscribe",
  "subscriptions": [
    {
      "topic": "crypto_prices",
      "type": "update",
      "filters": "{\"symbol\":\"BTCUSDT\"}"
    }
  ]
}
```

> ✅ **正确格式**（来自 [Polymarket/real-time-data-client](https://github.com/Polymarket/real-time-data-client)，经实际测试验证）：
>
> - `filters` 是 JSON 对象格式的字符串：`{"symbol":"BTCUSDT"}`
> - 符号使用**大写**（如 `BTCUSDT`、`ETHUSDT`、`SOLUSDT`）
> - ⚠️ **每个 topic 只能有一个活跃 filter**（实测结论）：
>   - 同一 `subscriptions` 数组中多个相同 topic 订阅 → 只有第一个生效
>   - 分别发送多个订阅消息 → 后者覆盖前者
>   - **多符号方案**：使用 `filters: ""`（空字符串）接收所有数据，客户端过滤

**订阅消息（不带 filters，订阅全部）**

> ℹ️ **测试验证**：省略 `filters` 字段 等效于 `filters: ""`，两者都接收所有数据。

```json
{
  "action": "subscribe",
  "subscriptions": [
    {
      "topic": "crypto_prices",
      "type": "update"
    }
  ]
}
```

**推送消息**

```json
{
  "topic": "crypto_prices",
  "type": "update",
  "timestamp": 1753314088421,
  "payload": {
    "symbol": "btcusdt",
    "timestamp": 1753314088395,
    "value": 67234.5
  }
}
```

### 2.4 filters 行为（实测结论）

| 测试场景                                     | 结果                  |
| -------------------------------------------- | --------------------- |
| 同一 `subscriptions` 数组中两个不同 filters  | **只有第一个生效**    |
| 分别发送两个订阅消息（不同 filters）         | **后者覆盖前者**      |
| `filters: ""` 空字符串                       | **接收所有符号** ✅   |
| `filters: "{\"symbol\":\"BTCUSDT\"}"` 单符号 | **仅接收指定符号** ✅ |

**实现建议**：

- 单符号订阅：使用 `{"symbol":"XXX"}` 服务端过滤（减少带宽）
- 多符号订阅：使用 `filters: ""`，客户端过滤

### 2.5 crypto_prices_chainlink - Chainlink 价格

```json
{
  "topic": "crypto_prices_chainlink",
  "type": "update",
  "timestamp": 1753314064237,
  "payload": {
    "symbol": "btc/usd",
    "timestamp": 1753314064213,
    "value": 67234.5
  }
}
```

---

## 3. 15 分钟窗口识别（来自 orderbook-watcher）

### 3.1 窗口时间戳计算

```javascript
// 计算当前 15 分钟窗口的起始时间戳（秒级）
function getCurrent15MinWindowTimestamp() {
  const now = Math.floor(Date.now() / 1000);
  const windowSeconds = 15 * 60;
  return Math.floor(now / windowSeconds) * windowSeconds;
}
```

**C# 等效实现**

```csharp
public static long GetCurrent15MinWindowTimestamp()
{
    var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    const long windowSeconds = 15 * 60;
    return (nowSeconds / windowSeconds) * windowSeconds;
}
```

### 3.2 构建市场 Slug

```javascript
// 构建 BTC Up/Down 15 分钟市场的 slug
function buildMarketSlug(timestamp) {
  return `btc-updown-15m-${timestamp}`;
}
```

### 3.3 获取市场数据（Gamma API）

```javascript
const slug = buildMarketSlug(timestamp);
const url = `https://gamma-api.polymarket.com/events/slug/${slug}`;
const response = await fetch(url);
const data = await response.json();

// 提取 token IDs
const tokenIds = JSON.parse(data.markets[0].clobTokenIds);
// tokenIds[0] = Up token, tokenIds[1] = Down token
```

---

## 4. C# 实现建议

### 4.1 DTO 映射

参考 `third-party/polymarket-websocket-client/src/types.ts`，需要翻译为 C# 的关键类型：

| TypeScript 类型           | C# 建议类型                                                               |
| ------------------------- | ------------------------------------------------------------------------- |
| `ClobBookEvent`           | `record ClobBookEvent(...)`                                               |
| `ClobPriceChangeEvent`    | `record ClobPriceChangeEvent(...)`                                        |
| `ClobLastTradePriceEvent` | `record ClobLastTradePriceEvent(...)`                                     |
| `OrderSummary`            | `record OrderSummary(string Price, string Size)`                          |
| `PriceChange`             | `record PriceChange(...)`                                                 |
| `CryptoPricePayload`      | `record CryptoPricePayload(string Symbol, long Timestamp, decimal Value)` |

### 4.2 连接生命周期

参考 `third-party/polymarket-websocket-client/src/base-client.ts`：

1. **状态机**: `disconnected → connecting → connected → reconnecting`
2. **自动重连**: 指数退避 + 抖动（`delay * 2^attempt + random(0~1000ms)`）
3. **心跳**: CLOB 30s / RTDS 5s
4. **重订阅**: 断线重连后自动重发所有订阅

### 4.3 订阅管理

参考 `third-party/polymarket-websocket-client/src/clob-client.ts`：

1. 维护 `HashSet<string>` 记录已订阅的 asset_ids
2. 连接成功后发送初始订阅
3. 动态订阅时发送增量 `{ assets_ids, operation: "subscribe" }`
4. 断线重连后重发完整订阅列表

---

## 5. 与我们架构的对接

| 我们的模块                                                     | 对应协议/参考                                                 |
| -------------------------------------------------------------- | ------------------------------------------------------------- |
| `MarketData/Autotrade.MarketData.Domain/Entities/OrderBook.cs` | `ClobBookEvent` + `ClobPriceChangeEvent`                      |
| `MarketData` 域的 WebSocket 订阅管理                           | `base-client.ts` 的连接生命周期 + `clob-client.ts` 的订阅管理 |
| Task 11 的现货价格源                                           | RTDS `crypto_prices` (Binance)                                |
| Task 12 的窗口识别                                             | `orderbook-watcher/source/utils.js` 的窗口计算逻辑            |

---

## 6. 关键文件索引

### polymarket-websocket-client

| 文件                              | 说明                               |
| --------------------------------- | ---------------------------------- |
| `src/types.ts`                    | 所有 DTO 类型定义                  |
| `src/base-client.ts`              | WebSocket 连接生命周期、重连、心跳 |
| `src/clob-client.ts`              | CLOB Market/User 通道订阅管理      |
| `src/rtds-client.ts`              | RTDS 订阅管理（crypto_prices 等）  |
| `docs/developers/CLOB/websocket/` | CLOB WebSocket 协议文档            |
| `docs/developers/RTDS/`           | RTDS WebSocket 协议文档            |

### polymarket-orderbook-watcher

| 文件              | 说明                                            |
| ----------------- | ----------------------------------------------- |
| `source/utils.js` | 15 分钟窗口计算、市场 slug 构建、Gamma API 调用 |
| `source/app.js`   | 完整的订单簿订阅与渲染逻辑                      |

---

## 7. 参考链接

- [polymarket-websocket-client (GitHub)](https://github.com/discountry/polymarket-websocket-client)
- [polymarket-orderbook-watcher (GitHub)](https://github.com/discountry/polymarket-orderbook-watcher)
- [Polymarket CLOB API 官方文档](https://docs.polymarket.com)
