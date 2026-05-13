OpportunityDiscovery v2 Breakdown
=================================

Follow the parent board:

- `task-board/10-opportunity-discovery-v2-profit-engine.txt`


Build Order
-----------
1. MarketData durable tape and replay.
2. Source registry and official source packs.
3. OpportunityDiscovery v2 domain model.
4. Feature extraction, scoring, and calibration.
5. Backtest, shadow, paper, and promotion gates.
6. Auto Live micro-allocation and kill criteria.
7. Strategy v2 executable-policy consumption.
8. CLI/API explainability.
9. Integrated validation and runbook.


Context Ownership
-----------------
- MarketData owns market catalog, WebSocket ingestion, CLOB polling snapshots,
  append-only market tape, and point-in-time replay APIs.
- OpportunityDiscovery owns hypotheses, evidence snapshots, source registry,
  features, scores, promotion gates, executable policies, and Live allocations.
- Strategy owns deterministic consumption of executable policies and decision
  records.
- Trading owns execution, account state, order lifecycle, risk events,
  reconciliation, and kill switch actions.
- Shared infrastructure stays provider-neutral and must not become a backdoor
  dependency between bounded contexts.


Status Rules
------------
Use only these markers:

- [TODO] not started
- [WIP] in progress
- [BLOCKED] blocked by dependency or explicit decision
- [DONE] completed with tests and evidence


Backtest Rules
--------------
- Replay readers must accept `asOfUtc`, `fromUtc`, and `toUtc`.
- Query results must never include data after the evaluation point.
- Fill simulation must use order book depth snapshots when available.
- Top-of-book-only backtests are allowed only as a named degraded mode and may
  not pass a Live promotion gate.
- Each backtest run must persist a replay seed, score version, fill model
  version, and input evidence snapshot id.
