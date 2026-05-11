# 待实现：现货确认 → 盘口重定价延迟套利（Repricing Lag Arbitrage）

来源：https://x.com/Mikocrypto11/status/2007317038461575179
polymarket 账号：https://polymarket.com/@CRYINGLITTLEBABY

> 状态：**待实现（仅需求与设计记录）**  
> 目标：在不破坏现有 DDD 三限界上下文（MarketData / Strategy / Trading+Execution）的前提下，补齐该策略落地所需的信息与后续任务拆解。

## 1. 背景与直觉

观察到某类交易行为：在 15 分钟 BTC 相关市场里，**现货行情已发生明显变化**，但 Polymarket 的盘口（概率/价格）仍存在短时间“滞后”，从而出现可捕捉的错价。

该策略区别于“预测/动量/抢先手”：

- 不在窗口刚开时猜方向
- 等待现货走势显著发生（或达到某个确定阈值）后再介入
- 目标是捕捉 **盘口重定价延迟（lag）**，而非预测未来走势

> 重要：该策略“看起来 100% 胜率”的前提高度依赖 **市场结算口径** 与 **现货参考源**，以及策略的“确认条件”是否真的能将胜率推到极高。下面需要明确这些事实，避免把“经验叙述”误当作“可证明套利”。

## 2. 需要明确的市场类型与结算口径（必须确认）

该策略能否成立，核心取决于：我们判断“结果已确定”的依据是否与 **市场的结算定义** 一致。

请在实现前确认并记录：

1. **市场类型**

   - 是 “Up/Down” 二元市场？
   - 还是 “15-Min Price Range / 价格区间” 一类的阈值市场？
   - 结算条件是否与“开盘价/收盘价差”相关，还是与“在窗口内是否触达某价位/区间”相关？

2. **结算参考（Oracle/Index）**

   - 使用哪个交易所/指数作为结算参考？
   - 参考价的采样频率、时区与取值方式（如 TWAP/某一时刻）？
   - 与我们可获取的现货数据源的一致性要求（必须尽量对齐，避免“现货已跌”但结算源不同导致翻盘）。

3. **窗口定义**
   - 窗口 start/end 的精确定义（按整点/按创建时间/按市场元数据字段）？
   - 是否存在开盘前撮合、延迟、或窗口对齐偏差？

> 建议：在 `MarketData` 中增加“MarketWindowSpec（窗口规范）”读模型/元数据映射，将上述内容结构化保存。

## 3. 策略的判定逻辑（候选形态，待验证）

该策略可抽象为：

1. **识别目标市场集合**（例如 BTC 15m Up/Down）
2. **进入窗口后等待**（例如等待 3–5 分钟）
3. **从现货参考源判定“优势方向”**
   - 需要一个“确认条件”，例如：
     - 跌幅超过阈值 \( \Delta P / P_0 \le -X \) 且剩余时间不足以翻转（经验/统计）
     - 或触达某阈值（若市场本身是 range/threshold）
4. **读取 Polymarket 盘口（订单簿 top-of-book / 深度）**
5. **检测错价（mispricing）**
   - 例如：盘口价格 \(p*{pm}\) 仍在 0.20–0.30，但我们对该方向的“公平概率/胜率”估计 \(p*{fair}\) 明显更高
   - 以差值/比值作为信号：\(edge = p*{fair} - p*{pm}\)
6. **执行下单**
   - 价格通常是“抢滞后”的限价单（避免滑点）
   - 需要考虑成交深度和挂单优先级（是否需要更激进的价格）
7. **退出与对冲（视市场类型）**
   - 若是纯二元结算：可以持有到结算
   - 若是窗口内事件：可能需要提前退出/止损

> 关键：第 3 步“确认条件”必须可被验证/回测，否则只能算叙事。

## 4. 架构落点（不改边界，只加能力）

### 4.1 MarketData bounded context（数据域）

需要新增/扩展：

- **Spot Price Feed Provider**
  - 现货数据源（交易所或聚合器）适配器
  - 输出统一的 `SpotPriceTick(symbol, price, tsUtc)` 或 1s bar
- **MarketWindowSpec / MarketMeta Enrichment**
  - 将市场的窗口 start/end、标的、阈值、结算口径、tokenId 映射成结构化字段
- **统一的数据访问接口**
  - Strategy 层只依赖抽象（例如 `IMarketDataSnapshot` / `ISpotPriceStore`）

### 4.2 Strategy bounded context（策略域）

新增一个策略实现（名称暂定）：

- `RepricingLagArbitrageStrategy`
  - 输入：市场元数据、现货 tick、Polymarket orderbook
  - 状态机：`Idle → WaitingForConfirmation → Signal → SubmitOrder → Monitor → Done/Faulted`
  - 输出：标准化的执行请求（交给 Execution）

### 4.3 Execution/Trading bounded context（执行/交易域）

该策略不应直接触达 Polymarket Client，而是通过既定的 Execution 层统一下单/撤单：

- **Execution**
  - 负责：下单、撤单、查询状态、幂等、重试、断路器、限流（与 Task 4/Task 2 计划一致）
  - 该策略只产出 `OrderIntent/OrderRequest`
- **Trading**
  - 负责：订单/持仓/账户的领域一致性（与风险事件记录）

## 5. 订单执行细节（落地前需要定稿）

1. **下单类型**

   - 首选：限价单（降低滑点），并针对“滞后窗口很短”的特性设置较短的有效期/超时撤单
   - IOC/FAK/FOK：是否可用取决于 Polymarket 支持的 TimeInForce（由 Task 4 落地）

2. **定价方式**

   - 目标是“吃滞后”，往往需要用 **略激进的限价** 保证成交（尤其在深度薄时）
   - 需要基于 orderbook 深度计算：可成交量、预估滑点、是否分拆下单

3. **幂等与重试**

   - 所有会产生副作用的请求必须带 idempotency key
   - 重试仅对“可重试错误”生效（429/5xx/超时），并尊重 Retry-After

4. **取消与退出**
   - 若错价窗口消失（盘口已重定价），未成交单需及时撤单
   - 若市场结算规则允许提前平仓（或存在反向市场），需明确是否做“提前止盈/止损”

## 6. 风控与失败模式（必须在实现前覆盖）

### 6.1 风险点清单

- **结算口径不一致**：现货源 != 结算 oracle，导致策略判断“已确定”但实际不是
- **市场定义误读**：Up/Down 的比较基准、阈值、时间对齐错误
- **盘口深度不足**：看起来 0.30 可买，但真实可成交量极小，导致滑点吞噬优势
- **滞后窗口极短**：撮合/网络延迟导致错价消失
- **极端反转**：若市场不是阈值触达型而是窗口终点对比型，反转始终可能发生
- **API 限流/封禁**：高频轮询/下单触发 closed-only 或风控

### 6.2 建议的风险阈值（由 Risk 模块配置）

结合现有计划（Task 5）可直接复用/扩展：

- `MaxCapitalPerMarket`
- `MaxUnhedgedCapitalPerMarket`
- `MaxFirstLegExposureSeconds`（此策略可解释为“信号出现后，到完成成交/撤单的最大容忍时长”）
- `MaxConsecutiveOrderErrors`
- 手动/自动 Kill Switch（当 oracle/source 不一致或系统异常时快速止损）

## 7. 数据与测试 / 回测计划（建议在实现前准备）

### 7.1 离线/模拟数据需求

为验证“重定价延迟”是否可持续，建议收集并落库（或保存为文件）：

- Polymarket：目标市场的 orderbook（top-of-book + 深度）时间序列、成交/盘口变化
- Spot：对齐结算口径的现货 tick/1s bar
- 市场元数据：窗口 start/end、tokenId、阈值、结算规则

### 7.2 回测方法（高层）

- **事件驱动回放**：按时间戳回放 spot 与 orderbook，模拟策略状态机
- **延迟注入**：在回放中人为注入网络/撮合延迟，评估鲁棒性
- **滑点模型**：使用订单簿深度估算执行价与可成交量

### 7.3 测试分层（对应未来任务）

- 单元测试：窗口状态机、信号计算、mispricing 判定
- 契约测试：Polymarket API 请求形状、idempotency、错误映射（已在 Task 2 覆盖）
- 集成测试：PaperExecution + Mock MarketData 回放，验证端到端

## 8. 需要你补充确认的信息（实现前必须回答）

1. **目标市场到底是哪一种**（Up/Down、Range、还是其他）
2. **结算 oracle/指数是谁**（以及如何对齐我们的 spot 源）
3. **窗口 start/end 的来源**（Gamma 元数据？CLOB 元数据？字段名是什么）
4. **“确认条件”阈值**（跌幅/涨幅、时间、成交量等）
5. **资金/风险偏好**（单笔额度、最大同时市场数、是否允许提前退出）

## 9. 与现有 Taskmaster 计划的关系（实现前置）

该策略建议依赖以下既定任务完成后再做：

- Task 2：Polymarket REST Client（下单/查询/幂等/限流/重试）
- Task 3：WebSocket MarketData（orderbook snapshot+delta、订阅管理）
- Task 4：Execution Engine（Live/Paper、订单状态机）
- Task 5：Risk Management（阈值、kill switch、watchdog）
- Task 6：Strategy Engine（StrategyManager、生命周期、数据路由）

## 10. 非目标（避免范围失控）

- 不在本策略首版中承诺“100% 胜率”
- 不在首版中接入多交易所/多指数（先对齐结算源的一个 spot 源即可）
- 不在首版中做复杂的 ML/预测模型（先验证“滞后套利”是否存在且可复现）
