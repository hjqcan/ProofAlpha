# Trading Bounded Context

Trading 限界上下文负责订单执行、持仓管理和交易账户操作。

## 架构概览

```
Trading/
├── Autotrade.Trading.Application/             # 应用服务层
│   └── Execution/                             # 执行引擎
│       ├── IIdempotencyStore.cs              # 幂等性存储接口
│       ├── InMemoryIdempotencyStore.cs       # 内存幂等性实现
│       ├── IOrderStateTracker.cs             # 订单状态跟踪接口
│       ├── InMemoryOrderStateTracker.cs      # 内存状态跟踪实现
│       ├── OrderLimitValidator.cs            # 挂单数量限制验证
│       ├── LiveExecutionService.cs           # 实盘执行服务
│       ├── PaperExecutionService.cs          # 模拟执行服务
│       ├── PaperTradingOptions.cs            # 模拟交易配置
│       ├── TimeInForceHandler.cs             # 订单时效处理
│       ├── OrderReconciliationService.cs     # 订单对账服务
│       └── ExecutionModeFactory.cs           # 执行模式工厂
├── Autotrade.Trading.Application.Contract/    # 契约层
│   └── Execution/                             # 执行契约
│       ├── IExecutionService.cs              # 执行服务接口
│       ├── ExecutionRequest.cs               # 执行请求 DTO
│       ├── ExecutionResult.cs                # 执行结果 DTO
│       ├── OrderStatusResult.cs              # 订单状态结果
│       └── ExecutionOptions.cs               # 执行配置
├── Autotrade.Trading.Domain/                  # 领域层
├── Autotrade.Trading.Domain.Shared/           # 共享领域对象
│   ├── Enums/                                 # 枚举
│   │   ├── OrderSide.cs                      # 买卖方向
│   │   ├── OrderType.cs                      # 订单类型
│   │   └── TimeInForce.cs                    # 订单时效
│   └── ValueObjects/                          # 值对象
├── Autotrade.Trading.Infra.Data/              # 数据持久化
├── Autotrade.Trading.Infra.CrossCutting.IoC/  # 依赖注入
└── Autotrade.Trading.Tests/                   # 单元测试
```

## 核心组件

### 1. 执行服务 (`IExecutionService`)

统一的订单执行接口，支持 Live 和 Paper 两种模式。

```csharp
public interface IExecutionService
{
    Task<ExecutionResult> PlaceOrderAsync(ExecutionRequest request, CancellationToken ct);
    Task<ExecutionResult> CancelOrderAsync(string clientOrderId, CancellationToken ct);
    Task<OrderStatusResult> GetOrderStatusAsync(string clientOrderId, CancellationToken ct);
}
```

### 2. 执行状态 (`ExecutionStatus`)

```
Pending → Accepted → PartiallyFilled → Filled
                                     ↘ Cancelled
                                     ↘ Rejected
                                     ↘ Expired
```

### 3. 时效类型 (`TimeInForce`)

| 类型 | 说明              | 终态行为                            |
| ---- | ----------------- | ----------------------------------- |
| GTC  | Good-Til-Canceled | 持续有效直到取消或成交              |
| GTD  | Good-Til-Date     | 到期自动过期 (→Expired)             |
| FAK  | Fill-And-Kill     | 立即部分成交后取消剩余 (→Cancelled) |
| FOK  | Fill-Or-Kill      | 全部成交或全部取消 (→Cancelled)     |

### 4. 幂等性保障 (`IIdempotencyStore`)

- 防止重复订单提交
- Client Order ID → Exchange Order ID 映射
- TTL 自动过期
- 支持 `RemoveAsync` 用于失败后清理

### 5. 订单限制 (`OrderLimitValidator`)

- 按市场限制最大挂单数量
- 可配置 `MaxOpenOrdersPerMarket`
- 与 `IOrderStateTracker` 配合追踪挂单计数

### 6. 状态跟踪 (`IOrderStateTracker`)

- 追踪订单状态变化
- 维护每个市场的挂单计数
- 支持跨组件状态同步

## 配置

```json
{
  "Execution": {
    "Mode": "Paper",
    "DefaultTimeoutSeconds": 30,
    "MaxOpenOrdersPerMarket": 10,
    "IdempotencyTtlSeconds": 3600,
    "EnableReconciliation": true,
    "ReconciliationIntervalSeconds": 60
  },
  "PaperTrading": {
    "SlippageBps": 10,
    "PartialFillProbability": 0.0,
    "SimulatedLatencyMs": 50,
    "DefaultFillRate": 0.8
  }
}
```

## Market 订单支持

> [!IMPORTANT]
> Polymarket CLOB 仅支持 Limit 订单。
> 提交 `OrderType.Market` 请求将被验证拒绝。

## 使用示例

```csharp
public class TradingStrategy
{
    private readonly IExecutionService _execution;

    public async Task PlaceOrder()
    {
        var result = await _execution.PlaceOrderAsync(new ExecutionRequest
        {
            ClientOrderId = Guid.NewGuid().ToString(),
            MarketId = "market-123",
            TokenId = "token-456",
            Outcome = OutcomeSide.Yes,
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,  // 必须是 Limit
            TimeInForce = TimeInForce.Gtc,
            Price = 0.45m,
            Quantity = 100m
        });

        if (result.Success)
        {
            Console.WriteLine($"Order placed: {result.ExchangeOrderId}");
        }
    }
}
```
