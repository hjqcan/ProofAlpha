# Arc-backed Polymarket Arbitrage Agent Closure Plan

This plan turns the current Arc demo loop into a verifiable end-to-end agent loop:

1. Discover a Polymarket opportunity.
2. Produce or select a strategy with a reproducible risk envelope.
3. Publish the signal proof to Arc before execution.
4. Execute in Paper first, then Live only when separately armed.
5. Attribute the order flow through Polymarket builder metadata.
6. Record performance, fees, and revenue settlement evidence on Arc.
7. Verify the full user flow in a real browser against a running test environment.

## Non-negotiable success criteria

- Every public claim maps to a file, transaction, API response, screenshot, or test result.
- No production claim relies only on local fixture data.
- Paid access is enforced by the backend, not only the UI.
- Live execution remains disabled unless the existing live gates are explicitly armed.
- Backtests or replay tests exist for each trading feature before it is used in a demo script.
- Builder attribution must distinguish signed-order evidence from externally verified builder trades.
- Revenue settlement must distinguish subscription/testnet payout evidence from production accounting.

## Delivery phases

### Phase 1: Close builder attribution verification

Status on 2026-05-13: implemented for client, matcher, CLI entrypoint, and verifier compatibility. It still requires real configured Polymarket credentials and real attributed order flow before making a production attribution claim.

Goal: replace the current "signed envelope only" builder evidence with an optional external verification path.

Deliverables:

- Polymarket CLOB client method for `GET /builder/trades`.
- Builder trade model that keeps raw builder code out of public evidence.
- Matcher that correlates Arc signal id, client order id, exchange order id, market id, and builder code hash.
- Contract tests using WireMock fixtures for pagination, match, and not-found cases.

Backtest/gate:

- Unit/contract test proves a recorded order envelope can be matched against historical builder trade fixtures.
- Negative test proves unmatched builder trades do not produce a false success.

Current evidence:

- `Shared/Autotrade.Polymarket/PolymarketClobClient.cs` implements `GET /builder/trades`.
- `Shared/Autotrade.Polymarket/BuilderAttribution/PolymarketBuilderAttribution.cs` matches builder trades back to Arc signal evidence without exposing raw builder code or signatures.
- `interfaces/Autotrade.Cli/Commands/ArcBuilderCommands.cs` supports `--verify-builder-trades`.
- `Shared/Autotrade.Polymarket.Tests/PolymarketClientContractTests.cs` covers pagination, matched evidence, and not-found evidence.
- `scripts/verify-arc-hackathon-closure.ps1` accepts demo `not_used` and real `matched` external verification states.

### Phase 2: One-run signal-to-order correlation

Goal: persist one run id across signal publication, strategy decision, order intent, order response, builder evidence, performance outcome, and revenue settlement.

Deliverables:

- Correlation record: `arcSignalId -> runSessionId -> clientOrderId -> exchangeOrderId -> tradeIds`.
- CLI/API output that exposes the correlation without secrets.
- Demo script updates that use the same correlation id across artifacts.

Backtest/gate:

- Replay fixture asserts every generated artifact links to the same signal id and run session.
- Failure fixture asserts missing exchange order or missing Arc signal fails closure verification.

### Phase 3: Deterministic two-leg arbitrage replay

Goal: prove the two-leg arbitrage strategy closes both legs under controlled historical snapshots before any live claim.

Deliverables:

- Replay dataset with YES/NO order books, liquidity, slippage, and fee assumptions.
- Backtest report: edge before fees, edge after fees, fill assumptions, max notional, residual leg risk.
- Risk gate that rejects stale, shallow, or one-leg-only fills.

Backtest/gate:

- Profitable fixture emits two order intents.
- Fee/slippage fixture rejects a false edge.
- Partial-fill fixture records hedge or abort behavior.

### Phase 4: Arc access and settlement hardening

Goal: make the subscription/access path auditable from chain event to backend entitlement.

Deliverables:

- Entitlement sync evidence includes chain id, contract address, block, tx hash, plan id, strategy key, and expiry.
- Access decision audit links denied/allowed API responses to the source entitlement transaction.
- Revenue settlement artifact states whether funds were distributed, recorded only, or simulated.

Backtest/gate:

- API tests prove unsubscribed wallets cannot fetch protected reasoning or start Paper auto-trade.
- Sync tests prove stale, wrong-chain, wrong-plan, or expired subscriptions are rejected.

### Phase 5: Browser verification

Goal: test the complete user journey in the browser against the running test environment.

Deliverables:

- Browser script opens the dashboard/subscriber portal.
- It verifies locked state, subscription evidence, unlocked signal details, Paper auto-trade request, builder evidence, performance, and settlement panels.
- Screenshots are stored under `artifacts/arc-hackathon/demo-run/`.

Gate:

- No console errors.
- Desktop and mobile screenshots show non-overlapping content.
- Each panel shows the same Arc signal id.

## Completion audit template

Before declaring the loop complete, fill this checklist with evidence paths:

| Requirement | Evidence | Status |
| --- | --- | --- |
| Opportunity discovery produced signal candidate |  | Pending |
| Strategy/risk envelope generated or selected |  | Pending |
| Backtest/replay passed |  | Pending |
| Arc signal proof published before execution |  | Pending |
| USDC subscription unlocks protected access |  | Pending |
| Unsubscribed wallet is denied by backend |  | Pending |
| Paper execution accepted through backend gate |  | Pending |
| Polymarket order carries builder metadata |  | Pending |
| Builder trades externally verified or explicitly not available |  | Pending |
| Performance outcome recorded |  | Pending |
| Revenue settlement recorded/distributed with clear mode |  | Pending |
| Browser test passed on desktop and mobile |  | Pending |
| Public wording matches implemented evidence |  | Pending |
