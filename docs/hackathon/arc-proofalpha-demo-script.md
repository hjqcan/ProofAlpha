# ProofAlpha Arc Hackathon Demo Script

Status: Phase 0 demo contract.

## Demo Objective

Show that ProofAlpha is a paid agent gateway: an unsubscribed wallet is blocked,
a USDC subscription grants access, the agent publishes a proof-backed signal,
Paper execution preserves ProofAlpha safety controls, and outcome/revenue
records feed reputation.

## Preconditions

- Execution mode is Paper unless the operator deliberately runs a separate Live
  arming procedure.
- Demo wallet addresses are public identifiers only. Do not paste private keys,
  mnemonics, API keys, builder secrets, CLOB secrets, RPC secrets, or Circle API
  keys into docs, logs, screenshots, API responses, or WebApp state.
- Every fixture is labeled as fixture in CLI output, API response, WebApp copy,
  and generated artifacts.
- Expected demo strategies: `repricing_lag_arbitrage` and
  `dual_leg_arbitrage`.

## Happy Path

1. Open subscriber portal with wallet `0xUNSUBSCRIBED`.
   - Expected: full signal details are locked.
   - Evidence: `artifacts/arc-hackathon/demo-run/access-denied.json`.
2. Run the subscription step with Arc USDC or fixture USDC.
   - Expected: entitlement result includes tier, strategy ids, expiry, source,
     and tx hash or fixture id.
   - Evidence: `artifacts/arc-hackathon/demo-run/subscription.json`.
3. Recheck entitlement for wallet `0xSUBSCRIBED`.
   - Expected: backend access decision is allowed for
     `repricing_lag_arbitrage`.
   - Evidence: `artifacts/arc-hackathon/demo-run/access-allowed.json`.
4. Unlock one opportunity or strategy signal.
   - Expected: response includes subscriber-safe details, evidence hashes, risk
     envelope, and no secrets.
   - Evidence: `artifacts/arc-hackathon/demo-run/signal-proof.json`.
5. Publish the signal proof to Arc or to a labeled local/testnet fixture.
   - Expected: local intent id and chain/fixture result id are durable and
     idempotent.
   - Evidence: `artifacts/arc-hackathon/demo-run/signal-publication.json`.
6. Execute Paper order with builder attribution evidence.
   - Expected: risk and compliance checks run, command audit records the
     operator action, and signed-order evidence is redacted.
   - Evidence:
     `artifacts/arc-hackathon/demo-run/order-envelope-redacted.json`.
7. Record outcome.
   - Expected: outcome records win, loss, expired, rejected, or stale status and
     does not drop losing or rejected signals from history.
   - Evidence: `artifacts/arc-hackathon/demo-run/performance-outcome.json`.
8. Record revenue settlement or simulated split.
   - Expected: output says whether this is real testnet settlement or a
     simulated local settlement journal.
   - Evidence: `artifacts/arc-hackathon/demo-run/revenue-settlement.json`.
9. Show reputation and subscriber status in WebApp.
   - Expected: reputation includes all published outcomes, including stale,
     expired, rejected, or losing signals.
   - Evidence:
     `artifacts/arc-hackathon/screenshots/subscriber-unlocked.png` and
     `artifacts/arc-hackathon/screenshots/performance-ledger.png`.

## Narration

Use this script for the five-minute recording:

1. "ProofAlpha is not a generic trading bot. It is a paid agent gateway for
   Polymarket strategies."
2. "The first proof point is access control. This unsubscribed wallet cannot
   see full opportunity details."
3. "The user pays USDC on Arc, or this run uses a clearly labeled fixture when
   testnet access is unavailable."
4. "The backend, not the UI, reads entitlement and unlocks the signal."
5. "Before execution, the agent publishes a hash of the signal and supporting
   evidence so the record cannot be rewritten after the result is known."
6. "Paper execution keeps all ProofAlpha safety controls: risk limits, kill
   switch, command audit, and Live arming remain independent of paid access."
7. "Polymarket remains the trading venue. Builder attribution lets the agent
   become a monetizable entry point without claiming custody."
8. "The performance ledger records outcomes, including rejected, stale, expired,
   and losing signals, so reputation is not cherry-picked."
9. "This is a demo of proof, entitlement, execution evidence, and settlement
   records. It is not investment advice and it does not guarantee profit."

## Failure Path To Show If Network Is Unavailable

1. Run the same flow with `source=fixture`.
2. Show the exact failed Arc command, UTC timestamp, endpoint, and error in
   `artifacts/arc-hackathon/demo-run/logs`.
3. Show local intent/result records that would be replayed when the network is
   available.
4. Label every downstream screenshot and JSON artifact as fixture-derived.

## No-Go Lines For Presenter

Do not say:

- "risk-free"
- "guaranteed returns"
- "fully autonomous Live trading"
- "Arc settles the Polymarket trade"
- "ProofAlpha custodies user funds"
- "subscription bypasses risk controls"

Say instead:

- "Paper-first, risk-gated execution"
- "subscriber access to evidence-backed signals"
- "Arc-anchored proof and settlement records"
- "Polymarket remains the venue"
- "builder-attributed flow"

