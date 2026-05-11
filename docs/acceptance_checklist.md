# PRD MVP + V1 Acceptance Checklist

This checklist is executable evidence for `docs/prd.txt` sections 10.1 and 10.2. It intentionally excludes task 11/12 spot feed and repricing-lag work, V2 UI, Liquidity Making, and Volatility Scalping.

Use a local override file for repeatable runs:

```bash
dotnet run --project Autotrade.Cli -- run --config Autotrade.Cli/appsettings.local.json
```

## Preflight

1. Configure `ConnectionStrings:AutotradeDatabase`.
2. Run `dotnet build` and `dotnet test`.
3. Run:

```bash
dotnet run --project Autotrade.Cli -- config validate --config Autotrade.Cli/appsettings.local.json
dotnet run --project Autotrade.Cli -- health --mode readiness --config Autotrade.Cli/appsettings.local.json
dotnet run --project Autotrade.Cli -- status --config Autotrade.Cli/appsettings.local.json
```

Expected evidence:

- Build and test outputs show 0 errors.
- `config validate`, `health readiness`, and `status` show compliance state.
- Live mode is blocked unless `Compliance:GeoKycAllowed=true`.

## AC-001 Paper Stable Run And Recovery

Configuration:

- `Execution:Mode=Paper`
- `MarketData:CatalogSync:Enabled=true`
- `Compliance:GeoKycAllowed=false` is allowed in Paper, but should emit compliance warning/risk events.

Steps:

1. Start the process in Paper mode.
2. Run for the agreed window.
3. Interrupt and restart the process.
4. Export evidence:

```bash
dotnet run --project Autotrade.Cli -- export decisions --limit 200 --config Autotrade.Cli/appsettings.local.json > artifacts/decisions.csv
dotnet run --project Autotrade.Cli -- export orders --limit 200 --config Autotrade.Cli/appsettings.local.json > artifacts/orders.csv
dotnet run --project Autotrade.Cli -- export order-events --limit 200 --config Autotrade.Cli/appsettings.local.json > artifacts/order_events.csv
dotnet run --project Autotrade.Cli -- export trades --limit 200 --config Autotrade.Cli/appsettings.local.json > artifacts/trades.csv
dotnet run --project Autotrade.Cli -- export pnl --config Autotrade.Cli/appsettings.local.json > artifacts/pnl.csv
```

Expected evidence:

- Process restarts without losing persisted orders/events.
- Paper orders and order events are persisted.
- Paper compliance warnings do not block order placement.

## AC-002 Dual-Leg Decision To Audit Loop

Configuration:

- `Strategies:DualLegArbitrage:Enabled=true`
- `StrategyEngine:MaxConcurrentStrategies>=1`

Steps:

```bash
dotnet run --project Autotrade.Cli -- strategy start --id dual_leg_arbitrage --config Autotrade.Cli/appsettings.local.json
dotnet run --project Autotrade.Cli -- status --config Autotrade.Cli/appsettings.local.json
```

Export:

```bash
dotnet run --project Autotrade.Cli -- export decisions --strategy-id dual_leg_arbitrage --limit 200 --config Autotrade.Cli/appsettings.local.json > artifacts/dual_leg_decisions.csv
dotnet run --project Autotrade.Cli -- export orders --strategy-id dual_leg_arbitrage --limit 200 --config Autotrade.Cli/appsettings.local.json > artifacts/dual_leg_orders.csv
dotnet run --project Autotrade.Cli -- export order-events --strategy-id dual_leg_arbitrage --limit 200 --config Autotrade.Cli/appsettings.local.json > artifacts/dual_leg_order_events.csv
```

Expected evidence:

- Decisions include reason/context fields.
- Orders include `ExchangeOrderId` when accepted by an exchange path.
- Order events cover submitted, accepted/rejected, filled/cancelled/expired paths when those paths are exercised.

## AC-003 Risk Blocking And Kill Switch

Steps:

```bash
dotnet run --project Autotrade.Cli -- killswitch activate --level hard --reason "manual stop" --config Autotrade.Cli/appsettings.local.json
dotnet run --project Autotrade.Cli -- status --config Autotrade.Cli/appsettings.local.json
dotnet run --project Autotrade.Cli -- export risk-events --limit 100 --config Autotrade.Cli/appsettings.local.json > artifacts/risk_events.csv
dotnet run --project Autotrade.Cli -- killswitch reset --config Autotrade.Cli/appsettings.local.json
```

Expected evidence:

- HardStop blocks new orders and attempts to cancel open orders.
- Risk events include reason code and action.
- `health readiness` reports unhealthy while a blocking kill switch is active.

## AC-004 CLI Control, Config, And Export

Steps:

```bash
dotnet run --project Autotrade.Cli -- config get --path Execution:Mode --show-source --config Autotrade.Cli/appsettings.local.json
dotnet run --project Autotrade.Cli -- config set --path Strategies:DualLegArbitrage:PairCostThreshold --value 0.97 --config Autotrade.Cli/appsettings.local.json
dotnet run --project Autotrade.Cli -- config validate --json --config Autotrade.Cli/appsettings.local.json > artifacts/config_validate.json
dotnet run --project Autotrade.Cli -- status --json --config Autotrade.Cli/appsettings.local.json > artifacts/status.json
```

Expected evidence:

- Config get/set works against the selected override file.
- `status.json` includes execution mode, compliance, open order counts, positions, recent risk events, and strategy states.

## AC-005 Live Execution Closure

Automated evidence must use mock/contract tests only. Do not put real private keys or real funds in automated tests.

Configuration for manual Live acceptance:

- `Execution:Mode=Live`
- `Compliance:GeoKycAllowed=true`
- Polymarket CLOB credentials supplied by user-secrets or environment variables.

Manual steps:

1. Start the process in Live mode with a dedicated low-risk account.
2. Place a small order through a strategy or a controlled command path.
3. Confirm `Orders.ExchangeOrderId` is persisted.
4. Restart the process.
5. Cancel or query the open order after restart.
6. Export orders, order events, trades, and pnl.

Expected evidence:

- Successful and rejected Live order attempts are persisted.
- Restart recovery restores open orders into idempotency and state tracking.
- REST reconciliation fills trades and positions without duplicate trade insertion.
- CLOB user WebSocket events can update order/trade state, while REST reconciliation remains authoritative.

## AC-101 Two Strategies In Parallel

Configuration:

- `Strategies:DualLegArbitrage:Enabled=true`
- `Strategies:EndgameSweep:Enabled=true`
- `StrategyEngine:MaxConcurrentStrategies=2`

Steps:

```bash
dotnet run --project Autotrade.Cli -- strategy start --id dual_leg_arbitrage --config Autotrade.Cli/appsettings.local.json
dotnet run --project Autotrade.Cli -- strategy start --id endgame_sweep --config Autotrade.Cli/appsettings.local.json
dotnet run --project Autotrade.Cli -- status --json --config Autotrade.Cli/appsettings.local.json > artifacts/status_two_strategies.json
```

Expected evidence:

- Both strategies reach Running/Paused/Stopped according to desired state and risk state.
- Shared risk limits block orders across strategies when limits are exceeded.

## AC-102 First-Leg Exposure Plan

Configuration:

- Set `Risk:MaxFirstLegExposureSeconds` low enough for the test window.
- Choose `Risk:UnhedgedExitAction` from `LogOnly`, `CancelOrders`, `CancelAndExit`, or `ForceHedge`.

Steps:

1. Create a scenario where the first leg can fill but the hedge leg cannot complete within the configured timeout.
2. Export risk events and order events.

Expected evidence:

- Unhedged exposure is recorded.
- Timeout produces risk events.
- Selected exit action is visible in cancellation/order/risk audit.

## AC-103 Observability And Readiness

Steps:

```bash
dotnet run --project Autotrade.Cli -- health --mode readiness --json --config Autotrade.Cli/appsettings.local.json > artifacts/readiness.json
dotnet run --project Autotrade.Cli -- health --mode liveness --json --config Autotrade.Cli/appsettings.local.json > artifacts/liveness.json
```

Optional:

- Enable `Observability:EnablePrometheusExporter=true`.
- Enable `Observability:EnableOtlpExporter=true`.

Expected evidence:

- Readiness covers DB, API, WebSocket, background service heartbeat, kill switch, and compliance.
- Diagnostics logs API latency, WebSocket disconnects, strategy lag, and error-rate warnings.

