# Autotrade Product-Hardening Roadmap

Status: Phase 0 baseline
Last reviewed: 2026-05-03

## Source of Truth

The executable engineering plan is `task-board/00-README.txt`.

Phase files under `task-board/` define intent, scope, acceptance criteria,
planned evidence, and task breakdown. The matching phase folders define the
implementation slices.

## Product Direction

The next milestone is a trusted operator workstation, not another strategy.

Priority order:

1. operator trust and command safety
2. readiness and Live arming
3. strategy explainability and control
4. Paper-to-Live promotion evidence
5. audit replay and incident response
6. market workstation polish
7. integrated release gate

## Phase Order

1. Phase 0: Product Governance and Safety Baseline
2. Phase 1: Control Room Contract and Security
3. Phase 2: Onboarding, Readiness, and Live Arming
4. Phase 3: Strategy Explainability and Control
5. Phase 4: Paper-to-Live Promotion and Run Reports
6. Phase 5: Audit Replay, Risk, and Incident Response
7. Phase 6: Market Workstation UX and Realtime Polish
8. Phase 7: Integrated Quality Gate and Release

## Update Rules

- Product-hardening scope changes must update this roadmap and the affected
  task-board phase files in the same change.
- A phase can close only after evidence exists.
- A task can close only after its acceptance criteria are met.
- If evidence is unavailable because of environment constraints, the blocker
  must be recorded explicitly.
- New strategy work must not precede the trust, readiness, explainability, and
  audit phases unless the user explicitly changes the product priority.

## Current Known P0 Item

The Web App `zh-CN` copy contains mojibake. Phase 6 owns the repair, but the
issue is treated as operator-safety relevant because unreadable controls can
lead to bad decisions.
