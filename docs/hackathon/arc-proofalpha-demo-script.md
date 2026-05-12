# ProofAlpha Arc Hackathon Five-Minute Demo Script

## Setup

Run the full demo gate first:

```powershell
.\scripts\run-arc-hackathon-demo.ps1
```

Keep these files open during recording:

- `artifacts/arc-hackathon/demo-run/demo-summary.md`
- `artifacts/arc-hackathon/screenshots/subscriber-blocked.png`
- `artifacts/arc-hackathon/screenshots/subscriber-unlocked.png`
- `artifacts/arc-hackathon/screenshots/signal-proof.png`
- `artifacts/arc-hackathon/screenshots/performance-ledger.png`
- `artifacts/arc-hackathon/screenshots/revenue-settlement.png`

## Talk Track

### 0:00 - Product Frame

"ProofAlpha is a paid agent gateway for Polymarket strategies. Arc is used for
proof, paid access, reputation, and revenue settlement evidence. Polymarket
remains the execution venue, and this demo is Paper-first."

### 0:30 - Agent Finds Strategy

"The agent starts from an opportunity record. Before any execution result is
known, ProofAlpha writes a signal proof with the strategy id, market id,
reasoning hash, risk envelope hash, expected edge, max notional, and valid-until
window."

Show `signal-proof.json` or the signal proof screenshot.

### 1:00 - User Blocked Before Subscription

"The first gate is backend entitlement. An unsubscribed wallet cannot view the
full signal or request Paper automation."

Show `subscriber-blocked.png` and `access-denied.json`.

### 1:35 - User Subscribes With USDC/Testnet Token

"The demo subscription uses a local Hardhat USDC-style token and
`StrategyAccess`. The subscription event is mirrored into ProofAlpha
entitlement state."

Show `subscription.json`, then `access-allowed.json`.

### 2:05 - Signal Proof Published To Arc

"Now the signal proof is anchored through the `SignalRegistry` path. The summary
contains the local EVM transaction hash and the same signal id used downstream."

Show `signal-publication.json`.

### 2:40 - Paper Execution With Builder Attribution

"Execution stays Paper-first. The signed-order evidence is redacted: we keep
hashes and builder attribution correlation, not raw signatures or secrets."

Show `builder-attribution.json` and `order-envelope-redacted.json`.

### 3:20 - Performance Ledger Records Outcome

"The demo deliberately records an `ExecutedLoss`. Reputation has to include
losses, rejected signals, expired signals, and pending signals. It is not a
selected-wins feed."

Show `performance-ledger.png` and `performance-outcome.json`.

### 4:05 - Revenue Settlement Records Monetization

"Finally the revenue path records a subscription-fee settlement split: 70% agent
owner, 20% strategy author, and 10% platform. This is settlement evidence, not a
claim that Arc settles Polymarket venue trades."

Show `revenue-settlement.png` and `revenue-settlement.json`.

### 4:40 - Safety Posture

"Paid access does not bypass ProofAlpha risk, compliance, kill switch, command
audit, or Live arming. This is not investment advice, does not guarantee profit,
and does not claim custody of user funds."

End on `demo-summary.md` and `secret-scan.md`.

## No-Go Lines

Do not say:

- "guaranteed profit"
- "risk-free"
- "fully autonomous live trading"
- "Arc settles the Polymarket trade"
- "ProofAlpha custodies user funds"
- "subscription bypasses safety controls"

Say instead:

- "Paper-first, risk-gated execution"
- "subscriber access to evidence-backed signals"
- "Arc-recorded proof, access, performance, and revenue evidence"
- "Polymarket remains the venue"
- "builder-attributed order-flow evidence"
