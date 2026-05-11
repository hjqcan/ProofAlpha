Arc Hackathon ProofAlpha Task Board
===================================

Purpose
-------
This folder is the executable plan for turning ProofAlpha into a paid,
Arc-backed Polymarket strategy agent gateway.

The core loop is:

```text
agent finds strategy
  -> ProofAlpha proves utility through evidence and outcomes
  -> Arc anchors signal/performance/reputation
  -> user pays USDC subscription on Arc
  -> backend grants opportunity or auto-trading access
  -> Polymarket execution uses builder attribution
  -> Arc records revenue/settlement
```

This board is separate from the existing product-hardening board. Work here may
reuse existing product surfaces, but it must not weaken Paper-first operation,
Live arming, risk gates, command audit, or credential safety.


Execution Order
---------------
1. Phase 0: paid-agent thesis and demo contract.
2. Phase 1: strategy utility proof model.
3. Phase 2: Arc contract suite foundation.
4. Phase 3: signal proof registry.
5. Phase 4: USDC subscription and entitlements.
6. Phase 5: API entitlement gates and auto-trading permissions.
7. Phase 6: Polymarket builder attribution.
8. Phase 7: performance ledger and agent reputation.
9. Phase 8: agent strategy provenance.
10. Phase 9: Control Room subscriber portal.
11. Phase 10: revenue settlement and splits.
12. Phase 11: demo gate and submission.


Working Rules
-------------
1. The paid access path must be real enough to demo: subscription state must
   gate at least one API or command path.
2. The "strategy is useful" claim must be evidence based: include signal
   publication, paper execution outcome, and performance ledger records.
3. Do not publish only winners. Expired, rejected, stale, or losing signals
   must be represented in the model.
4. Do not add Arc calls inside strategy hot loops. Use application services,
   command handlers, or outbox-style workers.
5. Every chain write needs a durable local intent record and a durable local
   result record.
6. Every public claim in the pitch needs an artifact: tx hash, screenshot,
   command log, JSON export, or test output.
7. Keep private keys, RPC credentials, API keys, and wallet secrets in env vars,
   user secrets, or ignored local files.
8. If a hackathon fixture is used, label it as a fixture in UI, docs, and
   artifacts.


Two-Week Execution Plan
-----------------------
Day 1:
- Complete Phase 0.
- Freeze the demo story and entitlement tiers.

Day 2:
- Complete Phase 1.
- Define proof metrics, canonical evidence documents, and anti-cherry-pick
  rules.

Day 3-4:
- Complete Phase 2.
- Build and test Arc contracts locally.
- Deploy SignalRegistry and StrategyAccess to Arc testnet if possible.

Day 5:
- Complete Phase 3.
- Publish one deterministic signal proof to Arc.

Day 6:
- Complete Phase 4.
- Implement subscription/entitlement mirror and backend read path.

Day 7:
- Complete Phase 5.
- Gate opportunity details and auto-trading permission paths by entitlement.

Day 8:
- Complete Phase 6.
- Produce builder-code signed order evidence.

Day 9:
- Complete Phase 7.
- Record signal outcomes to performance ledger and compute agent reputation.

Day 10:
- Complete Phase 8.
- Anchor OpportunityDiscovery or SelfImprove provenance.

Day 11:
- Complete Phase 9.
- Show subscriber portal and Arc proof state in WebApp.

Day 12:
- Complete Phase 10.
- Record revenue settlement or split event.

Day 13:
- Complete Phase 11.
- Run demo script and command gates.

Day 14:
- Record final video, polish screenshots, and prepare submission.


Status Markers
--------------
- `[TODO]` not started
- `[WIP]` in progress
- `[BLOCKED]` blocked by external service, missing decision, or failing gate
- `[DONE]` complete with evidence


Definition of Done
------------------
A task is done only when:

- code/docs are complete for that slice
- tests or demo evidence exist
- entitlement behavior is explicit when applicable
- safety and secret handling are checked
- evidence artifact paths are recorded
- the implementation does not bypass bounded-context ownership

