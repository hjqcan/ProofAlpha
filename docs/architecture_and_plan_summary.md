基于 DDD 的系统架构设计

以下架构采用 领域驱动设计（DDD） 方法，将系统拆分为若干限界上下文 (Bounded Context)，明确定义领域模型与服务，同时确保灵活扩展不同策略。系统主要由 交易领域、市场数据领域、策略执行领域 和 基础设施层 组成。

1. 交易领域 (Trading Domain)
   角色/组件 说明
   实体 (Entity) – Account 表示 Polymarket 账户，包含余额、仓位、订单集合。
   实体 – Position 代表在某市场的仓位，包含持仓数量、方向（Up/Down）、平均成本、订单记录。
   值对象 (Value Object) – Price/Spread 描述价格和点差等不可变属性。
   聚合根 (Aggregate Root) – TradingAccount 聚合 Account、Position 和相关订单，保证下单、撤单的一致性和原子性。
   领域服务 – OrderService 封装下单、撤单逻辑，负责生成订单、持仓更新和仓位校验。利用领域事件通知其他组件。
   领域事件 – OrderPlaced、OrderFilled、PositionClosed 用于在聚合内部及跨上下文传递状态变更，例如策略层根据下单结果调整行为。
   仓储接口 – ITradeRepository 定义保存和查询账户、仓位、订单的方法，由基础设施层实现。
2. 市场数据领域 (Market Data Domain)
   角色/组件 说明
   实体 – Market 描述 Polymarket 的一个预测市场，包括市场 ID、名称、类型（如 15 分钟涨跌）、到期时间、状态等。
   实体 – OrderBook 维护某市场的实时订单簿深度和最优买卖价。
   领域服务 – MarketDataService 通过 WebSocket/HTTP 接收订单簿、价格、行情更新
   lookonchain.com
   。提供订阅接口供策略层调用。
   仓储接口 – IMarketRepository 缓存市场列表、历史价格，允许查询和过滤符合策略条件的市场。
3. 策略执行领域 (Strategy Domain)

这个上下文封装策略逻辑，可针对不同套利方式实现多个策略，实现通过策略模式或工厂模式选择。

角色/组件 说明
接口 – ITradingStrategy 所有策略必须实现的接口，定义 SelectMarkets()、EvaluateEntry()、EvaluateExit() 等方法。
策略实体 – DualLegArbitrageStrategy 对应推文所述两腿套利。逻辑：

选择近期开盘的 15 分钟涨跌市场；

监视订单簿，当一侧价格偏低到阈值时下单买入该方向；

等待另一侧价格出现错配，确保两笔合约成本之和 ≤ 1 美元，再下单买入对冲
lookonchain.com
；

如果出现极端波动且难以对冲，可择机退出，限制单市场投入。 |
| 策略实体 – LiquidityMakingStrategy | 根据前期研究中流动性做市策略，双边挂单并赚取价差和流动性奖励（如果未来需要拓展）。 |
| 策略实体 – EndgameSweepStrategy | 选择结算在即且赔率高的市场进行尾盘扫货（基于前述研报）。 |
| 策略管理器 – StrategyManager | 负责策略调度、参数配置、资金分配，多策略运行时协调资金和风险。 |
| 风险管理服务 – RiskManagementService | 监控每个策略和市场的资金占用、最大亏损、持仓比例，提供止损、暂停策略等功能
bonzai.pro
。 |

4. 应用层 (Application Layer)

应用层协调领域模型和基础设施。主要组件包括：

应用服务 – TradingAppService：暴露给用户或 UI 的主要接口，负责接收用户指令（开始/停止策略、设置参数）、调用策略管理器、处理领域事件并更新仓储。

DTO 和映射器：用于将领域模型转换为数据传输对象 (DTO)，供接口或前端展示。

命令与查询 (CQRS)：对于复杂应用，可将读取与写入分离，读模型可直接从数据库或缓存中查询，以提供快速的界面响应。

5. 基础设施层 (Infrastructure)

负责与外部世界交互，实现仓储接口和 API 调用：

Polymarket API 客户端：使用 C# 的 HttpClient 和 WebSocket 客户端与 Polymarket CLOB API 对接，支持下单、撤单、查询市场信息、订阅订单簿。

MarketRepository 实现：缓存市场列表和行情数据；利用 WebSocket 监听实时更新，落地存储供策略层使用。

TradeRepository 实现：保存账户和订单数据，可使用轻量级数据库（如 SQLite）或内存缓存，必要时持久化到磁盘。

日志系统：记录所有操作、错误及交易明细，支持本地文件和远程日志聚合。

配置与依赖注入：使用 .NET Core 的依赖注入容器管理服务注册和配置（API 密钥、策略参数等）。

6. 用户界面与交互

命令行接口 (CLI)：提供启动/停止策略、查看仓位、设置参数的命令，适合开发者使用。

Web/桌面 GUI：使用 Blazor 或 WPF 构建交易监控面板，显示市场列表、持仓、历史交易和实时盈亏。提供模拟模式和真实交易开关，方便调试和风险控制
bonzai.pro
。

API 接口：未来可暴露 REST 或 gRPC 接口，供其他服务或自动运维系统调用。

7. 关键设计考量

性能与延迟：两腿套利机会稍纵即逝，系统应使用异步 I/O、WebSocket 订阅和内存缓存减少延迟；必要时部署于靠近 Polymarket 服务器的 VPS 以降低网络延时。

原子性与一致性：下单和持仓变更必须在事务内完成，聚合根负责保证一致性；如第一条腿下单后长时间未能对冲，应有风险保护措施。

策略隔离与扩展性：DDD 分隔不同策略上下文，便于增加新策略或替换现有策略而不影响其他部分。使用策略模式/工厂模式让新增策略容易集成。

合规与地理限制：接口层须检查用户的 KYC/地理限制，确保符合 Polymarket 法规要求
lookonchain.com
。

详细开发计划
阶段 1：需求分析与领域建模（第 1-2 周）

收集需求：结合推文及文章，总结两腿套利的业务规则、风险管理需求、市场列表筛选条件等。

绘制领域模型：利用 UML 或 C# 类图描述实体、值对象、聚合根、领域服务等；与团队讨论边界划分。

定义策略算法：确定策略参数（阈值、最大资金占用、止损规则），列出需要的 API 调用和数据订阅。

阶段 2：基础设施搭建与外部对接（第 3-4 周）

Polymarket API 封装：编写用于下单、撤单、查询市场的 HTTP 客户端，基于 .NET HttpClient；封装 WebSocket 客户端订阅订单簿和成交数据。

仓储实现：实现 IMarketRepository 和 ITradeRepository，使用内存数据结构与 SQLite 数据库作为持久化层，支持并发访问。

日志与配置管理：集成 NLog 或 Serilog 记录日志，使用 appsettings.json 管理 API 密钥和策略参数。

阶段 3：领域层和策略开发（第 5-7 周）

实现交易领域模型：编写 Account、Position、TradingAccount 等实体与聚合根，实现业务校验逻辑。

开发领域服务：实现 OrderService，处理下单、撤单、更新仓位等操作；实现 RiskManagementService 控制仓位风险。

实现市场数据服务：根据 WebSocket 事件更新订单簿与市场状态，提供订阅接口。

编写两腿套利策略：实现 DualLegArbitrageStrategy，包括选择市场、建仓逻辑、完成对冲和退出逻辑；与风险管理模块协作。

阶段 4：应用层与用户界面（第 8-9 周）

开发 TradingAppService：实现启动/停止策略、参数修改、查看状态的 API；集成领域服务和策略管理器。

构建 CLI 工具：使用 .NET System.CommandLine 提供命令行界面，支持策略运行、查看市场、调节参数。

设计 GUI (可选)：若需要可开发 Blazor 或 WPF 前端，展示实时仓位和订单簿；初期也可使用控制台日志。

阶段 5：测试、风险评估与部署（第 10-12 周）

单元测试：针对领域模型、服务、策略等编写测试，确保规则正确实现。

集成测试：模拟 API 调用和 WebSocket 消息，验证策略能正确识别机会并下单。

回测与模拟：使用 Polymarket 历史数据回测策略表现，评估利润空间与风险；在沙盒账户中小资金实测。

部署和监控：将服务部署到靠近 Polymarket 的 VPS 上，配置监控和日志分析；设置告警规则（网络断线、异常亏损等）。

持续迭代

在初版稳定运行后，可探索更多策略（如尾盘扫货或流动性做市），并在策略管理器中按权重分配资金。

随着 Polymarket API 更新，及时调整 API 调用（如新增订单类型 FAK/FOK）。

加强安全与合规：审查智能合约调用、遵守当地法律及平台条款。
