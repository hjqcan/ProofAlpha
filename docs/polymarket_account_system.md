## Polymarket 账户体系（完成品级）开发文档

> 本文档目标：把“Polymarket 账户体系”从 MVP 形态升级为**可长期演进的完成品级设计**。  
> 范围：账户身份（EOA/Proxy/Safe）、签名与鉴权（L1/L2）、API 凭证生命周期、本地 secrets 管理、启动引导、对账与风控资金计算、审计与可观测性、未来扩展（多账户/多 signer/多 key）。

---

### 1. 背景与目标

#### 1.1 现状（代码层已具备的能力）

- **交易账户聚合根**：Trading bounded context 以 `TradingAccount` 作为交易账户聚合根（聚合订单、持仓、风控事件、成交）。
- **账户唯一标识**：系统运行时由 `Polymarket:Clob:Address` 作为 Live 模式账户 key（EOA 地址）。
- **CLOB 鉴权**：
  - **L1**：EIP-712（用于 create/derive API key）
  - **L2**：HMAC（用于下单、撤单、查询余额/挂单等）
- **账户同步与对账**：已具备 Balance/Allowance、外部持仓、外部挂单同步与漂移检测（用于 fail-fast / kill switch 等策略）。

#### 1.1.1 关键代码落点索引（开发时用于快速定位）

> 下面列出“完成品级账户体系”在当前工程中的实际落点（以便你在实现/排障时 1 分钟内找到入口）。

- **Trading 账户聚合与持久化**
  - 聚合根：`context/Trading/Autotrade.Trading.Domain/Entities/TradingAccount.cs`
  - DbContext 映射：`context/Trading/Autotrade.Trading.Infra.Data/Context/TradingContext.cs`
  - 账户幂等初始化：`context/Trading/Autotrade.Trading.Application.Contract/Accounts/ITradingAccountProvisioner.cs`
  - EF 实现：`context/Trading/Autotrade.Trading.Infra.Data/Repositories/EfTradingAccountProvisioner.cs`
- **启动引导与运行上下文**
  - 账户 key 解析：`context/Trading/Autotrade.Trading.Application/Execution/TradingAccountKeyResolver.cs`
  - 运行时上下文（单进程单账户）：`context/Trading/Autotrade.Trading.Application/Execution/TradingAccountContext.cs`
  - Bootstrap（供 HostedService 与 CLI 复用）：`context/Trading/Autotrade.Trading.Application/Execution/TradingAccountBootstrapper.cs`
  - Worker（进程启动时自动 bootstrap）：`context/Trading/Autotrade.Trading.Infra.BackgroundJobs/Workers/TradingAccountBootstrapWorker.cs`
- **外部快照与漂移**
  - 外部快照 store：`context/Trading/Autotrade.Trading.Application/Execution/ExternalAccountSnapshotStore.cs`
  - 同步与对账：`context/Trading/Autotrade.Trading.Application/Execution/AccountSyncService.cs`
  - effective capital：`context/Trading/Autotrade.Trading.Application/Risk/EffectiveRiskCapitalProvider.cs`
- **CLOB 客户端与鉴权**
  - options：`Shared/Autotrade.Polymarket/Options/PolymarketClobOptions.cs`
  - auth handler：`Shared/Autotrade.Polymarket/Http/PolymarketAuthHandler.cs`
  - header factory：`Shared/Autotrade.Polymarket/Http/PolymarketAuthHeaderFactory.cs`
  - client：`Shared/Autotrade.Polymarket/PolymarketClobClient.cs`
- **CLI 操作**
  - `account status/sync`：`Autotrade.Cli/Commands/AccountCommands.cs`
  - 导出 orders/trades：`Autotrade.Cli/Commands/ExportCommands.cs`

#### 1.2 完成品级目标（本阶段要达成的“业界标准”）

1) **身份清晰**：明确区分资金归属地址（funder/proxy）与签名者（signer），并在 EOA 场景下固化约束（两者相等）。  
2) **凭证可管理**：API Key / Secret / Passphrase 具备生命周期（创建、启用、轮换、禁用、吊销），并可审计。  
3) **敏感信息不落明文**：PrivateKey / ApiSecret / ApiPassphrase 不进入日志、不进数据库明文；通过本地 secrets 文件统一管理（当前单机运行）。  
4) **启动可自举**：空数据库、空 API creds 的情况下，能在明确的规则下完成初始化（或明确 fail-fast）。  
5) **可观测与可恢复**：账户状态（地址、creds 状态、最后同步时间、漂移摘要、有效资金）可查询与导出。  
6) **可扩展**：未来要支持 Proxy Wallet / Gnosis Safe / 多 signer / 多 API key / 单进程多账户时，不需要推翻架构，只需要放宽约束与扩展实现。

---

### 2. 术语与权威语义

#### 2.1 账户相关术语

- **EOA**：Externally Owned Account（外部拥有账户）。地址由私钥唯一确定。
- **FunderAddress（资金归属地址）**：Polymarket 账户体系里“资金/仓位归属”的地址。  
  - EOA：`FunderAddress == SignerAddress`
  - Proxy/Safe：`FunderAddress != SignerAddress`
- **Signer（签名者）**：能够产出签名（L1 EIP-712 / 其他链上签名）的实体（通常是 EOA 私钥、硬件钱包、远程签名器、MPC 等）。

#### 2.2 CLOB 鉴权

- **L1（EIP-712）**：证明你控制某个地址的私钥，用于创建/派生 L2 API 凭证。
- **L2（HMAC）**：使用 `ApiKey + ApiSecret + ApiPassphrase` 对请求进行 HMAC 签名。
- **POLY_ADDRESS Header**：请求头中 `POLY_ADDRESS` 的语义必须与官方客户端对齐：
  - EOA：填写 EOA 地址（也是资金归属地址）
  - Proxy/Safe：填写 funder/proxy 地址（资金归属地址），签名者可能不同

> 注：本项目当前阶段选择 **EOA**，但必须在模型中显式保留 `SignatureType` 与“funder vs signer”的扩展点（见 9. 未来演进）。

#### 2.3 认证细节（完成品级：必须可复现、可对齐官方实现）

##### 2.3.1 Header 常量（当前代码已实现）

- `POLY_ADDRESS`
- `POLY_SIGNATURE`
- `POLY_TIMESTAMP`
- `POLY_NONCE`（仅 L1）
- `POLY_API_KEY`（仅 L2）
- `POLY_PASSPHRASE`（仅 L2）
- `POLY_IDEMPOTENCY_KEY`（下单等请求的幂等控制）

##### 2.3.2 L2 HMAC 签名 canonical message

**message 拼接规则**（与官方 clob-client 对齐）：

- 无 body：`message = timestamp + method + requestPath`
- 有 body：`message = timestamp + method + requestPath + serializedBody`

**算法**：

- `key = base64decode(apiSecret)`
- `sig = HMACSHA256(key, utf8(message))`
- 输出 base64，再转 URL-safe：`+ -> -`、`/ -> _`（保留 `=`）

##### 2.3.3 L1 EIP-712 typed data（概念层要求）

L1 签名的结构化消息必须与官方一致（否则 derive/create api key 会失败）。  
本项目使用 Nethereum 的 `Eip712TypedDataSigner.SignTypedDataV4` 实现，并固定：

- Domain：`name=ClobAuthDomain`，`version=1`，`chainId=<ChainId>`
- Message：`address=<POLY_ADDRESS>`，`timestamp=<秒级>`，`nonce=<int>`，`message="This message attests that I control the given wallet"`

---

### 3. 需求边界（当前确定的产品决策）

#### 3.1 当前落地约束（已由用户确认）

- **账户类型**：仅支持 **EOA**
- **地址配置**：Live 模式必须显式配置 `Polymarket:Clob:Address`（让用户一眼知道是哪个钱包）
- **部署模型**：暂不需要单进程多账户（默认一进程一账户，多账户通过多进程/多实例实现）
- **API key**：暂不需要一个账户多个 API key（但模型可保留 1:N，业务约束只允许 1 个 Active）
- **Secrets 存储**：暂时只在个人电脑运行，使用本地文件

---

### 4. 领域模型设计（Trading bounded context）

> 目标：消除“TradingAccount + Account”的命名歧义，避免把“账户”再嵌套一个叫 Account 的对象。

#### 4.1 聚合根：TradingAccount

**职责**：作为 Trading 上下文的“交易账户根”，聚合并一致性管理：

- 账户身份（地址、签名类型、链 id）
- 风控资金上限（RiskLimit）
- 订单、成交、持仓、风控事件（已在现有代码中存在）

**建议字段（完成品基线）**

- `Id : Guid`
- `WalletAddress : string`（EOA：即 funder/signer；统一 lower-case）
- `ChainId : int`（默认 137）
- `SignatureType : enum`（EOA=0，预留 Proxy/Safe）
- `RiskTotalCapital : decimal`
- `RiskAvailableCapital : decimal`
- `RiskUpdatedAtUtc : DateTimeOffset`
- `CreatedAtUtc/UpdatedAtUtc`
- `RowVersion : byte[]`（乐观并发）

> 为什么要把 RiskCapital 放进 TradingAccount？  
> 因为风控上限属于“账户的运行约束”，并且会影响所有策略/订单校验。实际余额/allowance 属于外部快照（见 6.），两者应明确分层。

#### 4.2 未来扩展实体（本阶段先设计，后续逐步落地）

> 当前 EOA 可以不立刻实现这些实体，但文档要把边界与未来路径定清楚。

- `TradingAccountSigner`（1:N）
  - EOA 阶段的约束：必须且只能 1 个 Active signer，并且地址等于 WalletAddress
  - Proxy/Safe 阶段：允许多个 signer，支持轮换、吊销、阈值等
- `TradingAccountApiCredential`（1:N）
  - 当前约束：只允许 1 个 Active credential
  - 未来：允许多 key（按 bot 实例/环境隔离）

---

### 5. 配置与本地 secrets 规范（当前单机最佳实践）

#### 5.1 配置文件分层

建议分为两层文件：

1) **非敏感配置**：`Autotrade.Cli/appsettings.local.json`（已在项目约定中存在/被加载）
2) **敏感配置（secrets）**：新增 `Autotrade.Cli/appsettings.secrets.local.json`（需要 gitignore；仅本机）

> 约束：日志不得输出 PrivateKey、ApiSecret、ApiPassphrase 明文。

#### 5.2 secrets 文件建议结构（示例）

```json
{
  "Polymarket": {
    "Clob": {
      "PrivateKey": "0x...",
      "ApiKey": "xxx",
      "ApiSecret": "base64...",
      "ApiPassphrase": "xxx"
    }
  }
}
```

#### 5.3 文件安全建议（单机也要做）

- secrets 文件必须被 gitignore
- 文件权限建议设置为仅当前用户可读
- 如未来要更安全：可把 secrets 文件做加密（例如 `*.enc` + 本机 master key），并抽象出 `ISecretProvider`/`ISecretStore`

---

### 6. 启动引导（Bootstrap）与运行期同步

#### 6.1 启动引导目标

启动时保证以下条件成立：

- 能解析出 Live 账户 key（EOA 地址）并校验格式
- `TradingAccount` 在数据库存在（幂等创建）
- （Live）完成一次外部快照同步（余额/allowance/持仓/挂单）并根据配置策略 fail-fast
- 风控资金计算能得到有效值：`effective = min(RiskLimit, ExternalBalance, ExternalAllowance)`

#### 6.2 运行期同步与漂移

- **同步内容**：
  - Balance/Allowance（CLOB）
  - Positions（Data API）
  - Open Orders（CLOB）
- **漂移检测**：
  - 外部存在但内部未知的挂单（UnknownExternal）
  - 内部认为挂单但外部不存在（MissingInternal）
  - 外部持仓与内部账本不一致（QuantityMismatch/AvgCostMismatch）
- **策略**：
  - 启动阶段：按配置可 fail-fast
  - 运行阶段：按配置可触发 kill switch（或仅告警）

---

### 7. 可观测与审计

#### 7.1 建议的可观测指标（最小集合）

- `account_sync_last_success_timestamp`
- `account_sync_drift_count_positions`
- `account_sync_drift_count_open_orders_unknown`
- `account_sync_drift_count_open_orders_missing`
- `effective_capital_total/available`
- `polymarket_api_latency_ms`（已有 Polymarket client metrics，可复用）

#### 7.2 审计建议

- CLI 触发的账户相关操作（status/sync/未来的 rotate）必须落入 `CommandAuditLogs`
- 若未来实现 API key 轮换：轮换事件要写入专门的审计表或复用 RiskEventLog/CommandAuditLog（带 context_json）

---

### 8. 测试计划（完成品标准）

#### 8.1 单元测试

- 地址规范化与校验：必须为 0x + 40 hex，最终存储 lower-case
- HMAC 签名 message 规范：timestamp + method + path + body（与官方一致）
- secrets 文件解析：缺字段/格式错误应 fail-fast 且不泄露敏感信息
- effective capital 计算：min(risk, balance, allowance)

#### 8.2 集成测试（可选）

- 使用 mock server / contract tests 验证 CLOB endpoints 与签名头
- 数据库空库启动：能自动创建所需表并运行到 “ready”

---

### 9. 未来演进路线（从 EOA 到 Proxy/Safe）

#### 9.1 为什么要预留 Proxy/Safe？

当你未来不想“换私钥就换地址/搬钱”时，Proxy/Safe 能做到：

- funder（资金归属）地址不变
- signer 可轮换/吊销（人员变动、密钥泄露、容灾）
- 甚至可做多签阈值（更安全）

#### 9.2 需要新增/放宽的点

- `TradingAccount` 增加：
  - `FunderAddress`（资金归属地址）
  - `SignerAddress`（当前 active signer）
  - `SignatureType` 改为真正生效
- `PolymarketClobOptions` / 客户端鉴权：
  - 从全局 Options 转为“按账户上下文”解析（为单进程多账户铺路）
- 账户表与 secrets：
  - signer 私钥不再等于 funder
  - 允许多 signer（轮换）

---

### 10. 实施清单（从文档到代码）

> 本清单用于把本文档落地为代码（按阶段逐步实现）。

阶段 A（当前必做，EOA 完成品基线）：

- [ ] TradingAccount 模型中显式包含 `ChainId`、`SignatureType`（先固定 EOA）
- [ ] 清理/重命名当前 `Account` 值对象（避免歧义与 MVP 注释）
- [ ] 本地 secrets 文件规范与加载（新增 `appsettings.secrets.local.json`）
- [ ] 启动引导：缺失 Address/缺失 secrets 时明确 fail-fast 文案
- [ ] 空库启动：自动建表（无 migrations 模式）

阶段 B（可选增强）：

- [ ] ApiCredential 的轮换命令（CLI）+ 审计
- [ ] 将外部快照摘要落库（跨进程可观测）

阶段 C（未来扩展）：

- [ ] Proxy/Safe 支持：funder != signer；signature_type 生效；多 signer 轮换

