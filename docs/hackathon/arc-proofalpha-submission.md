# ProofAlpha Arc Hackathon Submission Contract

Status: Phase 0 frozen for implementation.

## One-Liner

ProofAlpha proves alpha on Arc, sells access with USDC, and executes on
Polymarket through a risk-gated agent.

## Pitch Paragraph

ProofAlpha is a paid agent trading gateway for prediction markets. The AI agent
discovers Polymarket opportunities, records the pre-execution signal and
evidence hashes on Arc, and later records outcomes so subscribers can judge the
agent by full history instead of selected wins. Users pay USDC for access to
agent-generated arbitrage opportunities or for permission to request automated
Paper trading. Polymarket remains the execution venue, and builder attribution
turns the agent into a monetizable market entry point without weakening the
existing ProofAlpha risk, compliance, audit, and Live-arming gates.

## Paid-Agent Loop

1. Agent discovers an opportunity from market data, evidence, or strategy
   reasoning.
2. ProofAlpha creates a redacted utility proof: signal hash, evidence hash,
   reasoning hash, risk envelope, and current strategy version.
3. Arc anchors publication intent and later records outcome, reputation, access,
   and settlement events.
4. A wallet pays or uses a labeled fixture for a USDC subscription.
5. Backend entitlement checks unlock subscriber-safe opportunity details or
   auto-trading permission.
6. Paper execution runs through existing ProofAlpha risk, compliance, kill
   switch, and audit behavior.
7. Polymarket order evidence includes builder metadata so attributable flow can
   be correlated with the Arc signal id.
8. Outcome and revenue settlement records update the agent reputation view.

## Subscription Tiers

Demo prices are hackathon fixture prices, not production billing.

| Tier | Strategy ids covered | Duration | Demo amount | Allowed actions | Disabled reason when missing |
| --- | --- | --- | --- | --- | --- |
| `SignalViewer` | `repricing_lag_arbitrage`, `dual_leg_arbitrage` | 14 days | 10 USDC | API/WebApp can view full published opportunity details, proof hashes, redacted reasoning, performance history, and Arc tx links. CLI can export subscriber-safe signal proof. | `subscription_required`: wallet has no active SignalViewer, AutoTrader, or Operator entitlement for the strategy. |
| `AutoTrader` | `repricing_lag_arbitrage`, `dual_leg_arbitrage` | 14 days | 25 USDC | All SignalViewer actions plus API/CLI/WebApp can request Paper strategy start or copy-trade automation for the subscribed wallet. Live remains blocked unless existing Live arming, command confirmation, risk, compliance, and operator gates pass. | `autotrade_permission_required`: wallet lacks active AutoTrader or Operator entitlement, or the strategy/risk tier is outside the subscription scope. |
| `Operator` | All demo strategies | Hackathon demo window | 0 USDC local/admin fixture | Local/admin can publish signals, replay outcomes, settle fixture revenue, and reset demo data. WebApp must label this as local/admin only. | `operator_only`: action requires local operator mode and cannot be unlocked by public subscription. |

The first real gate for implementation is backend authorization, not UI state.
At least one API or CLI path must reject an unsubscribed wallet before the WebApp
renders locked content as unlocked.

## Strategy Utility Proof Standard

A strategy is useful in the demo only when all measurable fields exist:

| Requirement | Evidence |
| --- | --- |
| Pre-execution signal hash | Canonical signal JSON hash recorded before Paper execution or replay outcome. |
| Strategy, evidence, and reasoning hash | Redacted hashes cover the strategy version, evidence inputs, and subscriber-safe reasoning. |
| Risk envelope | Max notional, risk tier, execution mode, kill-switch state, and Live-arming state are captured. |
| Paper execution or deterministic replay outcome | Existing Paper run report or replay summary identifies decisions, orders, fills, rejects, stale signals, and expired opportunities. |
| Post-execution outcome record | Outcome includes win/loss/expired/rejected/stale status, PnL or simulated PnL, slippage, latency, and exposure notes. |
| Performance ledger entry | Arc/local ledger record links signal id, outcome id, publication hash, and agent reputation inputs. |

This standard proves process integrity and historical behavior. It does not
predict future returns or imply profit.

## Demo Path

1. Show an unsubscribed wallet blocked from full signal details.
2. Subscribe with Arc USDC or a clearly labeled testnet USDC fixture.
3. Backend reads entitlement and returns a positive access decision.
4. Subscriber unlocks a redacted arbitrage opportunity.
5. Publish the signal proof to Arc, or record a labeled local/testnet fixture
   if the network is unavailable.
6. Execute a Paper order with Polymarket builder metadata in redacted evidence.
7. Record the performance outcome on Arc or a labeled local/testnet fixture.
8. Record revenue settlement or split event.
9. Show agent reputation, subscriber access, proof ids, and fixture labels in
   WebApp.

## Testnet, Fixture, And Real Boundaries

| Surface | Phase 0 label | Implementation expectation |
| --- | --- | --- |
| USDC subscription | Testnet or fixture until Arc credentials and faucet funding are configured. | Store chain id, token address, tx hash, block number, and entitlement expiry when real. Store fixture id and reason when simulated. |
| Arc contract writes | Testnet preferred, local fixture allowed only with explicit degraded state. | All writes must be idempotent by domain id and preserve local intent/result records. |
| Polymarket execution | Paper by default. | Live is out of demo scope unless existing Live arming and operator gates pass manually. |
| Builder attribution | Redacted signed-order evidence. | Never expose builder credentials or Polymarket secrets to browser/API responses. |
| Revenue split | Simulated settlement journal until production billing exists. | Public copy must say settlement record or simulated split, not payout. |

## No-Go Language

Reject any UI, doc, pitch, or API description that says or implies:

- guaranteed profit
- risk-free or risk eliminated
- fully autonomous Live trading
- Arc settles the Polymarket venue trade
- ProofAlpha safely custodies user funds
- paid access bypasses ProofAlpha risk, compliance, kill switch, Live arming, or
  command audit

## Acceptance Map

| Acceptance criterion | Evidence |
| --- | --- |
| Paid-agent loop described in one page | This document, sections One-Liner through Demo Path. |
| Subscription tiers defined before contract work | Subscription Tiers table. |
| Demo path includes a real entitlement gate | Paid-Agent Loop step 5 and Subscription Tiers gate note. |
| Strategy utility proof is explicit and measurable | Strategy Utility Proof Standard table. |
| Simulated/testnet parts are labeled | Testnet, Fixture, And Real Boundaries table. |

