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

Status on 2026-05-13: implemented for the local/testnet demo evidence path. The correlation now survives from signal proof through builder evidence, performance outcome, revenue settlement, and the completion audit.

Goal: persist one run id across signal publication, strategy decision, order intent, order response, builder evidence, performance outcome, and revenue settlement.

Deliverables:

- Correlation record: `arcSignalId -> runSessionId -> clientOrderId -> exchangeOrderId -> tradeIds`.
- CLI/API output that exposes the correlation without secrets.
- Demo script updates that use the same correlation id across artifacts.

Backtest/gate:

- Replay fixture asserts every generated artifact links to the same signal id and run session.
- Failure fixture asserts missing exchange order or missing Arc signal fails closure verification.

Current evidence:

- `artifacts/arc-hackathon/demo-run/execution-correlation.json` records `signalId`, `runSessionId`, `clientOrderId`, `exchangeOrderId`, and the linked evidence files.
- `scripts/run-arc-hackathon-demo.ps1` writes and asserts the correlation across builder attribution, performance outcome, and revenue settlement artifacts.
- `interfaces/ArcContracts/scripts/demo-performance-outcome.cjs` and `interfaces/ArcContracts/scripts/demo-revenue-settlement.cjs` include the same correlation fields in their artifacts.
- `scripts/verify-arc-hackathon-closure.ps1` fails the completion audit unless strategy, signal, run session, client order, and exchange order are aligned.

### Phase 3: Deterministic two-leg arbitrage replay

Status on 2026-05-13: implemented for deterministic fixture replay, CLI evidence export, and completion-audit gating. It still requires real historical Polymarket tape and live execution credentials before making a production profitability or execution claim.

Goal: prove the two-leg arbitrage strategy closes both legs under controlled historical snapshots before any live claim.

Deliverables:

- Replay dataset with YES/NO order books, liquidity, slippage, and fee assumptions.
- Backtest report: edge before fees, edge after fees, fill assumptions, max notional, residual leg risk.
- Risk gate that rejects stale, shallow, or one-leg-only fills.

Backtest/gate:

- Profitable fixture emits two order intents.
- Fee/slippage fixture rejects a false edge.
- Partial-fill fixture fails closed before execution when both legs cannot satisfy minimum size.
- Stale quote fixture rejects paired quotes outside the allowed quote-age window.

Current evidence:

- `context/Strategy/Autotrade.Strategy.Application/Strategies/DualLeg/DualLegArbitrageReplayRunner.cs` implements a depth-aware fill model for YES/NO FOK replay with slippage, fee, max-notional, and quote-age checks.
- `context/Strategy/Autotrade.Strategy.Tests/DualLegArbitrageReplayRunnerTests.cs` covers positive edge, false-edge rejection, shallow-depth rejection, and stale-quote rejection.
- `interfaces/Autotrade.Cli export dual-leg-replay-demo` writes `artifacts/arc-hackathon/demo-run/dual-leg-replay.json`.
- `scripts/run-arc-hackathon-demo.ps1` now runs the replay gate as part of the demo, and `scripts/verify-arc-hackathon-closure.ps1` fails closure unless the replay gate passes.

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

Status on 2026-05-13: implemented for the subscriber portal smoke path. The latest demo run used `ARC_WEBAPP_BROWSER_CHANNEL=chrome`, and the WebApp screenshot log records `channel: "chrome"`.

Goal: test the complete user journey in the browser against the running test environment.

Deliverables:

- Browser script opens the dashboard/subscriber portal.
- It verifies locked state, subscription evidence, unlocked signal details, Paper auto-trade request, builder evidence, performance, and settlement panels.
- Screenshots are stored under `artifacts/arc-hackathon/screenshots/`.

Gate:

- No console errors.
- Desktop and mobile screenshots show non-overlapping content.
- Each panel shows the same Arc signal id.

Current evidence:

- `scripts/arc-hackathon-webapp-check.mjs` supports `ARC_WEBAPP_BROWSER_CHANNEL=chrome`, checks console/page errors, validates locked and unlocked subscriber states, clicks `Request paper auto-trade`, and checks desktop/mobile horizontal overflow.
- `artifacts/arc-hackathon/demo-run/logs/20-webapp-screenshot-capture.stdout.txt` records Chrome launch evidence and screenshot paths.
- `artifacts/arc-hackathon/screenshots/subscriber-blocked.png`, `subscriber-unlocked.png`, `signal-proof.png`, `performance-ledger.png`, `revenue-settlement.png`, and `subscriber-mobile.png` are the latest Chrome-rendered screenshots.

## Completion audit template

Before declaring the loop complete, fill this checklist with evidence paths:

| Requirement | Evidence | Status |
| --- | --- | --- |
| Opportunity discovery produced signal candidate | `artifacts/arc-hackathon/demo-run/signal-proof.json` | Passed |
| Strategy/risk envelope generated or selected | `artifacts/arc-hackathon/demo-run/signal-proof.json` | Passed |
| Backtest/replay passed | `artifacts/arc-hackathon/demo-run/dual-leg-replay.json`, `DualLegArbitrageReplayRunnerTests.cs` | Passed |
| Arc signal proof published before execution | `artifacts/arc-hackathon/demo-run/signal-publication.json` | Passed |
| USDC subscription unlocks protected access | `artifacts/arc-hackathon/demo-run/subscription.json`, `access-allowed.json` | Passed |
| Unsubscribed wallet is denied by backend | `artifacts/arc-hackathon/demo-run/access-denied.json` | Passed |
| Paper execution accepted through backend gate | `artifacts/arc-hackathon/demo-run/autotrade-permission.json` | Passed |
| Polymarket order carries builder metadata | `artifacts/arc-hackathon/demo-run/builder-attribution.json` | Passed |
| Builder trades externally verified or explicitly not available | `artifacts/arc-hackathon/demo-run/builder-attribution.json`; demo status is `not_used`, real status must be `matched` before a production attribution claim | Passed for demo |
| Performance outcome recorded | `artifacts/arc-hackathon/demo-run/performance-outcome.json`, `agent-reputation.json` | Passed |
| Revenue settlement recorded/distributed with clear mode | `artifacts/arc-hackathon/demo-run/revenue-settlement.json`, `local-evm-closed-loop.json` | Passed |
| Browser test passed on desktop and mobile | `artifacts/arc-hackathon/demo-run/logs/20-webapp-screenshot-capture.stdout.txt`, `artifacts/arc-hackathon/screenshots/*.png` | Passed |
| Public wording matches implemented evidence | Demo summary and audit disclose Paper/local EVM mode and avoid production profitability claims | Passed |
