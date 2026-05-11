Autotrade Task Board
====================

Purpose
-------
This folder is the executable development plan for Autotrade product hardening.
It translates the current repository state, README, PRD, acceptance checklist,
control-room prototype, and product-hardening baseline docs into step-by-step
implementation work.

This is not a product spec.
This is the build order, task breakdown, and definition of done for engineering.


Source Documents
----------------
- README.md
- docs/prd.txt
- docs/acceptance_checklist.md
- docs/acceptance_report_template.md
- docs/architecture_summary.md
- docs/architecture_detail.md
- docs/product/autotrade-current-surface-inventory.md
- docs/product/autotrade-safety-policy.md
- docs/product/autotrade-baseline-quality-gate.md
- docs/product/autotrade-product-hardening-roadmap.md
- interfaces/Autotrade.Api/ControlRoom/*
- webApp/src/*


Working Rules
-------------
1. Runtime and language are fixed:
   - backend: C# / .NET 10
   - frontend: React + TypeScript + Vite
   - database: PostgreSQL for durable trading state

2. Safety is product behavior, not a later hardening task:
   - Paper mode must remain the default safe path.
   - Live mode must require explicit readiness and arming evidence.
   - No real API key, private key, or secret can be committed.
   - Client-side code must never receive exchange credentials.

3. Development style is fixed:
   - Contract and regression tests come before broad implementation.
   - Every phase ends with targeted build, test, and product evidence.
   - A phase can be marked `[DONE]` only after its evidence is recorded.
   - Warnings are treated as errors.

4. No technical debt is allowed as a planned outcome:
   - Do not defer correctness behind simplified implementations.
   - Do not bypass risk, audit, or reconciliation to make UI work.
   - Do not add a product surface without acceptance evidence.

5. Bounded contexts remain isolated:
   - Trading owns execution, orders, positions, trades, account state, risk, and audit.
   - MarketData owns catalog, order book, and freshness.
   - Strategy owns lifecycle, scheduling, parameters, and decision records.
   - Shared infrastructure remains generic.

6. Product priority is fixed:
   - first: operator trust and command safety
   - second: readiness and Live arming
   - third: strategy explainability and control
   - fourth: Paper-to-Live promotion evidence
   - fifth: audit replay and incident response
   - sixth: market workstation polish
   - seventh: integrated release gate

7. Review loop is fixed:
   - Implement one feature slice at a time.
   - Run the relevant regression gate.
   - Stage only that slice.
   - Ask a subagent to review the staged diff from first principles.
   - Fix valid findings before continuing.
   - Do not proceed while staged review findings remain unresolved.


Status Conventions
------------------
Use the following status markers when updating tasks later:

- [TODO] not started
- [WIP] in progress
- [BLOCKED] waiting on dependency or decision
- [DONE] completed and accepted


Execution Order
---------------
Read and execute files in this order:

1. 01-phase-0-product-governance-and-safety-baseline.txt
2. 02-phase-1-control-room-contract-and-security.txt
3. 03-phase-2-onboarding-readiness-and-live-arming.txt
4. 04-phase-3-strategy-explainability-and-control.txt
5. 05-phase-4-paper-to-live-promotion-and-run-reports.txt
6. 06-phase-5-audit-replay-risk-and-incident-response.txt
7. 07-phase-6-market-workstation-ux-and-realtime-polish.txt
8. 08-phase-7-integrated-quality-gate-and-release.txt


Additional Boards
-----------------
The following boards are independent from the product-hardening sequence above:

- `09-arc-hackathon-proofalpha.txt`
- `arc-hackathon-proofalpha/`

Use the Arc hackathon board only for the Arc-backed ProofAlpha submission. It
must preserve the same safety baseline: Paper-first, no browser credentials, no
Live execution without arming, and no unverified profitability claims.


Current Sequencing Note
-----------------------
- The current system already has substantial backend capability: Paper/Live
  execution modes, multiple strategies, risk controls, persistence, recovery,
  reconciliation, exports, health checks, and a control-room API.
- The current Web App is a product prototype, not yet an accepted operator
  workstation.
- The next product milestone is not another trading strategy. The next milestone
  is a control room that a developer/operator can safely keep open during Paper
  and carefully armed Live operation.
- Known P0 product defect: existing zh-CN web copy is mojibake. Phase 6 includes
  fixing copy and encoding because unreadable UI text is operationally unsafe.
- Phase 0 is closed by `docs/product/autotrade-phase-0-quality-gate.md`.
