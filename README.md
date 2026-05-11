# Autotrade

Autotrade is a C# / .NET 10 Polymarket-style prediction-market automation
system. It is built around DDD bounded contexts, a CLI host, a local Control
Room API, and a React/Vite Web App for operator evaluation.

The default posture is Paper-first. Live trading is intentionally gated by
configuration, credential presence, compliance checks, readiness, risk state,
and explicit Live arming evidence. This repository is not a profitability
guarantee, investment-advice product, or unattended Live trading release.

## Current Surfaces

- `interfaces/Autotrade.Cli`: command-line host for running the system,
  readiness/status checks, strategy lifecycle control, kill switch operations,
  account sync, exports, OpportunityDiscovery, SelfImprove, and Live arming.
- `interfaces/Autotrade.Api`: ASP.NET Core Control Room API with health,
  readiness, OpenAPI/Swagger, local-access protection, market discovery,
  control-room snapshot, risk drilldown, audit timeline, replay exports,
  run reports, strategy parameter history, and SelfImprove endpoints.
- `interfaces/WebApp`: React 19 + Vite operator control room. Views include
  Markets, Trade, Ops, Activity, and Reports.
- `context/*`: bounded contexts for Trading, MarketData, Strategy,
  OpportunityDiscovery, and SelfImprove.
- `Shared/*`: reusable infrastructure for EF Core, background jobs, event bus,
  Polymarket clients, LLM JSON clients, security helpers, and testing.

## Implemented Capabilities

- Strategy engine with configurable strategies:
  - `dual_leg_arbitrage`
  - `endgame_sweep`
  - `liquidity_pulse`
  - `liquidity_maker`
  - `micro_volatility_scalper`
  - `llm_opportunity` through reviewed/published OpportunityDiscovery output
- Paper and Live execution services.
- Order, trade, position, order-event, risk-event, decision, command-audit,
  run-report, promotion-checklist, and replay evidence persistence.
- Startup recovery, REST reconciliation, account sync, external drift checks,
  open-order cancellation/query support, and user order/trade WebSocket events.
- Risk management, compliance guardrails, kill switch, Live arming/disarming,
  typed confirmations, and audit-visible high-impact commands.
- Polymarket Gamma, Data, CLOB, CLOB market/user WebSocket, and RTDS client
  infrastructure with logging, metrics, rate limiting, idempotency, and
  resilience handlers.
- Market catalog sync, local order-book state, market discovery ranking,
  freshness states, and control-room market detail/order-book APIs.
- Background jobs via Hangfire for market catalog sync, retention,
  OpportunityDiscovery, SelfImprove, and account/risk maintenance.
- OpportunityDiscovery for paper-only market research using public evidence
  sources plus OpenAI-compatible structured JSON analysis.
- SelfImprove for structured strategy episodes, parameter proposal dry-runs,
  generated strategy package gates, and canary-safe promotion workflows.
- Local product hardening evidence scripts for acceptance, deploy smoke,
  audit regression, cross-surface quality gates, and release closure.

## Prerequisites

- .NET SDK compatible with `global.json` (`10.0.101` with latest-feature
  roll-forward).
- Node.js and npm for `interfaces/WebApp`.
- PostgreSQL when running module-enabled CLI/API flows against persistence.
- Optional: initialize submodules in a fresh checkout:

```powershell
git submodule update --init --recursive
```

## Build And Test

From the repository root:

```powershell
dotnet restore
dotnet build
dotnet test
```

Warnings are treated as errors through `Directory.Build.props`.

Web App:

```powershell
npm --prefix .\interfaces\WebApp install
npm --prefix .\interfaces\WebApp run build
```

Optional Postgres smoke tests are enabled only when
`AUTOTRADE_TEST_POSTGRES` is set.

## Configuration

Primary config files:

- `interfaces/Autotrade.Cli/appsettings.json`
- `interfaces/Autotrade.Cli/appsettings.paper.json`
- `interfaces/Autotrade.Cli/appsettings.local.json` (ignored local override)
- `interfaces/Autotrade.Api/appsettings.json`
- `interfaces/Autotrade.Api/appsettings.Development.json`

Configuration can also come from environment variables or user secrets. For
.NET hierarchical config, use double underscores in environment variables, for
example:

```powershell
$env:ConnectionStrings__AutotradeDatabase = "Host=localhost;Port=5432;Database=autotrade;Username=postgres;Password=123456"
$env:Execution__Mode = "Paper"
```

Minimal local Paper override:

```json
{
  "ConnectionStrings": {
    "AutotradeDatabase": "Host=localhost;Port=5432;Database=autotrade;Username=postgres;Password=123456"
  },
  "Execution": {
    "Mode": "Paper"
  },
  "EventBus": {
    "UseInMemory": true
  },
  "Database": {
    "AutoMigrate": true
  },
  "Strategies": {
    "DualLegArbitrage": { "Enabled": false },
    "EndgameSweep": { "Enabled": false },
    "LiquidityPulse": { "Enabled": false },
    "LiquidityMaker": { "Enabled": false },
    "MicroVolatilityScalper": { "Enabled": false },
    "LlmOpportunity": { "Enabled": false }
  }
}
```

Do not commit real private keys, CLOB API secrets, mnemonics, passwords, or
exchange credentials. The browser surface must never receive credential
material.

## CLI Usage

Use the actual CLI project under `interfaces/Autotrade.Cli`:

```powershell
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- --help
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- run --config .\interfaces\Autotrade.Cli\appsettings.local.json
```

Common diagnostics:

```powershell
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- config validate --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- readiness --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- health --mode readiness --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- status --config .\interfaces\Autotrade.Cli\appsettings.local.json
```

Strategy lifecycle:

```powershell
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- strategy list --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- strategy start --id dual_leg_arbitrage --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- strategy pause --id dual_leg_arbitrage --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- strategy resume --id dual_leg_arbitrage --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- strategy stop --id dual_leg_arbitrage --config .\interfaces\Autotrade.Cli\appsettings.local.json
```

Kill switch:

```powershell
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- killswitch activate --level hard --reason "manual stop" --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- killswitch reset --config .\interfaces\Autotrade.Cli\appsettings.local.json
```

Live arming:

```powershell
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- live status --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- live arm --actor <operator> --reason "<reason>" --confirm "ARM LIVE" --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- live disarm --actor <operator> --reason "<reason>" --confirm "DISARM LIVE" --config .\interfaces\Autotrade.Cli\appsettings.local.json
```

Exports:

```powershell
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- export decisions --limit 200 --output decisions.csv --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- export orders --limit 200 --output orders.csv --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- export trades --limit 200 --output trades.csv --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- export pnl --output pnl.csv --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- export order-events --limit 200 --output order-events.csv --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- export run-report --session-id <session-guid> --output run-report.json --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- export promotion-checklist --session-id <session-guid> --output promotion-checklist.csv --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- export replay-package --output replay-package.json --config .\interfaces\Autotrade.Cli\appsettings.local.json
```

OpportunityDiscovery and SelfImprove are disabled by default and should be
enabled deliberately:

```powershell
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- opportunity scan --config .\interfaces\Autotrade.Cli\appsettings.local.json
dotnet run --project .\interfaces\Autotrade.Cli\Autotrade.Cli.csproj -- self-improve list --limit 20 --config .\interfaces\Autotrade.Cli\appsettings.local.json
```

## API And Web App

Start the API on the default local Control Room port:

```powershell
$env:ASPNETCORE_URLS = "http://127.0.0.1:5080"
dotnet run --project .\interfaces\Autotrade.Api\Autotrade.Api.csproj
```

API endpoints include:

- `GET /health/live`
- `GET /health/ready`
- `GET /api/readiness`
- `GET /api/control-room/snapshot`
- `GET /api/control-room/markets`
- `GET /api/control-room/markets/{marketId}`
- `GET /api/control-room/markets/{marketId}/order-book`
- `POST /api/control-room/strategies/{strategyId}/state`
- `POST /api/control-room/risk/kill-switch`
- `GET /api/run-reports/active`
- `GET /api/replay-exports`
- `GET /api/strategy-parameters/{strategyId}`
- `GET /swagger`

For read-only local API smoke without module registration, set:

```powershell
$env:AutotradeApi__EnableModules = "false"
$env:AutotradeApi__ControlRoom__CommandMode = "ReadOnly"
$env:AutotradeApi__ControlRoom__EnableControlCommands = "false"
$env:AutotradeApi__ControlRoom__EnablePublicMarketData = "false"
```

Start the Web App:

```powershell
$env:VITE_API_BASE = "http://127.0.0.1:5080"
npm --prefix .\interfaces\WebApp run dev
```

The Vite dev server defaults to `http://127.0.0.1:5173`.

## Local Product Deploy Smoke

For local API + Web App product evaluation, run the Phase 7 smoke:

```powershell
.\scripts\run-phase7-local-deploy-smoke.ps1
```

The script publishes the API package, builds the Web App, starts both on
loopback, checks health/readiness/Web root, selects fallback ports if 5080 or
5173 are occupied, and writes:

- `artifacts/deploy/phase-7/local-smoke.json`
- `artifacts/deploy/phase-7/local-smoke.md`

Detailed runbook: `docs/operations/local-product-deploy.md`.

## Architecture

| Area | Path | Ownership |
| --- | --- | --- |
| Trading | `context/Trading` | execution, orders, trades, positions, account sync, risk, compliance, kill switch, reconciliation, audit |
| MarketData | `context/MarketData` | market catalog, order books, WebSocket clients, subscriptions, freshness, market data persistence |
| Strategy | `context/Strategy` | strategy lifecycle, scheduling, decision logging, observations, parameters, promotion reports, replay exports |
| OpportunityDiscovery | `context/OpportunityDiscovery` | public evidence collection, LLM JSON analysis, opportunity review/publish workflow |
| SelfImprove | `context/SelfImprove` | strategy episodes, proposal validation, parameter patches, generated strategy package gates |
| Shared | `Shared` | EF base context, event bus, background jobs, Polymarket clients, LLM client, security helpers, test helpers |
| CLI | `interfaces/Autotrade.Cli` | command orchestration over bounded-context services |
| API | `interfaces/Autotrade.Api` | local control-room contracts and command routing |
| Web App | `interfaces/WebApp` | browser operator surface over API contracts |

Keep bounded-context isolation intact. UI and API code may compose read models
and route commands, but must not bypass Trading, MarketData, Strategy, risk,
compliance, audit, or Live arming services.

## Safety Rules

- Paper mode is the safe default.
- Live order placement requires `Execution:Mode=Live`, compliance allowance,
  CLOB credential presence, readiness, risk eligibility, kill switch inactive
  state, and current Live arming evidence.
- Control-room command modes are `ReadOnly`, `Paper`, and `LiveServices`.
- High-impact commands must be auditable whether accepted or blocked.
- Stale market data must be visible and must not be presented as live
  actionable state.
- Web App/API responses, logs, screenshots, exports, and artifacts must not
  reveal private keys, API secrets, signed payload material, or credentials.

See `docs/product/autotrade-safety-policy.md` for the safety invariants.

## Acceptance And Runbooks

- `docs/acceptance_checklist.md`
- `docs/acceptance_report_template.md`
- `docs/releases/autotrade-product-hardening-release.md`
- `docs/operations/autotrade-operator-runbook.md`
- `docs/operations/autotrade-incident-runbook.md`
- `docs/operations/local-product-deploy.md`
- `docs/operations/replay-export-schema.md`

Useful scripts:

```powershell
.\scripts\run-phase5-audit-regression.ps1
.\scripts\run-phase7-acceptance.ps1
.\scripts\run-phase7-cross-surface-gate.ps1
.\scripts\run-phase7-local-deploy-smoke.ps1
.\scripts\run-phase7-release-closure.ps1
```
