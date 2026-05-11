# Autotrade.Polymarket

Polymarket CLOB REST API 客户端库，为 Autotrade 系统提供与 Polymarket 交易所的通信能力。

## 概览

本库封装了 Polymarket CLOB（Central Limit Order Book）的完整 REST API，提供：

- **类型安全的 API 客户端** - 完整的请求/响应模型映射
- **EIP-712 签名支持** - L1/L2 两级身份验证
- **HttpClientFactory 集成** - 生产级 HTTP 客户端管理
- **Polly 弹性策略** - 重试、熔断、超时等
- **速率限制** - 符合官方 API 限流要求
- **可观测性** - Metrics 指标和日志记录

## 架构

```
Autotrade.Polymarket/
├── Abstractions/                    # 接口抽象
│   └── IPolymarketClobClient.cs    # CLOB 客户端接口（便于 Mock）
├── Http/                            # HTTP 管道组件
│   ├── PolymarketAuthHandler.cs    # L1/L2 鉴权处理器
│   ├── PolymarketAuthHeaderFactory.cs # HMAC 签名生成
│   ├── PolymarketRateLimitHandler.cs  # 速率限制处理器
│   ├── PolymarketLoggingHandler.cs    # 日志记录处理器
│   ├── PolymarketMetricsHandler.cs    # 指标收集处理器
│   └── PolymarketIdempotencyHandler.cs # 幂等性处理器
├── Models/                          # API 数据模型
│   ├── OrderRequest.cs             # 订单请求
│   ├── OrderInfo.cs                # 订单详情
│   ├── MarketInfo.cs               # 市场信息
│   ├── OrderBookSummary.cs         # 订单簿摘要
│   ├── TradeInfo.cs                # 成交记录
│   └── ...                         # 其他模型
├── Options/                         # 配置选项
│   ├── PolymarketClobOptions.cs    # CLOB 客户端配置
│   ├── PolymarketResilienceOptions.cs # 弹性策略配置
│   └── PolymarketRateLimitOptions.cs  # 限流配置
├── Observability/                   # 可观测性
│   └── PolymarketMetrics.cs        # Prometheus 指标
├── Extensions/                      # 扩展方法
│   └── PolymarketServiceCollectionExtensions.cs # DI 注册
├── PolymarketClobClient.cs         # CLOB 客户端实现
├── PolymarketClobEndpoints.cs      # API 端点常量
└── PolymarketConstants.cs          # 协议常量
```

## 快速开始

### 1. 注册服务

```csharp
// Program.cs 或 Startup.cs
using Autotrade.Polymarket.Extensions;

services.AddPolymarketClobClient(configuration);
```

### 2. 配置 (appsettings.json)

```json
{
  "Polymarket": {
    "Clob": {
      "Host": "https://clob.polymarket.com",
      "ChainId": 137,
      "PrivateKey": "<your_private_key>",
      "ApiKey": "<your_api_key>",
      "ApiSecret": "<your_api_secret>",
      "ApiPassphrase": "<your_api_passphrase>",
      "Timeout": "00:00:10",
      "UseServerTime": false
    },
    "Resilience": {
      "RetryCount": 3,
      "RetryDelayMs": 500,
      "CircuitBreakerFailureThreshold": 5,
      "CircuitBreakerDurationSeconds": 30
    },
    "RateLimit": {
      "OrdersPerTenSeconds": 500,
      "OrdersPerTenMinutes": 3000
    }
  }
}
```

> ⚠️ **安全提示**：敏感配置（PrivateKey、ApiKey、ApiSecret、ApiPassphrase）应通过 User Secrets 或环境变量注入，不要提交到代码仓库。

### 3. 使用客户端

```csharp
public class MyService
{
    private readonly IPolymarketClobClient _client;

    public MyService(IPolymarketClobClient client)
    {
        _client = client;
    }

    public async Task PlaceOrderAsync()
    {
        // 获取订单簿
        var orderBook = await _client.GetOrderBookAsync("token-id-here");
        if (!orderBook.IsSuccess)
        {
            Console.WriteLine($"Error: {orderBook.Error?.Message}");
            return;
        }

        // 下单
        var request = new OrderRequest
        {
            TokenId = "token-id-here",
            Side = "BUY",
            Price = "0.50",
            Size = "10",
            Type = "GTC"
        };

        var result = await _client.PlaceOrderAsync(request, idempotencyKey: Guid.NewGuid().ToString());
        if (result.IsSuccess)
        {
            Console.WriteLine($"Order placed: {result.Data?.OrderId}");
        }
    }
}
```

## API 参考

### IPolymarketClobClient 接口

| 类别       | 方法                         | 说明                           |
| ---------- | ---------------------------- | ------------------------------ |
| **服务器** | `GetServerTimeAsync()`       | 获取服务器时间（用于签名同步） |
| **认证**   | `CreateApiKeyAsync()`        | 创建 API Key                   |
|            | `DeriveApiKeyAsync()`        | 派生 API Key                   |
|            | `GetApiKeysAsync()`          | 获取所有 API Key               |
|            | `DeleteApiKeyAsync()`        | 删除 API Key                   |
| **市场**   | `GetMarketsAsync()`          | 获取所有市场（分页）           |
|            | `GetMarketAsync()`           | 获取单个市场详情               |
| **订单簿** | `GetOrderBookAsync()`        | 获取指定代币的订单簿           |
| **订单**   | `PlaceOrderAsync()`          | 下单（需 L2 认证）             |
|            | `CancelOrderAsync()`         | 取消订单                       |
|            | `CancelAllOrdersAsync()`     | 取消所有订单                   |
|            | `GetOrderAsync()`            | 获取订单详情                   |
|            | `GetOpenOrdersAsync()`       | 获取所有挂单                   |
| **交易**   | `GetTradesAsync()`           | 获取成交记录                   |
| **余额**   | `GetBalanceAllowanceAsync()` | 获取账户余额和授权             |

### 认证级别

| 级别     | 说明               | 适用场景                     |
| -------- | ------------------ | ---------------------------- |
| **None** | 无需认证           | 公开数据（市场信息、订单簿） |
| **L1**   | 钱包签名 (EIP-712) | 创建/派生 API Key            |
| **L2**   | HMAC 签名          | 订单操作、账户数据           |

### 订单类型

| 类型  | 说明                                |
| ----- | ----------------------------------- |
| `GTC` | Good-Til-Canceled，持续有效直到取消 |
| `GTD` | Good-Til-Date，指定时间后过期       |
| `FOK` | Fill-Or-Kill，全部成交或全部取消    |
| `FAK` | Fill-And-Kill，部分成交后取消剩余   |

## HTTP 管道

请求处理顺序（从外到内）：

```
Request → Metrics → Logging → RateLimit → Idempotency → Auth → HttpClient → Polymarket API
```

| 处理器                         | 职责                                   |
| ------------------------------ | -------------------------------------- |
| `PolymarketMetricsHandler`     | 记录请求延迟、状态码等 Prometheus 指标 |
| `PolymarketLoggingHandler`     | 记录请求/响应日志                      |
| `PolymarketRateLimitHandler`   | 实施 API 速率限制                      |
| `PolymarketIdempotencyHandler` | 添加幂等性 Key 头                      |
| `PolymarketAuthHandler`        | 添加 L1/L2 认证签名                    |

## 弹性策略

客户端内置 Polly 策略：

- **重试**：瞬时错误自动重试（可配置次数和延迟）
- **熔断**：连续失败后暂时阻断请求
- **超时**：防止请求无限等待

幂等请求（GET、DELETE）使用完整弹性策略，非幂等请求（POST）仅使用超时策略。

## 速率限制

遵循 Polymarket 官方 API 限制：

| 端点        | 限制                          |
| ----------- | ----------------------------- |
| POST /order | 500 次/10 秒，3000 次/10 分钟 |
| 其他端点    | 按官方文档限制                |

超过限制时抛出 `PolymarketRateLimitRejectedException`。

## 依赖

- .NET 10.0
- Microsoft.Extensions.Http
- Nethereum.Signer (EIP-712 签名)
- Polly (弹性策略)
- System.Threading.RateLimiting

## 参考资料

- [Polymarket CLOB API 文档](https://docs.polymarket.com)
- [py-clob-client (Python 官方客户端)](https://github.com/polymarket/py-clob-client)
- [clob-client (TypeScript 官方客户端)](https://github.com/polymarket/clob-client)
