# Trading 模块详细设计

本文档详细描述 Trading 模块的内部实现、执行流程、风控机制和审计系统。

---

## 目录

1. [模块概述](#1-模块概述)
2. [核心组件](#2-核心组件)
3. [执行服务](#3-执行服务)
4. [风控系统](#4-风控系统)
5. [审计与持久化](#5-审计与持久化)
6. [配置项说明](#6-配置项说明)
7. [Live 模式详解](#7-live-模式详解)
8. [常见问题排查](#8-常见问题排查)

---

## 1. 模块概述

Trading 模块负责：

- **订单执行**：Paper 模式模拟执行，Live 模式真实下单
- **风险控制**：资本限制、订单限制、KillSwitch
- **订单审计**：记录订单事件、成交到数据库
- **订单对账**：Live 模式下同步交易所订单状态

### 1.1 模块边界

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Trading 模块                                   │
│                                                                         │
│  输入:                                                                   │
│  ├── ExecutionRequest (来自 Strategy)                                   │
│  ├── 订单簿数据 (来自 MarketData，Paper 模式成交判断)                    │
│  └── 配置 (Execution / Risk / PaperTrading)                             │
│                                                                         │
│  输出:                                                                   │
│  ├── ExecutionResult (返回给 Strategy)                                  │
│  ├── OrderStateUpdate (通知 Strategy 订单状态)                          │
│  ├── Orders/OrderEvents/Trades (持久化到数据库)                          │
│  └── RiskEventLog (风控事件日志)                                         │
│                                                                         │
│  依赖:                                                                   │
│  ├── IOrderBookReader (Paper 模式读取订单簿)                             │
│  └── IPolymarketClobClient (Live 模式调用交易 API)                       │
└─────────────────────────────────────────────────────────────────────────┘
```

### 1.2 项目结构

```
context/Trading/
├── Autotrade.Trading.Application/
│   ├── Execution/
│   │   ├── IExecutionService.cs           # 执行服务接口
│   │   ├── PaperExecutionService.cs       # Paper 模式实现
│   │   ├── LiveExecutionService.cs        # Live 模式实现
│   │   ├── ExecutionModeFactory.cs        # 执行模式工厂
│   │   ├── InMemoryIdempotencyStore.cs    # 幂等性存储
│   │   ├── InMemoryOrderStateTracker.cs   # 订单状态跟踪
│   │   ├── OrderLimitValidator.cs         # 订单限制验证
│   │   ├── OrderReconciliationService.cs  # 订单对账服务
│   │   └── TimeInForceHandler.cs          # 有效期处理
│   ├── Risk/
│   │   ├── IRiskManager.cs                # 风控接口
│   │   ├── RiskManager.cs                 # 风控实现
│   │   ├── RiskStateStore.cs              # 风控状态存储
│   │   ├── RiskMetrics.cs                 # 风控指标
│   │   ├── KillSwitchService.cs           # 紧急停止服务
│   │   └── UnhedgedExposureWatchdog.cs    # 未对冲敞口监控
│   ├── Audit/
│   │   ├── IOrderAuditLogger.cs           # 订单审计接口
│   │   ├── OrderAuditLogger.cs            # 订单审计实现
│   │   └── OrderAuditIds.cs               # 审计 ID 生成
│   ├── Maintenance/
│   │   ├── TradingRetentionService.cs     # 数据保留服务
│   │   └── TradingRetentionOptions.cs     # 保留配置
│   └── Metrics/
│       └── TradingMetrics.cs              # 交易指标
├── Autotrade.Trading.Application.Contract/
│   ├── Execution/
│   │   ├── ExecutionRequest.cs            # 执行请求
│   │   ├── ExecutionResult.cs             # 执行结果
│   │   ├── ExecutionOptions.cs            # 执行配置
│   │   ├── PaperTradingOptions.cs         # Paper 配置
│   │   └── ExecutionStatus.cs             # 订单状态枚举
│   ├── Risk/
│   │   ├── RiskOptions.cs                 # 风控配置
│   │   └── RiskCapitalOptions.cs          # 资本限制配置
│   └── Audit/
│       └── OrderStateUpdate.cs            # 订单状态更新
├── Autotrade.Trading.Domain/
│   └── Entities/
│       ├── Order.cs                       # 订单实体
│       ├── OrderEvent.cs                  # 订单事件实体
│       ├── Trade.cs                       # 成交实体
│       ├── TradingAccount.cs              # 交易账户聚合实体
│       ├── Position.cs                    # 持仓实体
│       └── RiskEventLog.cs                # 风控事件实体
└── Autotrade.Trading.Infra.Data/
    ├── Context/
    │   └── TradingContext.cs              # EF 上下文
    └── Repositories/
        ├── EfOrderRepository.cs
        ├── EfTradeRepository.cs
        ├── EfOrderEventRepository.cs
        ├── EfPositionRepository.cs
        └── EfRiskEventRepository.cs
```

---

## 2. 核心组件

### 2.1 组件关系图

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│  Strategy 模块                                                              │
│       │                                                                     │
│       │ ExecutionRequest                                                    │
│       ▼                                                                     │
│  ┌───────────────────────┐                                                  │
│  │  ExecutionModeFactory │                                                  │
│  │                       │                                                  │
│  │  根据 Execution:Mode  │                                                  │
│  │  选择执行服务         │                                                  │
│  └───────────┬───────────┘                                                  │
│              │                                                              │
│      ┌───────┴───────┐                                                      │
│      ▼               ▼                                                      │
│  ┌───────────┐   ┌───────────┐                                              │
│  │  Paper    │   │   Live    │                                              │
│  │ Execution │   │ Execution │                                              │
│  │  Service  │   │  Service  │                                              │
│  └─────┬─────┘   └─────┬─────┘                                              │
│        │               │                                                    │
│        │               │                                                    │
│        └───────┬───────┘                                                    │
│                │                                                            │
│  ┌─────────────┼─────────────────────────────────────────────────────────┐ │
│  │             ▼                                                          │ │
│  │  ┌─────────────────────┐                                               │ │
│  │  │  IdempotencyStore   │  幂等性检查                                    │ │
│  │  └─────────────────────┘                                               │ │
│  │             │                                                          │ │
│  │             ▼                                                          │ │
│  │  ┌─────────────────────┐                                               │ │
│  │  │ OrderLimitValidator │  订单限制检查                                  │ │
│  │  └─────────────────────┘                                               │ │
│  │             │                                                          │ │
│  │             ▼                                                          │ │
│  │  ┌─────────────────────┐                                               │ │
│  │  │    RiskManager      │  风控检查                                      │ │
│  │  └─────────────────────┘                                               │ │
│  │             │                                                          │ │
│  │             ▼                                                          │ │
│  │  ┌─────────────────────┐                                               │ │
│  │  │  OrderStateTracker  │  状态跟踪 & 通知                               │ │
│  │  └─────────────────────┘                                               │ │
│  │             │                                                          │ │
│  │             ▼                                                          │ │
│  │  ┌─────────────────────┐                                               │ │
│  │  │  OrderAuditLogger   │  审计日志                                      │ │
│  │  └─────────────────────┘                                               │ │
│  │                                                                        │ │
│  │                         共享基础设施                                    │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 核心接口

#### IExecutionService

```csharp
public interface IExecutionService
{
    /// <summary>下单</summary>
    Task<ExecutionResult> PlaceOrderAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default);
    
    /// <summary>撤单</summary>
    Task<ExecutionResult> CancelOrderAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default);
    
    /// <summary>查询订单状态</summary>
    Task<OrderStatusResult> GetOrderStatusAsync(
        string clientOrderId,
        CancellationToken cancellationToken = default);
}
```

#### ExecutionRequest

```csharp
public sealed class ExecutionRequest
{
    public string ClientOrderId { get; init; }    // 客户端订单 ID (幂等键)
    public string TokenId { get; init; }          // Token ID
    public string MarketId { get; init; }         // 市场 ID
    public string? StrategyId { get; init; }      // 策略 ID
    public string? CorrelationId { get; init; }   // 关联 ID
    
    public string Outcome { get; init; }          // "Yes" / "No"
    public OrderSide Side { get; init; }          // Buy / Sell
    public TimeInForce TimeInForce { get; init; } // GTC / GTD / FOK / FAK
    public decimal Price { get; init; }           // 限价
    public decimal Quantity { get; init; }        // 数量
    public DateTimeOffset? GoodTilDateUtc { get; init; } // GTD 到期时间
}
```

#### ExecutionResult

```csharp
public sealed class ExecutionResult
{
    public bool Success { get; init; }
    public string ClientOrderId { get; init; }
    public string? ExchangeOrderId { get; init; }
    public ExecutionStatus Status { get; init; }
    public decimal FilledQuantity { get; init; }
    public decimal? AveragePrice { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
```

---

## 3. 执行服务

### 3.1 执行模式选择

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        ExecutionModeFactory                                  │
│                                                                             │
│  public IExecutionService Create()                                          │
│  {                                                                          │
│      var mode = _options.CurrentValue.Mode;                                 │
│                                                                             │
│      return mode switch                                                     │
│      {                                                                      │
│          ExecutionMode.Paper => _serviceProvider                            │
│              .GetRequiredService<PaperExecutionService>(),                  │
│                                                                             │
│          ExecutionMode.Live => _serviceProvider                             │
│              .GetRequiredService<LiveExecutionService>(),                   │
│                                                                             │
│          _ => throw new InvalidOperationException($"Unknown mode: {mode}")  │
│      };                                                                     │
│  }                                                                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Paper 执行流程

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      PaperExecutionService.PlaceOrderAsync()                 │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Step 1: 请求验证                                                   │   │
│  │                                                                     │   │
│  │  var validationError = request.Validate();                          │   │
│  │  if (validationError != null)                                       │   │
│  │      return ExecutionResult.Fail("VALIDATION_ERROR", validationError);│   │
│  │                                                                     │   │
│  │  验证内容:                                                          │   │
│  │  - ClientOrderId 非空                                               │   │
│  │  - TokenId / MarketId 有效                                          │   │
│  │  - Price 在 0.01 ~ 0.99 范围                                        │   │
│  │  - Quantity > 0                                                     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Step 2: 幂等性检查                                                 │   │
│  │                                                                     │   │
│  │  var (isNew, existingOrderId) = await _idempotencyStore             │   │
│  │      .TryAddAsync(clientOrderId, requestHash, ttl);                 │   │
│  │                                                                     │   │
│  │  if (!isNew && existingOrderId != null)                             │   │
│  │  {                                                                  │   │
│  │      // 重复请求，返回已有订单                                      │   │
│  │      return ExecutionResult.Succeed(clientOrderId, existingOrderId);│   │
│  │  }                                                                  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Step 3: 订单限制检查                                               │   │
│  │                                                                     │   │
│  │  var limitError = _limitValidator.ValidateCanPlaceOrder(marketId);  │   │
│  │  if (limitError != null)                                            │   │
│  │      return ExecutionResult.Fail("ORDER_LIMIT_EXCEEDED", limitError);│   │
│  │                                                                     │   │
│  │  检查内容:                                                          │   │
│  │  - 每分钟订单数 < MaxOrdersPerMinute                                │   │
│  │  - 该市场活跃订单数 < MaxOrdersPerMarket                            │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Step 4: 模拟延迟 (可选)                                            │   │
│  │                                                                     │   │
│  │  if (_options.SimulatedLatencyMs > 0)                               │   │
│  │      await Task.Delay(_options.SimulatedLatencyMs);                 │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Step 5: 创建模拟订单                                               │   │
│  │                                                                     │   │
│  │  var exchangeOrderId = $"PAPER-{++_orderIdCounter:D8}";             │   │
│  │  var paperOrder = new PaperOrder { ... };                           │   │
│  │  _orders[clientOrderId] = paperOrder;                               │   │
│  │                                                                     │   │
│  │  → 日志: "[Paper] 订单已接受: ClientOrderId=xxx, ..."               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Step 6: 状态通知                                                   │   │
│  │                                                                     │   │
│  │  await _stateTracker.OnOrderStateChangedAsync(new OrderStateUpdate  │   │
│  │  {                                                                  │   │
│  │      ClientOrderId = clientOrderId,                                 │   │
│  │      Status = ExecutionStatus.Accepted,                             │   │
│  │      ...                                                            │   │
│  │  });                                                                │   │
│  │                                                                     │   │
│  │  → 通知策略: 订单已被接受                                           │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Step 7: 审计日志                                                   │   │
│  │                                                                     │   │
│  │  await _auditLogger.LogOrderAcceptedAsync(orderId, ...);            │   │
│  │                                                                     │   │
│  │  → 写入 OrderEvents 表                                              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Step 8: 模拟撮合                                                   │   │
│  │                                                                     │   │
│  │  await SimulateFillAsync(paperOrder, ...);                          │   │
│  │                                                                     │   │
│  │  见下方详细流程                                                     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Step 9: 返回结果                                                   │   │
│  │                                                                     │   │
│  │  return new ExecutionResult                                         │   │
│  │  {                                                                  │   │
│  │      Success = true,                                                │   │
│  │      ClientOrderId = clientOrderId,                                 │   │
│  │      ExchangeOrderId = exchangeOrderId,                             │   │
│  │      Status = paperOrder.Status,                                    │   │
│  │      FilledQuantity = paperOrder.FilledQuantity,                    │   │
│  │      AveragePrice = paperOrder.AverageFilledPrice                   │   │
│  │  };                                                                 │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.3 Paper 模拟撮合逻辑

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          SimulateFillAsync                                   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  1. GTD 过期检查                                                    │   │
│  │                                                                     │   │
│  │  if (order.TimeInForce == GTD && order.GoodTilDateUtc < now)        │   │
│  │  {                                                                  │   │
│  │      order.Status = Expired;                                        │   │
│  │      return;                                                        │   │
│  │  }                                                                  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  2. 获取订单簿 Top-of-Book                                          │   │
│  │                                                                     │   │
│  │  var topOfBook = _orderBookReader.GetTopOfBook(order.TokenId);      │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│              ┌─────────────────────┴─────────────────────┐                  │
│              │                                           │                  │
│        有订单簿                                      无订单簿                │
│              │                                           │                  │
│              ▼                                           ▼                  │
│  ┌─────────────────────────┐              ┌─────────────────────────┐      │
│  │  基于订单簿判断成交      │              │  使用默认成交率          │      │
│  │                         │              │                         │      │
│  │  Buy 订单:              │              │  canFill = random()     │      │
│  │  canFill = (限价 ≥ Ask) │              │    < DefaultFillRate    │      │
│  │  fillPrice = Ask        │              │                         │      │
│  │                         │              │  fillPrice = 限价       │      │
│  │  Sell 订单:             │              │                         │      │
│  │  canFill = (限价 ≤ Bid) │              │                         │      │
│  │  fillPrice = Bid        │              │                         │      │
│  └─────────────────────────┘              └─────────────────────────┘      │
│              │                                           │                  │
│              └─────────────────────┬─────────────────────┘                  │
│                                    │                                        │
│              ┌─────────────────────┴─────────────────────┐                  │
│              │                                           │                  │
│        canFill = true                              canFill = false          │
│              │                                           │                  │
│              ▼                                           ▼                  │
│  ┌─────────────────────────┐              ┌─────────────────────────┐      │
│  │  3. 应用滑点            │              │  FAK/FOK: 取消          │      │
│  │                         │              │  GTC/GTD: 保持挂单      │      │
│  │  fillPrice *= (1 ±      │              │                         │      │
│  │    SlippageBps/10000)   │              │  return; // 不成交      │      │
│  │                         │              │                         │      │
│  │  fillPrice = Clamp(     │              │                         │      │
│  │    fillPrice, 0.01, 0.99)│              │                         │      │
│  └─────────────────────────┘              └─────────────────────────┘      │
│              │                                                              │
│              ▼                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  4. 决定成交数量                                                    │   │
│  │                                                                     │   │
│  │  FOK: fillQuantity = 全部 (否则取消)                                │   │
│  │  FAK: fillQuantity = 全部或部分 (剩余取消)                          │   │
│  │  GTC/GTD: fillQuantity = 全部或部分                                 │   │
│  │                                                                     │   │
│  │  部分成交概率: PartialFillProbability (默认 0.1)                    │   │
│  │  部分成交比例: MinPartialFillRatio ~ MaxPartialFillRatio            │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│              │                                                              │
│              ▼                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  5. 更新订单状态                                                    │   │
│  │                                                                     │   │
│  │  order.FilledQuantity = fillQuantity;                               │   │
│  │  order.AverageFilledPrice = fillPrice;                              │   │
│  │                                                                     │   │
│  │  if (fillQuantity >= originalQuantity)                              │   │
│  │      order.Status = Filled;                                         │   │
│  │  else if (fillQuantity > 0)                                         │   │
│  │      order.Status = PartiallyFilled; // 或 Cancelled (FAK)          │   │
│  │                                                                     │   │
│  │  → 日志: "[Paper] 订单成交: FilledQty=5, FillPrice=0.65, ..."       │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│              │                                                              │
│              ▼                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  6. 通知 & 审计                                                     │   │
│  │                                                                     │   │
│  │  await _stateTracker.OnOrderStateChangedAsync(...);                 │   │
│  │  await _auditLogger.LogOrderFilledAsync(...);                       │   │
│  │  await _auditLogger.LogTradeAsync(...);                             │   │
│  │                                                                     │   │
│  │  → 写入 OrderEvents 表                                              │   │
│  │  → 写入 Trades 表                                                   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.4 TimeInForce 处理

| TimeInForce | 含义 | Paper 模式行为 |
|-------------|------|----------------|
| `GTC` | Good Till Cancelled | 挂单直到成交或取消 |
| `GTD` | Good Till Date | 挂单直到指定时间或成交/取消 |
| `FOK` | Fill Or Kill | 必须全部成交，否则全部取消 |
| `FAK` | Fill And Kill | 尽量成交，剩余立即取消 |

---

## 4. 风控系统

### 4.1 风控检查流程

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              RiskManager                                     │
│                                                                             │
│  CanPlaceOrderAsync(request):                                               │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  1. KillSwitch 检查                                                 │   │
│  │                                                                     │   │
│  │  if (_killSwitchTriggered)                                          │   │
│  │      return RiskCheckResult.Reject("KILLSWITCH_ACTIVE");            │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  2. 资本限制检查                                                    │   │
│  │                                                                     │   │
│  │  var orderNotional = request.Price * request.Quantity;              │   │
│  │                                                                     │   │
│  │  // 单笔订单限制                                                    │   │
│  │  if (orderNotional > MaxNotionalPerOrder)                           │   │
│  │      return Reject("ORDER_TOO_LARGE");                              │   │
│  │                                                                     │   │
│  │  // 单市场敞口限制                                                  │   │
│  │  var marketExposure = GetMarketExposure(request.MarketId);          │   │
│  │  if (marketExposure + orderNotional > MaxNotionalPerMarket)         │   │
│  │      return Reject("MARKET_EXPOSURE_EXCEEDED");                     │   │
│  │                                                                     │   │
│  │  // 总敞口限制                                                      │   │
│  │  var totalExposure = GetTotalExposure();                            │   │
│  │  if (totalExposure + orderNotional > MaxNotional)                   │   │
│  │      return Reject("TOTAL_EXPOSURE_EXCEEDED");                      │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  3. 每日亏损限制检查                                                │   │
│  │                                                                     │   │
│  │  var dailyLoss = GetDailyLoss();                                    │   │
│  │  if (dailyLoss > MaxDailyLoss)                                      │   │
│  │      return Reject("DAILY_LOSS_EXCEEDED");                          │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  4. 返回允许                                                        │   │
│  │                                                                     │   │
│  │  return RiskCheckResult.Allow();                                    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 KillSwitch 机制

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            KillSwitchService                                 │
│                         (IHostedService 后台服务)                            │
│                                                                             │
│  触发方式:                                                                   │
│                                                                             │
│  1. CLI 命令:                                                               │
│     dotnet run --project Autotrade.Cli -- killswitch trigger                │
│                                                                             │
│  2. 配置文件变更 (热更新):                                                   │
│     {                                                                       │
│       "Risk": {                                                             │
│         "KillSwitch": {                                                     │
│           "Token": "new_random_token"  ← 修改 Token 触发                    │
│         }                                                                   │
│       }                                                                     │
│     }                                                                       │
│                                                                             │
│  3. 风控阈值触发:                                                            │
│     - MaxDailyLoss 超限                                                     │
│     - 连续错误次数超限                                                       │
│                                                                             │
│  触发效果:                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  1. 拒绝所有新订单                                                  │   │
│  │  2. (可选) 取消所有活跃订单                                         │   │
│  │  3. (可选) 平掉所有持仓                                             │   │
│  │  4. 记录风控事件到 RiskEventLogs                                    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  重置方式:                                                                   │
│  dotnet run --project Autotrade.Cli -- killswitch reset                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.3 未对冲敞口监控

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                       UnhedgedExposureWatchdog                               │
│                      (IHostedService 后台服务)                               │
│                                                                             │
│  监控场景:                                                                   │
│  - 双腿套利策略入场时，先成交了一腿 (如 YES)                                  │
│  - 对冲腿 (NO) 还在挂单等待成交                                              │
│  - 如果超时未成交，形成"单腿敞口"                                             │
│                                                                             │
│  处理流程:                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  定期检查所有 TradingAccount:                                      │   │
│  │                                                                     │   │
│  │  foreach (var agg in GetOpenAggregates())                           │   │
│  │  {                                                                  │   │
│  │      if (agg.HasUnhedgedLeg &&                                      │   │
│  │          agg.UnhedgedDuration > HedgeTimeoutSeconds)                │   │
│  │      {                                                              │   │
│  │          // 超时处理                                                │   │
│  │          switch (UnhedgedExitAction)                                │   │
│  │          {                                                          │   │
│  │              case CancelAndExit:                                    │   │
│  │                  // 取消挂单，平掉已成交的腿                         │   │
│  │                  break;                                             │   │
│  │              case ForceHedge:                                       │   │
│  │                  // 以市价强制成交对冲腿                             │   │
│  │                  break;                                             │   │
│  │          }                                                          │   │
│  │      }                                                              │   │
│  │  }                                                                  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 5. 审计与持久化

### 5.1 订单审计日志

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           OrderAuditLogger                                   │
│                                                                             │
│  记录事件类型:                                                               │
│                                                                             │
│  ┌─────────────────┬─────────────────────────────────────────────────┐     │
│  │   事件类型      │              记录时机                            │     │
│  ├─────────────────┼─────────────────────────────────────────────────┤     │
│  │ OrderAccepted   │ 订单被接受                                      │     │
│  │ OrderRejected   │ 订单被拒绝 (验证失败/风控拒绝)                   │     │
│  │ OrderFilled     │ 订单成交 (全部或部分)                           │     │
│  │ OrderCancelled  │ 订单被取消                                      │     │
│  │ OrderExpired    │ GTD 订单到期                                    │     │
│  └─────────────────┴─────────────────────────────────────────────────┘     │
│                                                                             │
│  写入表: OrderEvents                                                        │
│                                                                             │
│  public async Task LogOrderAcceptedAsync(                                   │
│      Guid orderId,                                                          │
│      string clientOrderId,                                                  │
│      string strategyId,                                                     │
│      string marketId,                                                       │
│      string exchangeOrderId,                                                │
│      string? correlationId,                                                 │
│      CancellationToken ct)                                                  │
│  {                                                                          │
│      var orderEvent = new OrderEvent(                                       │
│          orderId,                                                           │
│          OrderEventType.Accepted,                                           │
│          DateTimeOffset.UtcNow,                                             │
│          $"Order accepted: {exchangeOrderId}");                             │
│                                                                             │
│      await _orderEventRepository.AddAsync(orderEvent, ct);                  │
│  }                                                                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 成交审计日志

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Trade 记录                                         │
│                                                                             │
│  写入表: Trades                                                              │
│                                                                             │
│  public async Task LogTradeAsync(                                           │
│      Guid orderId,                                                          │
│      Guid tradeAggregateId,                                                 │
│      string clientOrderId,                                                  │
│      string strategyId,                                                     │
│      string marketId,                                                       │
│      string tokenId,                                                        │
│      string outcome,                                                        │
│      OrderSide side,                                                        │
│      decimal price,                                                         │
│      decimal quantity,                                                      │
│      string tradeId,                                                        │
│      decimal fee,                                                           │
│      string? correlationId,                                                 │
│      CancellationToken ct)                                                  │
│  {                                                                          │
│      var trade = new Trade(                                                 │
│          orderId,                                                           │
│          tradeAggregateId,                                                  │
│          marketId,                                                          │
│          tokenId,                                                           │
│          outcome,                                                           │
│          side,                                                              │
│          price,                                                             │
│          quantity,                                                          │
│          tradeId,                                                           │
│          fee,                                                               │
│          DateTimeOffset.UtcNow);                                            │
│                                                                             │
│      await _tradeRepository.AddAsync(trade, ct);                            │
│  }                                                                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.3 数据库表结构

```sql
-- 订单表 (Live 模式使用)
CREATE TABLE "Orders" (
    "Id" UUID PRIMARY KEY,
    "ClientOrderId" VARCHAR(64) NOT NULL,
    "ExchangeOrderId" VARCHAR(128),
    "StrategyId" VARCHAR(64),
    "MarketId" VARCHAR(64) NOT NULL,
    "TokenId" VARCHAR(128) NOT NULL,
    "Outcome" VARCHAR(16) NOT NULL,
    "Side" VARCHAR(8) NOT NULL,
    "Price" DECIMAL(18,8) NOT NULL,
    "OriginalQuantity" DECIMAL(18,8) NOT NULL,
    "FilledQuantity" DECIMAL(18,8) NOT NULL DEFAULT 0,
    "Status" VARCHAR(32) NOT NULL,
    "TimeInForce" VARCHAR(8) NOT NULL,
    "CreatedAtUtc" TIMESTAMPTZ NOT NULL,
    "UpdatedAtUtc" TIMESTAMPTZ,
    "RowVersion" BYTEA  -- 乐观并发控制
);

-- 订单事件表
CREATE TABLE "OrderEvents" (
    "Id" UUID PRIMARY KEY,
    "OrderId" UUID NOT NULL,
    "EventType" VARCHAR(32) NOT NULL,
    "TimestampUtc" TIMESTAMPTZ NOT NULL,
    "Details" TEXT
);

-- 成交表
CREATE TABLE "Trades" (
    "Id" UUID PRIMARY KEY,
    "OrderId" UUID NOT NULL,
    "TradingAccountId" UUID,
    "MarketId" VARCHAR(64) NOT NULL,
    "TokenId" VARCHAR(128) NOT NULL,
    "Outcome" VARCHAR(16) NOT NULL,
    "Side" VARCHAR(8) NOT NULL,
    "Price" DECIMAL(18,8) NOT NULL,
    "Quantity" DECIMAL(18,8) NOT NULL,
    "TradeId" VARCHAR(128) NOT NULL,
    "Fee" DECIMAL(18,8) NOT NULL DEFAULT 0,
    "ExecutedAtUtc" TIMESTAMPTZ NOT NULL
);

-- 持仓表
CREATE TABLE "Positions" (
    "Id" UUID PRIMARY KEY,
    "StrategyId" VARCHAR(64) NOT NULL,
    "MarketId" VARCHAR(64) NOT NULL,
    "TokenId" VARCHAR(128) NOT NULL,
    "Outcome" VARCHAR(16) NOT NULL,
    "Quantity" DECIMAL(18,8) NOT NULL,
    "AvgEntryPrice" DECIMAL(18,8) NOT NULL,
    "UnrealizedPnl" DECIMAL(18,8),
    "UpdatedAtUtc" TIMESTAMPTZ NOT NULL
);

-- 风控事件表
CREATE TABLE "RiskEventLogs" (
    "Id" UUID PRIMARY KEY,
    "EventType" VARCHAR(64) NOT NULL,
    "Severity" VARCHAR(16) NOT NULL,
    "Details" TEXT,
    "TimestampUtc" TIMESTAMPTZ NOT NULL
);
```

---

## 6. 配置项说明

### 6.1 Execution 配置

```json
{
  "Execution": {
    "Mode": "Paper",                    // Paper 或 Live
    "IdempotencyTtlSeconds": 300,       // 幂等键 TTL
    "MaxOrdersPerMinute": 60,           // 每分钟最大订单数
    "MaxOrdersPerMarket": 10            // 每市场最大活跃订单数
  }
}
```

### 6.2 PaperTrading 配置

```json
{
  "PaperTrading": {
    "SimulatedLatencyMs": 0,            // 模拟延迟
    "DefaultFillRate": 0.8,             // 无订单簿时默认成交率
    "SlippageBps": 5,                   // 滑点 (基点)
    "PartialFillProbability": 0.1,      // 部分成交概率
    "MinPartialFillRatio": 0.3,         // 最小部分成交比例
    "MaxPartialFillRatio": 0.8,         // 最大部分成交比例
    "DeterministicSeed": null           // 随机种子 (测试用)
  }
}
```

### 6.3 Risk 配置

```json
{
  "Risk": {
    "MaxNotional": 1000,                // 总敞口上限
    "MaxNotionalPerMarket": 100,        // 单市场敞口上限
    "MaxNotionalPerOrder": 50,          // 单笔订单上限
    "MaxDailyLoss": 100,                // 每日最大亏损
    "HedgeTimeoutSeconds": 30,          // 对冲超时
    "UnhedgedExitAction": "CancelAndExit", // CancelAndExit 或 ForceHedge
    
    "KillSwitch": {
      "Enabled": true,
      "Token": "initial_token"          // 修改此值触发 KillSwitch
    }
  }
}
```

---

## 7. Live 模式详解

### 7.1 Live 模式额外功能

| 功能 | Paper 模式 | Live 模式 |
|------|------------|-----------|
| 真实下单 | ✗ | ✓ |
| 订单持久化 | ✗ (内存) | ✓ (数据库) |
| 订单对账 | ✗ | ✓ |
| 持仓同步 | ✗ | ✓ |
| API 签名 | ✗ | ✓ |

### 7.2 Live 模式配置要求

```json
{
  "Execution": {
    "Mode": "Live"
  },
  "Polymarket": {
    "Clob": {
      "PrivateKey": "0x...",            // 钱包私钥
      "ApiKey": "xxx",                  // API Key
      "ApiSecret": "xxx",               // API Secret
      "ApiPassphrase": "xxx",           // API Passphrase
      "UseServerTime": true
    }
  }
}
```

### 7.3 订单对账服务

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      OrderReconciliationService                              │
│                      (Live 模式后台服务)                                     │
│                                                                             │
│  定期同步:                                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  1. 查询本地所有活跃订单 (状态 != Filled/Cancelled/Expired)         │   │
│  │                                                                     │   │
│  │  2. 批量查询交易所订单状态                                          │   │
│  │     await _clobClient.GetOrdersAsync(orderIds);                     │   │
│  │                                                                     │   │
│  │  3. 对比状态差异                                                    │   │
│  │     - 交易所已成交 → 本地未更新 → 补录成交                          │   │
│  │     - 交易所已取消 → 本地未更新 → 标记取消                          │   │
│  │     - 交易所无此订单 → 本地有 → 标记异常                            │   │
│  │                                                                     │   │
│  │  4. 更新本地数据库                                                  │   │
│  │                                                                     │   │
│  │  5. 记录对账审计日志                                                │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 8. 常见问题排查

### 8.1 订单未成交 (Paper 模式)

**可能原因**：
1. 限价不满足订单簿条件 (买单价 < 卖一价)
2. 订单簿数据未同步
3. DefaultFillRate 设置过低

**排查步骤**：
1. 检查日志中是否有 `[Paper] 订单已接受` 但没有 `订单成交`
2. 确认订单簿已同步 (`开始同步 N 个资产的订单簿`)
3. 检查限价与 BestAsk/BestBid 的关系

### 8.2 幂等性冲突

**现象**：
- 日志显示 `IDEMPOTENCY_CONFLICT`
- 相同 ClientOrderId 返回不同结果

**排查步骤**：
1. 确认 ClientOrderId 生成逻辑是否唯一
2. 检查 IdempotencyTtlSeconds 是否足够长
3. 确认没有并发使用相同 ClientOrderId

### 8.3 风控拒绝下单

**现象**：
- 日志显示 `ORDER_LIMIT_EXCEEDED` 或 `TOTAL_EXPOSURE_EXCEEDED`

**排查步骤**：
1. 检查当前敞口: `dotnet run -- positions`
2. 检查风控配置: `MaxNotional`, `MaxNotionalPerMarket`
3. 确认 KillSwitch 未触发

### 8.4 KillSwitch 误触发

**排查步骤**：
1. 检查日志中 `RiskEventLog` 相关记录
2. 查询数据库: `SELECT * FROM "RiskEventLogs" ORDER BY "TimestampUtc" DESC`
3. 检查配置文件中 `KillSwitch:Token` 是否被意外修改

**重置方法**：
```bash
dotnet run --project Autotrade.Cli -- killswitch reset
```
