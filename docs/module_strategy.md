# Strategy 模块详细设计

本文档详细描述 Strategy 模块的内部实现、策略生命周期、决策流程和扩展点。

---

## 目录

1. [模块概述](#1-模块概述)
2. [核心组件](#2-核心组件)
3. [策略生命周期](#3-策略生命周期)
4. [数据分发机制](#4-数据分发机制)
5. [内置策略详解](#5-内置策略详解)
6. [决策日志与审计](#6-决策日志与审计)
7. [配置项说明](#7-配置项说明)
8. [扩展：添加新策略](#8-扩展添加新策略)

---

## 1. 模块概述

Strategy 模块负责：

- **策略生命周期管理**：启动、停止、重启、状态监控
- **市场数据分发**：将 MarketData 的快照分发到各策略
- **策略执行**：调用策略决策逻辑，生成交易意图
- **决策审计**：记录策略决策到数据库

### 1.1 模块边界

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Strategy 模块                                  │
│                                                                         │
│  输入:                                                                   │
│  ├── MarketSnapshot (来自 MarketData)                                   │
│  ├── OrderStateUpdate (来自 Trading)                                    │
│  └── 配置 (StrategyEngine / Strategies:*)                               │
│                                                                         │
│  输出:                                                                   │
│  ├── TradeIntent (交易意图，发送到 Trading)                              │
│  ├── StrategyStatus (策略状态，暴露给 CLI)                               │
│  └── StrategyDecisionLog (决策日志，写入数据库)                          │
│                                                                         │
│  依赖:                                                                   │
│  ├── IMarketCatalog (查询市场元数据)                                     │
│  ├── IOrderBookReader (查询订单簿)                                       │
│  └── IExecutionService (下单)                                           │
└─────────────────────────────────────────────────────────────────────────┘
```

### 1.2 项目结构

```
context/Strategy/
├── Autotrade.Strategy.Application/
│   ├── Engine/
│   │   ├── IStrategyManager.cs            # 策略管理器接口
│   │   ├── StrategyManager.cs             # 策略管理器实现 (HostedService)
│   │   ├── StrategySupervisor.cs          # 单策略监督器
│   │   ├── StrategyRunner.cs              # 策略执行器
│   │   ├── StrategyMarketChannel.cs       # 市场快照通道
│   │   ├── StrategyDataRouter.cs          # 数据路由器
│   │   ├── StrategyRuntimeStore.cs        # 运行时状态存储
│   │   ├── StrategyEngineOptions.cs       # 引擎配置
│   │   └── IStrategyRunStateRepository.cs # 状态持久化接口
│   ├── Strategies/
│   │   ├── DualLeg/
│   │   │   ├── DualLegArbitrageStrategy.cs    # 双腿套利策略
│   │   │   ├── DualLegArbitrageOptions.cs     # 策略配置
│   │   │   └── DualLegState.cs                # 策略状态
│   │   └── Endgame/
│   │       ├── EndgameSweepStrategy.cs        # 到期扫货策略
│   │       └── EndgameSweepOptions.cs         # 策略配置
│   ├── Decisions/
│   │   ├── IStrategyDecisionLogger.cs     # 决策日志接口
│   │   ├── StrategyDecisionLogger.cs      # 决策日志实现
│   │   └── StrategyDecisionQueryService.cs# 决策查询服务
│   ├── Orders/
│   │   ├── StrategyOrderRegistry.cs       # 策略订单注册表
│   │   └── StrategyOrderUpdateChannel.cs  # 订单更新通道
│   ├── Audit/
│   │   ├── ICommandAuditLogger.cs         # 命令审计接口
│   │   └── CommandAuditLogger.cs          # 命令审计实现
│   └── Persistence/
│       ├── StrategyRetentionService.cs    # 数据保留服务
│       └── StrategyRetentionOptions.cs    # 保留配置
├── Autotrade.Strategy.Application.Contract/
│   └── Strategies/
│       ├── IStrategy.cs                   # 策略接口
│       ├── StrategyContext.cs             # 策略上下文
│       ├── StrategyDecision.cs            # 决策 DTO
│       ├── TradeIntent.cs                 # 交易意图 DTO
│       └── MarketSnapshot.cs              # 市场快照 DTO
├── Autotrade.Strategy.Domain/
│   └── Entities/
│       ├── StrategyDecisionLog.cs         # 决策日志实体
│       ├── StrategyRunState.cs            # 运行状态实体
│       └── CommandAuditLog.cs             # 命令审计实体
└── Autotrade.Strategy.Infra.Data/
    ├── Context/
    │   └── StrategyContext.cs             # EF 上下文
    └── Repositories/
        ├── StrategyDecisionRepository.cs
        ├── StrategyRunStateRepository.cs
        └── CommandAuditRepository.cs
```

---

## 2. 核心组件

### 2.1 组件关系图

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           StrategyManager                                    │
│                        (IHostedService 后台服务)                             │
│                                                                             │
│  职责:                                                                       │
│  - 管理所有策略的生命周期                                                    │
│  - 监控 DesiredStates 配置变化                                               │
│  - 创建/销毁 StrategySupervisor                                              │
│  - 持久化策略状态到 StrategyRunStates                                        │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                      StrategySupervisor (每策略一个)                 │   │
│  │                                                                     │   │
│  │  职责:                                                              │   │
│  │  - 监控单个策略的健康状态                                            │   │
│  │  - 处理策略崩溃和自动重启                                            │   │
│  │  - 汇报策略状态变更                                                  │   │
│  │                                                                     │   │
│  │  ┌─────────────────────────────────────────────────────────────┐   │   │
│  │  │                    StrategyRunner (每策略一个)               │   │   │
│  │  │                                                             │   │   │
│  │  │  职责:                                                       │   │   │
│  │  │  - 执行策略主循环                                            │   │   │
│  │  │  - 读取市场快照和订单更新                                     │   │   │
│  │  │  - 调用策略的 OnMarketSnapshotAsync()                        │   │   │
│  │  │  - 执行交易意图                                              │   │   │
│  │  │                                                             │   │   │
│  │  │  ┌─────────────────────────────────────────────────────┐   │   │   │
│  │  │  │              IStrategy 实现                          │   │   │   │
│  │  │  │  (DualLegArbitrageStrategy / EndgameSweepStrategy)  │   │   │   │
│  │  │  │                                                     │   │   │   │
│  │  │  │  职责:                                               │   │   │   │
│  │  │  │  - 实现具体的交易决策逻辑                             │   │   │   │
│  │  │  │  - 维护策略内部状态                                   │   │   │   │
│  │  │  │  - 返回 TradeIntent 列表                              │   │   │   │
│  │  │  └─────────────────────────────────────────────────────┘   │   │   │
│  │  └─────────────────────────────────────────────────────────────┘   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 核心接口

#### IStrategy

```csharp
public interface IStrategy
{
    /// <summary>策略唯一标识</summary>
    string StrategyId { get; }
    
    /// <summary>策略显示名称</summary>
    string Name { get; }
    
    /// <summary>处理市场快照，返回交易意图</summary>
    Task<IReadOnlyList<TradeIntent>> OnMarketSnapshotAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken);
    
    /// <summary>处理订单状态更新</summary>
    Task OnOrderUpdateAsync(
        StrategyOrderUpdate update,
        CancellationToken cancellationToken);
    
    /// <summary>获取策略关注的市场列表</summary>
    IReadOnlyList<string> GetActiveMarkets();
    
    /// <summary>启动策略</summary>
    Task StartAsync(CancellationToken cancellationToken);
    
    /// <summary>停止策略</summary>
    Task StopAsync(CancellationToken cancellationToken);
}
```

#### IStrategyManager

```csharp
public interface IStrategyManager
{
    /// <summary>获取所有策略状态</summary>
    Task<IReadOnlyList<StrategyStatus>> GetStatusesAsync(CancellationToken cancellationToken);
    
    /// <summary>启动指定策略</summary>
    Task<bool> StartStrategyAsync(string strategyId, CancellationToken cancellationToken);
    
    /// <summary>停止指定策略</summary>
    Task<bool> StopStrategyAsync(string strategyId, CancellationToken cancellationToken);
    
    /// <summary>重启指定策略</summary>
    Task<bool> RestartStrategyAsync(string strategyId, CancellationToken cancellationToken);
}
```

---

## 3. 策略生命周期

### 3.1 状态机

```
                              ┌──────────────────────────────────────┐
                              │                                      │
                              ▼                                      │
┌──────────┐   Register   ┌──────────┐   Start     ┌──────────┐     │
│  (none)  │─────────────►│ Created  │────────────►│ Starting │     │
└──────────┘              └──────────┘             └────┬─────┘     │
                                                        │           │
                                                        │ 启动成功  │
                                                        ▼           │
                                                  ┌──────────┐      │
                              ┌───────────────────│ Running  │◄─────┤
                              │                   └────┬─────┘      │
                              │                        │            │
                      ┌───────┴───────┐                │            │
                      ▼               ▼                │            │
                ┌──────────┐    ┌──────────┐          │            │
                │  Faulted │    │ Stopping │◄─────────┤ 主动停止   │
                └────┬─────┘    └────┬─────┘          │            │
                     │               │                │            │
       自动重启      │               │ 停止完成       │            │
    (RestartCount    │               ▼                │            │
     < MaxAttempts)  │         ┌──────────┐          │            │
                     │         │ Stopped  │          │            │
                     │         └──────────┘          │            │
                     │               │               │            │
                     │               │ 重新启动      │            │
                     └───────────────┴───────────────┘            │
                                     │                             │
                                     └─────────────────────────────┘
```

### 3.2 状态变更时机

| 当前状态 | 触发事件 | 新状态 | 动作 |
|----------|----------|--------|------|
| Created | Start 命令 | Starting | 创建 Runner，启动执行循环 |
| Starting | 启动成功 | Running | 开始处理市场快照 |
| Starting | 启动失败 | Faulted | 记录错误，等待重启 |
| Running | 正常停止 | Stopping | 优雅关闭 Runner |
| Running | 异常崩溃 | Faulted | 记录错误，触发重启逻辑 |
| Faulted | 重启次数 < Max | Starting | 等待 RestartDelaySeconds 后重启 |
| Faulted | 重启次数 ≥ Max | Faulted | 保持，需要人工干预 |
| Stopping | 停止完成 | Stopped | 清理资源 |
| Stopped | Start 命令 | Starting | 重新创建 Runner |

### 3.3 Supervisor 重启逻辑

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         StrategySupervisor                                   │
│                                                                             │
│  异常处理流程:                                                               │
│                                                                             │
│  try                                                                        │
│  {                                                                          │
│      await runner.RunAsync(cancellationToken);                              │
│  }                                                                          │
│  catch (OperationCanceledException)                                         │
│  {                                                                          │
│      // 正常取消，不重启                                                     │
│      SetState(Stopped);                                                     │
│  }                                                                          │
│  catch (Exception ex)                                                       │
│  {                                                                          │
│      SetState(Faulted, error: ex.Message);                                  │
│      RestartCount++;                                                        │
│                                                                             │
│      if (RestartCount <= MaxRestartAttempts)                                │
│      {                                                                      │
│          logger.LogWarning("Strategy {Id} faulted, restarting... ({Count}/{Max})",│
│              StrategyId, RestartCount, MaxRestartAttempts);                 │
│                                                                             │
│          await Task.Delay(RestartDelaySeconds * 1000);                      │
│          await StartAsync();  // 重新启动                                   │
│      }                                                                      │
│      else                                                                   │
│      {                                                                      │
│          logger.LogError("Strategy {Id} exceeded max restart attempts",    │
│              StrategyId);                                                   │
│          // 保持 Faulted 状态，等待人工干预                                  │
│      }                                                                      │
│  }                                                                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 4. 数据分发机制

### 4.1 Market Snapshot 分发

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│  MarketSnapshotProvider                    StrategyDataRouter               │
│  (组装快照)                               (分发快照)                        │
│                                                                             │
│  ┌─────────────┐                                                            │
│  │ MarketCatalog│                                                           │
│  │ (市场元数据) │──┐                                                         │
│  └─────────────┘  │                                                         │
│                   │ 组装                                                    │
│  ┌─────────────┐  │      ┌────────────────┐                                 │
│  │OrderBookStore│─┴─────►│ MarketSnapshot │                                 │
│  │ (Top-of-Book)│        └───────┬────────┘                                 │
│  └─────────────┘                 │                                          │
│                                  │ Broadcast()                              │
│                                  ▼                                          │
│                    ┌─────────────────────────┐                              │
│                    │   StrategyDataRouter    │                              │
│                    │                         │                              │
│                    │   _channels: Dict<      │                              │
│                    │     strategyId,         │                              │
│                    │     StrategyMarketChannel│                              │
│                    │   >                     │                              │
│                    └───────────┬─────────────┘                              │
│                                │                                            │
│            ┌───────────────────┼───────────────────┐                        │
│            │                   │                   │                        │
│            ▼                   ▼                   ▼                        │
│  ┌───────────────┐   ┌───────────────┐   ┌───────────────┐                 │
│  │StrategyMarket │   │StrategyMarket │   │StrategyMarket │                 │
│  │   Channel     │   │   Channel     │   │   Channel     │                 │
│  │(dual_leg_arb) │   │(endgame_sweep)│   │ (strategy_n)  │                 │
│  └───────┬───────┘   └───────┬───────┘   └───────┬───────┘                 │
│          │                   │                   │                          │
│          ▼                   ▼                   ▼                          │
│  ┌───────────────┐   ┌───────────────┐   ┌───────────────┐                 │
│  │StrategyRunner │   │StrategyRunner │   │StrategyRunner │                 │
│  └───────────────┘   └───────────────┘   └───────────────┘                 │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 StrategyMarketChannel 特性

```csharp
public sealed class StrategyMarketChannel : IDisposable
{
    private readonly Channel<MarketSnapshot> _channel;
    private readonly int _capacity;
    
    public StrategyMarketChannel(int capacity = 100)
    {
        _capacity = capacity;
        _channel = Channel.CreateBounded<MarketSnapshot>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,  // 队列满时丢弃最旧的
            SingleReader = true,                           // 只有一个 StrategyRunner 读取
            SingleWriter = false                           // 可能有多个写入者
        });
    }
    
    /// <summary>写入快照（非阻塞，队列满时丢弃最旧）</summary>
    public bool TryWrite(MarketSnapshot snapshot);
    
    /// <summary>批量读取快照</summary>
    public ValueTask<IReadOnlyList<MarketSnapshot>> ReadBatchAsync(
        int maxCount, CancellationToken cancellationToken);
    
    /// <summary>当前队列积压数量</summary>
    public int Count => _channel.Reader.Count;
}
```

### 4.3 订单更新分发

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         订单状态更新流程                                     │
│                                                                             │
│  Trading 模块                              Strategy 模块                    │
│                                                                             │
│  ┌───────────────────┐                                                      │
│  │ IOrderStateTracker│                                                      │
│  │                   │                                                      │
│  │ OnOrderStateChanged│                                                     │
│  │   Async()         │                                                      │
│  └─────────┬─────────┘                                                      │
│            │                                                                │
│            │ OrderStateUpdate                                               │
│            ▼                                                                │
│  ┌───────────────────┐                                                      │
│  │StrategyOrderRegistry                                                     │
│  │                   │                                                      │
│  │ 根据 StrategyId   │                                                      │
│  │ 路由到对应通道    │                                                      │
│  └─────────┬─────────┘                                                      │
│            │                                                                │
│            ▼                                                                │
│  ┌───────────────────┐                                                      │
│  │StrategyOrderUpdate│                                                      │
│  │     Channel       │                                                      │
│  │ (per-strategy)    │                                                      │
│  └─────────┬─────────┘                                                      │
│            │                                                                │
│            ▼                                                                │
│  ┌───────────────────┐                                                      │
│  │  StrategyRunner   │                                                      │
│  │                   │                                                      │
│  │  读取订单更新     │                                                      │
│  │  调用策略的       │                                                      │
│  │  OnOrderUpdateAsync│                                                     │
│  └───────────────────┘                                                      │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 5. 内置策略详解

### 5.1 DualLegArbitrageStrategy (双腿套利)

#### 策略原理

```
Polymarket 的二元市场特性:
- 每个市场有 YES 和 NO 两个 Token
- 无论结果如何，最终 YES + NO = $1.00

套利机会:
- 如果 YES_AskPrice + NO_AskPrice < $1.00
- 买入 YES + 买入 NO
- 到期时赎回 $1.00
- 利润 = $1.00 - (YES_Ask + NO_Ask) - 手续费

示例:
  YES_BestAsk = 0.52
  NO_BestAsk = 0.45
  PairCost = 0.97 < 1.00
  利润 = 0.03 ($3 per pair)
```

#### 决策流程

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     DualLegArbitrageStrategy.OnMarketSnapshotAsync()        │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  1. 市场筛选                                                        │   │
│  │                                                                     │   │
│  │     筛选条件:                                                       │   │
│  │     ✓ market.IsActive == true                                       │   │
│  │     ✓ market.Tokens.Count == 2                                      │   │
│  │     ✓ market.Liquidity >= MinLiquidity                              │   │
│  │     ✓ market.Volume24h >= MinVolume24h                              │   │
│  │     ✓ 两个 Token 都有有效的 BestAsk                                  │   │
│  │                                                                     │   │
│  │     排序: 按 Liquidity 降序                                         │   │
│  │     截取: 前 MaxMarkets 个                                          │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  2. 入场机会检测                                                    │   │
│  │                                                                     │   │
│  │     foreach (market in selectedMarkets)                             │   │
│  │     {                                                               │   │
│  │         var yesAsk = market.Tokens["Yes"].BestAsk;                  │   │
│  │         var noAsk = market.Tokens["No"].BestAsk;                    │   │
│  │         var pairCost = yesAsk + noAsk;                              │   │
│  │                                                                     │   │
│  │         if (pairCost < PairCostThreshold)  // 默认 0.98             │   │
│  │         {                                                           │   │
│  │             // 套利机会！                                           │   │
│  │             // 检查额外条件:                                        │   │
│  │             //   - 没有该市场的活跃持仓                              │   │
│  │             //   - 已过入场冷却期                                    │   │
│  │             //   - 名义金额不超限                                    │   │
│  │                                                                     │   │
│  │             intents.Add(CreateEntryIntent(market, ...));            │   │
│  │         }                                                           │   │
│  │     }                                                               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  3. 持仓管理                                                        │   │
│  │                                                                     │   │
│  │     foreach (position in activePositions)                           │   │
│  │     {                                                               │   │
│  │         // 检查出场条件:                                            │   │
│  │         //   - PairValue > ExitPairValueThreshold (默认 1.01)       │   │
│  │         //   - 持有时间 > MaxHoldSeconds                            │   │
│  │         //   - 单腿敞口超时 (HedgeTimeoutSeconds)                   │   │
│  │                                                                     │   │
│  │         if (shouldExit)                                             │   │
│  │         {                                                           │   │
│  │             intents.Add(CreateExitIntent(position, ...));           │   │
│  │         }                                                           │   │
│  │     }                                                               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│                                    ▼                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  4. 返回交易意图                                                    │   │
│  │                                                                     │   │
│  │     return intents;  // 可能为空                                    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### 配置项

```json
{
  "Strategies": {
    "DualLegArbitrage": {
      "Enabled": true,
      "ConfigVersion": "v1",
      
      // 入场阈值
      "PairCostThreshold": 0.98,       // YES_Ask + NO_Ask < 0.98 时入场
      
      // 出场阈值  
      "ExitPairValueThreshold": 1.01,  // 对价值 > 1.01 时考虑出场
      
      // 市场筛选
      "MinLiquidity": 1000,            // 最小流动性 (USDC)
      "MinVolume24h": 500,             // 最小24h成交量
      "MaxMarkets": 20,                // 最大关注市场数
      
      // 风控限制
      "MaxNotionalPerMarket": 50,      // 单市场最大敞口
      "MaxNotionalPerOrder": 10,       // 单笔订单最大金额
      "DefaultOrderQuantity": 5,       // 默认下单数量
      "MinOrderQuantity": 1,           // 最小下单数量
      
      // 时间控制
      "EntryCooldownSeconds": 30,      // 入场冷却期
      "MaxHoldSeconds": 3600,          // 最大持有时间
      "HedgeTimeoutSeconds": 30,       // 对冲腿超时
      
      // 滑点控制
      "MaxSlippage": 0.02              // 最大允许滑点
    }
  }
}
```

### 5.2 EndgameSweepStrategy (到期扫货)

#### 策略原理

```
接近到期的市场特点:
- 结果已经接近确定 (如 YES 概率 > 90%)
- 但价格还没有完全收敛到 1.00
- 存在微小利润空间

策略:
- 筛选即将到期 (如 15分钟内) 的市场
- 找到高确定性的一方 (如 YES > 0.95)
- 以较低价格 (如 0.98) 买入
- 等待到期赎回 $1.00

风险:
- 如果判断错误，损失接近全部本金
- 需要谨慎设置最小胜率阈值
```

#### 配置项

```json
{
  "Strategies": {
    "EndgameSweep": {
      "Enabled": false,
      "ConfigVersion": "v1",
      
      // 到期时间筛选
      "MaxSecondsToExpiry": 900,       // 最大离到期秒数 (15分钟)
      "MinSecondsToExpiry": 60,        // 最小离到期秒数 (防止来不及成交)
      
      // 市场筛选
      "MinLiquidity": 500,
      "MaxMarkets": 10,
      
      // 入场条件
      "MinWinProbability": 0.90,       // 最小胜率 (价格)
      "MaxEntryPrice": 0.98,           // 最高买入价
      "MinExpectedProfitRate": 0.02,   // 最小期望利润率
      
      // 风控
      "MaxNotionalPerMarket": 50,
      "MaxNotionalPerOrder": 20,
      "DefaultOrderQuantity": 10,
      "MinOrderQuantity": 1,
      "MaxSlippage": 0.01,
      
      // 冷却
      "EntryCooldownSeconds": 30
    }
  }
}
```

---

## 6. 决策日志与审计

### 6.1 决策日志结构

```csharp
public sealed class StrategyDecisionLog : Entity, IAggregateRoot
{
    public string StrategyId { get; }          // 策略 ID
    public string Action { get; }              // 动作: Entry/Exit/Hedge/Skip/Timeout
    public string Reason { get; }              // 决策原因
    public string? MarketId { get; }           // 相关市场
    public string? ContextJson { get; }        // 上下文 (JSON)
    public DateTimeOffset TimestampUtc { get; }// 时间戳
    public string? ConfigVersion { get; }      // 配置版本
    public string? CorrelationId { get; }      // 关联 ID
    public string? ExecutionMode { get; }      // 执行模式 (Paper/Live)
}
```

### 6.2 日志触发时机

| Action | 触发时机 | 记录内容 |
|--------|----------|----------|
| `Entry` | 策略决定入场 | 市场ID, PairCost, 数量, 价格 |
| `Exit` | 策略决定出场 | 市场ID, PairValue, 原因 |
| `Hedge` | 对冲腿成交 | 市场ID, 对冲详情 |
| `Skip` | 跳过入场机会 | 市场ID, 跳过原因 (冷却/限额等) |
| `Timeout` | 超时处理 | 市场ID, 超时类型 |

### 6.3 查询决策日志

```sql
-- 查询最近 10 条决策
SELECT * FROM "StrategyDecisionLogs" 
ORDER BY "TimestampUtc" DESC 
LIMIT 10;

-- 查询特定策略的入场决策
SELECT * FROM "StrategyDecisionLogs"
WHERE "StrategyId" = 'dual_leg_arbitrage'
  AND "Action" = 'Entry'
ORDER BY "TimestampUtc" DESC;
```

---

## 7. 配置项说明

### 7.1 StrategyEngine 配置

```json
{
  "StrategyEngine": {
    // 基础开关
    "Enabled": true,                      // 是否启用策略引擎
    
    // 并发控制
    "MaxConcurrentStrategies": 2,         // 最大并发策略数
    
    // 执行周期
    "EvaluationIntervalSeconds": 2,       // 策略评估间隔
    "OrderStatusPollIntervalSeconds": 3,  // 订单状态轮询间隔
    
    // 重启策略
    "MaxRestartAttempts": 3,              // 最大重启次数
    "RestartDelaySeconds": 5,             // 重启延迟
    
    // 限流
    "MaxOrdersPerCycle": 4,               // 每周期最大下单数
    
    // 快照
    "SnapshotTimeoutSeconds": 5,          // 快照等待超时
    
    // 审计
    "DecisionLogEnabled": true,           // 是否记录决策日志
    "ConfigVersion": "v1",                // 配置版本
    
    // 数据保留
    "Retention": {
      "DecisionLogRetentionDays": 30,     // 决策日志保留天数
      "CommandAuditRetentionDays": 30,    // 命令审计保留天数
      "CleanupIntervalHours": 6           // 清理间隔
    },
    
    // 运行时控制 (热更新)
    "DesiredStates": {
      "dual_leg_arbitrage": "Running",    // Running/Stopped
      "endgame_sweep": "Stopped"
    }
  }
}
```

### 7.2 DesiredStates 热更新

修改配置文件中的 `DesiredStates` 和 `ConfigVersion` 可以在不重启进程的情况下启停策略：

```json
{
  "StrategyEngine": {
    "DesiredStates": {
      "dual_leg_arbitrage": "Stopped",   // 改为 Stopped 停止策略
      "endgame_sweep": "Running"          // 改为 Running 启动策略
    },
    "ConfigVersion": "v20260106120000"    // 修改版本号触发热更新
  }
}
```

---

## 8. 扩展：添加新策略

### 8.1 步骤概述

1. 创建策略选项类
2. 实现 IStrategy 接口
3. 注册策略

### 8.2 示例：创建 MyNewStrategy

#### Step 1: 创建选项类

```csharp
// Autotrade.Strategy.Application/Strategies/MyNew/MyNewStrategyOptions.cs

public sealed class MyNewStrategyOptions
{
    public const string SectionName = "Strategies:MyNewStrategy";
    
    public bool Enabled { get; set; } = false;
    public string ConfigVersion { get; set; } = "v1";
    
    // 策略特定配置
    public decimal Threshold { get; set; } = 0.95m;
    public int MaxMarkets { get; set; } = 10;
}
```

#### Step 2: 实现策略

```csharp
// Autotrade.Strategy.Application/Strategies/MyNew/MyNewStrategy.cs

public sealed class MyNewStrategy : IStrategy
{
    private readonly StrategyContext _context;
    private readonly MyNewStrategyOptions _options;
    private readonly ILogger<MyNewStrategy> _logger;
    
    public string StrategyId => "my_new_strategy";
    public string Name => "MyNewStrategy";
    
    public MyNewStrategy(
        StrategyContext context,
        IOptions<MyNewStrategyOptions> options,
        ILogger<MyNewStrategy> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }
    
    public Task<IReadOnlyList<TradeIntent>> OnMarketSnapshotAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var intents = new List<TradeIntent>();
        
        // 实现决策逻辑
        // ...
        
        return Task.FromResult<IReadOnlyList<TradeIntent>>(intents);
    }
    
    public Task OnOrderUpdateAsync(StrategyOrderUpdate update, CancellationToken ct)
    {
        // 处理订单更新
        return Task.CompletedTask;
    }
    
    public IReadOnlyList<string> GetActiveMarkets()
    {
        // 返回关注的市场列表
        return Array.Empty<string>();
    }
    
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

#### Step 3: 注册策略

```csharp
// Autotrade.Strategy.Infra.CrossCutting.IoC/StrategyServiceCollectionExtensions.cs

public static IServiceCollection AddStrategyServices(this IServiceCollection services, IConfiguration configuration)
{
    // ... 现有代码 ...
    
    // 绑定配置
    services.Configure<MyNewStrategyOptions>(
        configuration.GetSection(MyNewStrategyOptions.SectionName));
    
    // 注册策略
    services.AddSingleton(new StrategyRegistration(
        "my_new_strategy",                         // 策略 ID
        "MyNewStrategy",                           // 显示名称
        typeof(MyNewStrategy),                     // 策略类型
        MyNewStrategyOptions.SectionName,          // 配置节点
        (sp, context) => ActivatorUtilities.CreateInstance<MyNewStrategy>(sp, context),
        sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<MyNewStrategyOptions>>().CurrentValue;
            return new StrategyOptionsSnapshot(
                "my_new_strategy",
                options.Enabled,
                options.ConfigVersion);
        }));
    
    return services;
}
```

#### Step 4: 添加配置

```json
{
  "Strategies": {
    "MyNewStrategy": {
      "Enabled": true,
      "ConfigVersion": "v1",
      "Threshold": 0.95,
      "MaxMarkets": 10
    }
  }
}
```
