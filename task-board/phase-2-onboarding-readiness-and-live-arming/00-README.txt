Phase 2 Breakdown: Onboarding, Readiness, and Live Arming
=========================================================

Follow the parent task file:

- `task-board/03-phase-2-onboarding-readiness-and-live-arming.txt`

Task order:

1. first-run wizard contract
2. readiness diagnostics surface
3. Live arming gate
4. config and credential safety
5. acceptance smoke

Working rules:

- Live arming is a hard gate.
- Paper mode must stay useful when Live is blocked.
- Diagnostics must be actionable and must not leak secrets.
- CLI, API, and Web App should agree on readiness status.
