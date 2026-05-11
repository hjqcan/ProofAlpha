# Autotrade Incident Runbook

## Scope

This runbook maps common control-room symptoms to safe operator actions. It does not promise exchange outcomes, fills, cancellations, profit and loss recovery, or kill-switch clearance. Every operator action must be treated as a recorded command with confirmation, disabled reason, result, and follow-up evidence.

## First Principles

- Preserve evidence before changing state when risk is not increasing.
- Stop new risk before investigating root cause when risk is increasing.
- Never reset a kill switch only because the UI is quiet; verify open orders, unhedged exposure, readiness, and recent audit entries.
- Prefer scoped strategy pause or stop when a single strategy is isolated; use global hard stop when the blast radius is uncertain.
- Treat exchange-side cancellation as an attempt. Reconcile order status after the command result.

## Symptom to Action Map

| Symptom | Primary action | Follow-up evidence | Notes |
| --- | --- | --- | --- |
| Rapid order creation, duplicated orders, or unknown strategy loop | Hard stop | Export incident package, audit timeline, open orders | Hard stop blocks new strategy activity but does not promise exchange cancellation. |
| Kill switch active after a risk event | Risk drill-down, export incident package | Risk event detail, unhedged exposure list, command audit | Reset only after open orders and exposures are reconciled. |
| One strategy is stale, faulted, or using suspect parameters | Pause strategy | Strategy decisions, parameter versions, readiness report | Pause keeps the strategy available for inspection. |
| A strategy should remain offline until operator review | Stop strategy | Strategy decisions, audit timeline | Stop is stronger than pause and should be explicit in the incident notes. |
| Open orders remain after hard stop or strategy pause | Cancel open orders | Command result, order status reconciliation | Requires `CONFIRM`; unsupported when order repository or execution service is unavailable. |
| Unhedged exposure timeout or hedge leg uncertainty | Pause affected strategy, cancel scoped open orders, export package | Unhedged exposure drill-down, related risk event | Do not assume the hedge is fixed until positions and orders reconcile. |
| Exchange API, credentials, or websocket health is degraded | Export package, hard stop if risk is increasing | Readiness report, audit timeline | Avoid reset while telemetry is degraded. |
| Post-incident handoff or offline analysis | Export incident package | Package JSON, runbook reference, audit timeline | Export is read-only and can be repeated safely. |

## Action Safety

| Action | Confirmation | Disabled when | Expected result |
| --- | --- | --- | --- |
| Hard stop | `CONFIRM` | Commands are disabled, transport is unhealthy, or hard stop is already active | Global kill switch command is audited and returns Accepted, Rejected, Disabled, or ConfirmationRequired. |
| Reset kill switch | `CONFIRM` | Commands are disabled or no kill switch is active | Reset command is audited; operator must verify risk state before and after. |
| Pause strategy | None in paper mode; live re-enable still requires confirmation elsewhere | Commands are disabled, no strategy is registered, or the strategy cannot accept the target state | Desired state is changed to Paused and audited. |
| Stop strategy | None in paper mode; live re-enable still requires confirmation elsewhere | Commands are disabled, no strategy is registered, or the strategy cannot accept the target state | Desired state is changed to Stopped and audited. |
| Cancel open orders | `CONFIRM` | Commands are disabled, no open orders are visible, order repository is unavailable, or execution service is unavailable | Each cancellable client order id is attempted and the aggregate result is audited. |
| Export incident package | None | Not applicable | Read-only package with snapshot, action catalog, runbook references, and export references. |

## Minimum Closure Checklist

- Export an incident package for the final state.
- Record the operator, reason code, and reason for each state-changing command.
- Reconcile open orders and positions after cancellation attempts.
- Check the audit timeline for command ordering, duration, outcome, and failures.
- Leave the kill switch active until the reason for activation is understood and documented.
- Re-run readiness diagnostics before resuming or re-arming live execution.
