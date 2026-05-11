# MarketData Bounded Context

Market Data 限界上下文负责实时市场数据的获取、同步和本地存储。

## 架构概览

```
MarketData/
├── Autotrade.MarketData.Application/          # 应用服务层
│   ├── OrderBook/                             # 订单簿管理
│   │   ├── ILocalOrderBookStore.cs           # 本地订单簿存储接口
│   │   ├── LocalOrderBookStore.cs            # 内存订单簿实现
│   │   ├── OrderBookSynchronizer.cs          # 订单簿同步器（snapshot + delta）
│   │   └── OrderBookSubscriptionService.cs    # 订阅编排（MarketId -> TokenId）
│   ├── WebSocket/                             # WebSocket 客户端
│   │   ├── Clob/                              # Polymarket CLOB 客户端
│   │   │   └── ClobMarketClient.cs
│   │   └── Base/                              # WebSocket 基类
│   ├── Catalog/                               # 市场目录
│   │   ├── MarketCatalog.cs
│   │   ├── MarketCatalogReader.cs
│   │   └── MarketCatalogSyncService.cs        # 定时刷新（Gamma API -> MarketCatalog）
├── Autotrade.MarketData.Application.Contract/ # 契约层
│   ├── OrderBook/                             # 跨上下文接口
│   │   └── IOrderBookReader.cs               # 只读订单簿查询接口（含 DTO）
│   │   └── IOrderBookSubscriptionService.cs   # 订阅管理接口（Strategy 调用）
│   └── WebSocket/Events/                      # WebSocket 事件类型
├── Autotrade.MarketData.Domain/               # 领域层
├── Autotrade.MarketData.Domain.Shared/        # 共享领域对象
│   ├── Enums/                                 # 枚举类型
│   └── ValueObjects/                          # 值对象 (Price, Quantity)
├── Autotrade.MarketData.Infra.Data/           # 数据持久化
├── Autotrade.MarketData.Infra.CrossCutting.IoC/ # 依赖注入
└── Autotrade.MarketData.Tests/                # 单元测试
```

## 核心组件

### 1. 订单簿同步器 (`OrderBookSynchronizer`)

- 管理 Polymarket CLOB WebSocket 连接
- 处理 snapshot + delta 更新
- 序列号验证和自动重同步
- 断线重连恢复

### 2. 本地订单簿存储 (`LocalOrderBookStore`)

- 内存订单簿状态
- 提供 `GetTopOfBook()` 获取最优买卖价
- 线程安全读写
- 实现 `IOrderBookReader` 供跨上下文使用

### 3. 市场目录 (`MarketCatalog` + `MarketCatalogSyncService`)

- `MarketCatalogSyncService` 周期性从 **Gamma API** 拉取市场元数据（/markets），更新到内存 `MarketCatalog`。
- 策略引擎通过 `IMarketCatalogReader` 查询候选市场（按流动性/到期时间/交易量等过滤）。

### 4. 订阅编排（`OrderBookSubscriptionService`）

- Strategy 引擎每个周期会产出目标 `marketId` 集合
- `OrderBookSubscriptionService` 将 `marketId` 映射到 `tokenId`（来自 MarketCatalog），并驱动 `OrderBookSynchronizer` 订阅/取消订阅
- 断线重连由 `ClobMarketClient` 自动重订阅保证

## 跨上下文依赖

其他 Context 应仅依赖 `MarketData.Application.Contract` 层：

```csharp
// Trading Context 使用
IOrderBookReader orderBookReader; // 只读接口
var topOfBook = orderBookReader.GetTopOfBook("token-id");
```

## 配置

```json
{
  "Polymarket": {
    "Gamma": {
      "Host": "https://gamma-api.polymarket.com",
      "Timeout": "00:00:10"
    },
    "WebSocket": {
      "ClobMarketUrl": "wss://ws-subscriptions-clob.polymarket.com/ws/market",
      "ClobUserUrl": "wss://ws-subscriptions-clob.polymarket.com/ws/user",
      "AutoReconnect": true,
      "MaxReconnectAttempts": 100,
      "ReconnectDelayMs": 1000,
      "MaxReconnectDelayMs": 30000
    }
  },
  "MarketData": {
    "CatalogSync": {
      "Enabled": true,
      "RefreshIntervalSeconds": 300,
      "PageSize": 100,
      "MaxPages": 200,
      "IncludeClosed": false
    }
  }
}
```
