# Autotrade Current Surface Inventory

Status: Phase 0 baseline
Last reviewed: 2026-05-03

## Purpose

This inventory records the current operator-visible and developer-visible
surfaces before product hardening continues. It separates implemented
engineering capability from accepted product behavior so later phases can close
gaps without moving architecture boundaries.

## Product Position

Autotrade is a Polymarket-style prediction-market automation system for
developers and quantitative operators. The next product milestone is a safe
operator control room for Paper runs and explicitly armed Live operation. The
product must not present itself as a profitability guarantee or investment
advisor.

## Runtime Surfaces

### CLI Host

Path: `Autotrade.Cli`

Current capability:

- Starts the trading host with configurable appsettings.
- Validates configuration and readiness.
- Reports status in human-readable and JSON-oriented command paths.
- Starts, pauses, resumes, and stops strategies.
- Activates and resets the kill switch.
- Exports decisions, orders, order events, trades, PnL, and risk evidence.
- Supports account and system commands.

Known product gaps:

- CLI and Web App readiness language must be aligned.
- High-risk command confirmation and command audit visibility need to be made
  consistent across CLI, API, and Web App.
- Paper-to-Live promotion is not yet an explicit operator workflow.

Owner boundary:

- CLI orchestration belongs in `Autotrade.Cli`.
- Trading, Strategy, and MarketData state must remain owned by their bounded
  contexts.

### Control-Room API

Path: `interfaces/Autotrade.Api`

Current capability:

- Exposes `api/control-room/snapshot`.
- Exposes market discovery, market detail, and order-book endpoints.
- Exposes strategy lifecycle command endpoint.
- Exposes kill-switch command endpoint.
- Exposes process mode, command mode, data mode, risk, strategies, markets,
  orders, positions, decisions, timeline, and series data through typed models.
- Provides health endpoints and OpenAPI setup.

Known product gaps:

- Contract tests need to freeze command and snapshot semantics.
- Command-mode, read-only-mode, and disabled-command behavior need stronger
  tests and UI representation.
- Local access and command safety must be explicit before Live operation is
  surfaced as product behavior.
- The API must never return exchange credentials or private key material.

Owner boundary:

- API can compose read models and route commands.
- API must not become a parallel trading domain or bypass existing risk,
  compliance, order, or strategy services.

### Web App Control Room

Path: `webApp`

Current capability:

- React + TypeScript + Vite app.
- Views for Markets, Trade, Ops, and Activity.
- Market discovery with search, category, sort, paging, and selection.
- Market detail with outcome tokens, order book, microstructure, orders, risk,
  positions, and decisions.
- Strategy Run/Pause/Stop controls.
- Kill-switch hard-stop and reset controls.
- Localized message structure for `zh-CN` and `en-US`.

Known product gaps:

- `zh-CN` copy currently contains mojibake and is unsafe for operator use.
- Control commands need clearer confirmation, disabled reasons, and error
  behavior.
- Order-book freshness and stale-data state are not yet strong enough for an
  operator workstation.
- Strategy explanations are shallow; operators cannot yet inspect full
  decision chains.
- Paper run reporting and Live arming are not yet represented as first-class
  workflows.

Owner boundary:

- Web App is an operator surface over API contracts.
- Web App must not store or receive exchange secrets.
- Web App must not place unmanaged orders outside strategy/risk contracts.

## Bounded Context Inventory

### Trading

Path: `context/Trading`

Current capability:

- Domain entities for orders, order events, trades, positions, risk events, and
  trading accounts.
- Paper and Live execution services.
- Order validation, time-in-force handling, idempotency, and order state
  tracking.
- REST reconciliation and startup recovery workers.
- Account sync and external account snapshot handling.
- Kill switch, risk manager, risk events, risk capital, and unhedged exposure
  worker.
- Compliance guard and Live-mode blocks.
- EF repositories and migrations.

Known product gaps:

- Live arming needs to become an explicit gate beyond raw configuration.
- Incident response and replay workflows are not yet first-class.
- Risk drill-down needs to expose trigger, current value, selected action, and
  affected orders in a coherent operator model.

Architecture risk:

- Any future UI command that directly mutates Trading state without risk and
  audit would pollute the bounded context design.

### MarketData

Path: `context/MarketData`

Current capability:

- Market catalog sync and readers.
- Order-book local store, synchronizer, and subscriptions.
- CLOB and RTDS WebSocket clients.
- Market and order-book domain entities and EF persistence.
- MarketData metrics.

Known product gaps:

- Control-room freshness status must be explicit and testable.
- Market discovery needs reason fields so operators know why a market is ranked
  or unsuitable.
- Stale order-book state must block unsafe operator interpretation.

Architecture risk:

- Strategy and Trading must consume MarketData through contracts rather than
  reaching into persistence internals.

### Strategy

Path: `context/Strategy`

Current capability:

- Strategy manager, supervisor, runner, runtime store, data router, and market
  channels.
- Implemented strategies: Dual Leg Arbitrage, Endgame Sweep, Liquidity Pulse,
  Liquidity Maker, and Micro Volatility Scalper.
- Strategy lifecycle control and desired state persistence.
- Strategy decision logging and command audit.
- Strategy order registry, status polling, and update routing.
- Strategy retention jobs and EF persistence.

Known product gaps:

- Strategy cards need blocked reasons, hypothesis, current decision chain, and
  parameter version context.
- Parameter changes need product-grade diff, validation, audit, and rollback.
- No-op and rejected decisions need to be inspectable, not hidden behind empty
  activity states.

Architecture risk:

- Adding new strategies before explainability and risk evidence would increase
  product complexity without increasing trust.

## Shared Infrastructure

Path: `Shared`

Current capability:

- Polymarket Gamma, Data, and CLOB clients.
- Auth, signing, idempotency, logging, metrics, rate limiting, and resilience
  handlers.
- EF base context and repositories.
- Event bus abstractions and CAP integration.
- Testing helpers.

Known product gaps:

- Client behavior needs to remain observable through API latency, rate-limit,
  retry, and reconciliation evidence.
- Secret redaction needs to be verified anywhere infrastructure errors are
  surfaced to CLI, API, logs, or Web App.

## Configuration and Secrets

Current capability:

- Uses appsettings, local override files, environment variables, and user
  secrets.
- `.env.example` exists.
- Live mode requires Polymarket CLOB credentials.
- Live order placement is blocked unless compliance permits it.

Known product gaps:

- First-run diagnostics should show required configuration presence without
  exposing values.
- Live arming must invalidate when critical config changes.
- Local examples should remain Paper-first.

## Quality and Acceptance Surface

Current capability:

- `dotnet restore`
- `dotnet build`
- `dotnet test`
- Web App `npm run build`
- Existing acceptance checklist for MVP + V1 evidence.
- Optional Postgres smoke behavior through `AUTOTRADE_TEST_POSTGRES`.
- Warnings treated as errors.

Known product gaps:

- Product-hardening phases need evidence artifacts under stable paths.
- Browser smoke evidence is not yet part of the regular product gate.
- Paper run reports and promotion checklists do not exist yet.

## P0 Risks

1. Web App `zh-CN` copy is unreadable and can cause operator error.
2. Control-room command safety is not yet frozen by contract tests.
3. Live operation has compliance and execution blocks, but not a complete
   arming workflow with evidence.
4. Operators cannot yet reconstruct full decision chains from UI.
5. Paper-mode success is not packaged into a promotion report.

## Product Hardening Direction

The next phases must improve trust before expanding alpha:

1. freeze control-room contracts and command safety
2. add onboarding, readiness, and Live arming
3. expose strategy reason chains and parameter history
4. turn Paper runs into evidence-backed reports
5. add audit replay and incident response
6. polish the market workstation and fix copy/encoding
7. close with an integrated quality gate
