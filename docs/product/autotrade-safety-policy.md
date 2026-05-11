# Autotrade Safety Policy

Status: Phase 0 baseline
Last reviewed: 2026-05-03

## Purpose

This policy defines product safety invariants for future implementation. These
rules are acceptance constraints, not recommendations. A later phase can refine
the implementation, but it must not weaken these invariants without an explicit
product decision and updated tests.

## Non-Negotiable Invariants

1. Paper mode is the safe default.
2. Live order placement requires explicit configuration, compliance allowance,
   credential presence, readiness, risk eligibility, and Live arming.
3. The browser must never receive private keys, API secrets, or exchange
   credential material.
4. The control room must not bypass bounded-context services for execution,
   risk, compliance, audit, or strategy lifecycle.
5. High-impact operator commands must be auditable whether they succeed or
   fail.
6. Stale market data must not be presented as live actionable state.
7. No feature may claim profitability, investment advice, or risk elimination.
8. No phase can close without build, test, and relevant product evidence.

## Execution Modes

### Paper

Paper mode is allowed without Live credentials. Paper mode may surface
compliance warnings, but those warnings must not block Paper-only simulation
unless a separate safety issue makes the process unhealthy.

Paper behavior requirements:

- Use the same strategy and risk decision path as Live where practical.
- Keep Paper-vs-Live differences explicit at the execution boundary.
- Persist Paper decisions, orders, order events, positions, and PnL evidence.
- Clearly label Paper reports and UI states.

### Live

Live mode can place real orders only after all gates pass.

Required gates:

- `Execution:Mode=Live`
- compliance allowance for the operator jurisdiction and account context
- required CLOB credentials present through environment variables or user
  secrets, never committed files
- database and migrations ready
- market data reachable and fresh enough for the configured strategy
- account sync and reconciliation healthy enough for configured operation
- risk limits valid and not currently blocking
- kill switch inactive unless the operation is specifically a safe reset or
  incident-response action
- Live arming record exists and is current

Live arming invalidation triggers:

- critical risk configuration changed
- execution mode changed
- strategy config version changed for armed strategies
- credential presence changed
- account sync detects drift beyond configured tolerance
- readiness changes to unhealthy for a Live-critical check

## Command Modes

### Read-Only

Read-only mode can inspect state but cannot mutate strategy lifecycle, risk
state, configuration, orders, or arming state.

### Degraded

Degraded mode means at least one required subsystem is impaired, but the process
can still provide enough state for inspection or safe incident response.

Examples:

- market data is reachable but stale or delayed
- WebSocket is disconnected while REST health still works
- background worker heartbeat is late but the process is responsive
- account sync is delayed
- non-Live-critical readiness checks are unhealthy

Allowed behavior:

- inspect snapshots, health, readiness, audit, orders, positions, and risk state
- export evidence
- activate a hard stop
- pause or stop strategies when the command path itself is healthy
- run Paper-only workflows if the degraded check does not affect their safety

Blocked behavior:

- Live arming
- Live order placement
- starting or resuming Live-impacting strategies
- resetting a kill switch when the degraded state prevents safe verification
- parameter changes that would affect enabled Live strategies
- any command whose required bounded-context service is unhealthy

Operator visibility requirements:

- The control room must show degraded state as distinct from ready, blocked, and
  offline.
- Degraded checks must include source, last observed time, reason, and
  remediation hint.
- Strategy and order controls disabled by degraded state must show a disabled
  reason.
- Audit and export paths must remain available when possible so the operator can
  collect evidence before shutdown.

Audit and readiness behavior:

- Entering or leaving degraded state should be visible in readiness evidence.
- High-impact commands attempted during degraded state must record whether they
  were accepted or blocked and why.
- Degraded state cannot satisfy Live arming evidence.

### Paper Commands

Paper command mode can control Paper strategies and Paper-safe workflows. It
must still audit high-impact actions such as hard stop, reset, and parameter
changes.

### Live Commands

Live command mode is available only when Live readiness and arming gates pass.
All Live-impacting commands require explicit confirmation and audit.

## High-Impact Commands

High-impact commands include:

- hard stop
- kill-switch reset
- strategy start, pause, resume, or stop in Live mode
- parameter updates for enabled strategies
- config rollback affecting enabled strategies
- Live arming or disarming
- cancel open orders
- incident response actions

Every high-impact command must record:

- command type
- target entity
- previous state when available
- requested state or value
- operator source
- timestamp
- validation result
- execution result
- failure reason when failed

## Credential and Secret Handling

Allowed behavior:

- Report whether required secret fields are present.
- Redact secret values in logs, API responses, UI errors, and exports.
- Use environment variables or user secrets for local development.

Forbidden behavior:

- Commit real API keys, private keys, or API secrets.
- Send CLOB credentials to the Web App.
- Include secrets in screenshots, artifacts, reports, or audit packages.
- Log signed payload material in a way that enables credential reuse.

## Market Data Freshness

Market data freshness must be visible anywhere it affects decisions.

Required states:

- fresh
- delayed
- stale
- unavailable
- error

Safety behavior:

- Strategy decisions should include the data freshness used.
- Stale order-book data must be visible in the control room.
- Any strategy blocked by stale data must show that blocked reason.
- Operators must not need to infer freshness from timestamps alone.

## Risk and Kill Switch

The kill switch blocks new order placement while active. A hard stop must be
treated as unhealthy for readiness when it blocks trading. Resetting a kill
switch is a high-impact command and must be confirmed and audited.

Risk-triggered behavior must record:

- limit name
- configured limit
- observed value
- trigger reason
- selected action
- affected orders or exposures when available
- mitigation result

## Architecture Boundaries

Trading owns execution, orders, trades, positions, account state, risk, and
audit.

MarketData owns market catalog, order-book state, subscriptions, and freshness.

Strategy owns lifecycle, scheduling, parameter use, decision logging, and
strategy-specific signal evaluation.

The control-room API composes read models and routes commands through bounded
context services. It must not become a second implementation of Trading,
MarketData, or Strategy behavior.

## Evidence Requirements

Every feature phase must produce evidence appropriate to the blast radius:

- backend changes: targeted tests plus `dotnet test`
- API changes: contract tests and sample JSON
- frontend changes: `npm run build` and browser smoke where UI behavior changed
- safety changes: blocked and accepted path evidence
- Live-related changes: mock or contract evidence only unless manual low-risk
  Live acceptance is explicitly documented

No evidence artifact may contain real secrets.
