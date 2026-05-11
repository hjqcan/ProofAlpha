Phase 1 Breakdown: Control Room Contract and Security
=====================================================

Follow the parent task file:

- `task-board/02-phase-1-control-room-contract-and-security.txt`

Task order:

1. API contract tests
2. local auth and command modes
3. command audit and confirmation
4. UI command safety states
5. build and browser smoke

Working rules:

- Freeze command semantics before UI polish.
- Never expose exchange credentials to the browser.
- Treat disabled controls as product state, not visual decoration.
- Keep all high-risk operator actions auditable.
