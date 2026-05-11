# Autotrade Baseline Quality Gate

Status: Phase 0 baseline
Last reviewed: 2026-05-03

## Purpose

This document defines the minimum quality gate that all product-hardening phases
must preserve. Later phases can add stricter gates, but they cannot remove these
baseline checks.

## Required Commands

Run from repository root unless noted.

```powershell
dotnet restore
dotnet build
dotnet test
```

Run from `webApp` when frontend files or Web App contracts are changed.

```powershell
npm run build
```

## Optional Postgres Smoke

Postgres smoke tests are optional and environment-dependent. They require:

```powershell
$env:AUTOTRADE_TEST_POSTGRES="..."
```

Optional smoke failures caused by a missing local database do not block a phase
unless that phase specifically changes PostgreSQL integration or migration
behavior. If a phase changes persistence, it must either run the smoke test or
record why the environment was unavailable.

## Required Acceptance Evidence by Change Type

### Documentation-only change that does not close a task or phase

- Review generated or updated files for scope accuracy.
- Ensure no secrets or account-specific values were introduced.

### Documentation-only task or phase closure

- Review generated or updated files for scope accuracy.
- Ensure no secrets or account-specific values were introduced.
- Run the baseline commands unless a true external blocker is recorded:
  - `dotnet restore`
  - `dotnet build`
  - `dotnet test`
  - `npm run build` from `webApp`
- Record pass/fail evidence in a durable doc or artifact.
- If task-board status changes, link the evidence path from the task or phase
  file.

### Backend domain or application change

- Targeted tests for changed behavior.
- `dotnet build`
- `dotnet test`
- Evidence notes under `artifacts/` when behavior affects acceptance.

### API change

- Contract tests for request and response semantics.
- Sample JSON for accepted, rejected, and disabled paths where applicable.
- `dotnet test`
- Web App build if frontend types or API calls changed.

### Web App change

- TypeScript build with `npm run build`.
- Browser smoke for affected views.
- Screenshot evidence for key operator states.
- `dotnet test` if API or backend contracts changed.

### Safety, risk, or Live-related change

- Accepted and blocked path tests.
- Audit evidence for high-impact commands.
- No real-money automated CI.
- No real credentials in artifacts.
- Live behavior tested through contract, fake, mock, or explicit manual
  low-risk acceptance only.

## Staged-Change Review Loop

After each completed feature:

1. Run the appropriate regression gate.
2. Stage only the files belonging to that feature.
3. Run a subagent code review over the staged diff.
4. Ask the review to answer:
   - What do these changes do?
   - Is this approach reasonable from first principles?
   - Does it pollute the current architecture or create technical debt?
   - Are there bugs, missing tests, or missing evidence?
5. Fix any valid finding.
6. Re-run the relevant gate.
7. Re-stage the final files.
8. Move to the next feature only after the staged diff is clean.

## Evidence Storage

Use stable paths:

- `docs/product/autotrade-phase-0-quality-gate.md`
- `artifacts/control-room/phase-1/`
- `artifacts/readiness/phase-2/`
- `artifacts/strategies/phase-3/`
- `artifacts/reports/phase-4/`
- `artifacts/audit/phase-5/`
- `artifacts/webapp/phase-6/`
- `artifacts/quality-gates/phase-7/`

Generated artifacts should be ignored or committed according to repository
policy. Evidence summaries and durable docs belong under `docs/` when they are
part of the release claim.

## Closure Rule

A task can move to `[DONE]` only when:

- implementation or documentation is complete
- required checks pass or a true external blocker is recorded
- staged-change review finds no unresolved issue
- evidence paths are linked from the task or phase file
- no known shortcut is left as planned technical debt
