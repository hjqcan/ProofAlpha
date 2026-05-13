# Autotrade Operator Runbook

## Scope

This runbook is for local evaluation and controlled Paper-mode operation of
Autotrade. It does not authorize unattended Live trading, does not provide
investment advice, and does not guarantee fills, cancellations, profit, or
exchange availability.

## Safety Defaults

- Keep `Execution:Mode` set to `Paper` unless a separate Live readiness review
  is complete.
- Keep the Control Room in `ReadOnly` mode for local product evaluation.
- Do not commit private keys, API credentials, mnemonics, `.env`, user secrets,
  or `appsettings.local.json`.
- Treat every state-changing command as auditable evidence: operator, reason
  code, reason, command result, and follow-up export.

## Setup

From the repository root:

```powershell
dotnet restore
dotnet build
dotnet test
Push-Location webApp
npm install
npm run build
Pop-Location
```

Primary evidence commands:

```powershell
.\scripts\run-phase7-acceptance.ps1
.\scripts\run-phase7-cross-surface-gate.ps1
.\scripts\run-phase7-local-deploy-smoke.ps1
```

Expected artifact locations:

- `artifacts/acceptance/phase-7/acceptance-report.json`
- `artifacts/acceptance/phase-7/acceptance-report.md`
- `artifacts/quality-gates/phase-7/autotrade-product-gate.json`
- `artifacts/quality-gates/phase-7/autotrade-product-gate.md`
- `artifacts/deploy/phase-7/local-smoke.json`
- `artifacts/deploy/phase-7/local-smoke.md`

## Local Product Evaluation

Use the tested local deploy smoke when evaluating API and Web App startup:

```powershell
.\scripts\run-phase7-local-deploy-smoke.ps1
```

The smoke publishes the API into `artifacts/deploy/phase-7/package/api`, builds
`webApp/dist`, starts both surfaces on loopback, checks health/readiness, and
serves the generated Web App assets. If `5080` or `5173` is occupied, it selects
free fallback ports and records them in `local-smoke.json`.

The smoke runs with:

- `Execution__Mode=Paper`
- `AutotradeApi__EnableModules=false`
- `AutotradeApi__ControlRoom__CommandMode=ReadOnly`
- `AutotradeApi__ControlRoom__EnableControlCommands=false`
- `AutotradeApi__ControlRoom__EnablePublicMarketData=false`

## Readiness

After `dotnet build`, use the built CLI DLL for repeatable command evidence:

```powershell
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll config validate --json
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll readiness --json
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll health --mode readiness --json
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll status --json
```

Readiness must be interpreted conservatively:

- `Blocked` or `Unhealthy` means do not arm Live.
- Pending migrations must be resolved before runtime acceptance can be treated
  as release-ready.
- Exchange credentials must be provided only through environment variables or
  user secrets when Live work is intentionally reviewed.

## Paper Run

For a Paper-mode process with a prepared local database:

```powershell
dotnet run --project Autotrade.Cli -- run --config Autotrade.Cli/appsettings.paper.json
```

Before evaluating results, export evidence:

```powershell
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll export decisions --limit 200 --output artifacts\operator\decisions.csv
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll export orders --limit 200 --output artifacts\operator\orders.csv
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll export order-events --limit 200 --output artifacts\operator\order-events.csv
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll export trades --limit 200 --output artifacts\operator\trades.csv
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll export pnl --strategy-id dual_leg_arbitrage --output artifacts\operator\pnl.csv
```

## Strategy Control

Use scoped commands first when a single strategy is affected:

```powershell
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll strategy pause --id dual_leg_arbitrage
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll strategy stop --id dual_leg_arbitrage
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll strategy start --id dual_leg_arbitrage
```

After any state change, run `status --json` and export `order-events`.

## Kill Switch

Use a global hard stop when blast radius is uncertain:

```powershell
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll killswitch activate --level hard --reason-code MANUAL --reason "operator stop" --yes
```

Reset only after orders, positions, readiness, and incident evidence have been
reviewed:

```powershell
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll killswitch reset --yes
```

The Control Room hard-stop and reset actions must be treated the same way:
capture the command result, then export incident evidence.

## Live Arming

Live arming is not part of local product smoke. Before any Live arming attempt:

1. `config validate --json` has no blocking issues.
2. `readiness --json` reports Paper and Live capabilities as ready for the
   intended scope.
3. `artifacts/quality-gates/phase-7/autotrade-product-gate.json` is current.
4. A human operator records the reason, evidence ID, and expiry window.
5. Credentials are supplied through user secrets or environment variables, not
   committed files.

Do not arm Live from stale acceptance evidence or while runtime gates are
blocked by database migrations, API reachability, credentials, or compliance
checks.

## OpportunityDiscovery v2

OpportunityDiscovery v2 is an evidence and validation workflow. The system
seeks measurable positive expected value and does not guarantee profit. Treat
every score, replay, gate, promotion, and allocation as evidence to inspect, not
as a promise of future outcome.

Release evidence for the v2 profit engine is kept under:

```text
artifacts/opportunity-discovery/v2-release/
```

Required local validation before reviewing a v2 opportunity:

```powershell
dotnet restore
dotnet build
dotnet test
```

Use the v2 operator commands from the built CLI DLL after `dotnet build`:

```powershell
dotnet .\interfaces\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll opportunity score --id <opportunity-id> --json
dotnet .\interfaces\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll opportunity replay --id <opportunity-id> --json
dotnet .\interfaces\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll opportunity explain --id <opportunity-id> --json
dotnet .\interfaces\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll opportunity live-status --json
```

When an operator or external workflow supplies a message as evidence, ingest it
as redacted Manual evidence. The command audit records message length, not the
raw message body:

```powershell
dotnet .\interfaces\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll opportunity ingest-message --source-name <source> --title <title> --message <message> --actor <operator> --json
```

When an operator or external workflow supplies public Polymarket wallet activity,
ingest it as Polymarket evidence from a JSON file. The command audit records the
file path and size, not the submitted activity body:

```powershell
dotnet .\interfaces\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll opportunity ingest-account-activity --input .\artifacts\opportunity-discovery\account-activity-request.json --actor <operator> --json
```

The input file shape is:

```json
{
  "walletAddress": "0xabc123abc123abc123abc123abc123abc123abcd",
  "sourceName": "public-account-activity",
  "observedAtUtc": "2026-05-13T15:50:00Z",
  "activities": [
    {
      "marketId": "market-alpha",
      "outcome": "yes",
      "side": "buy",
      "price": 0.42,
      "quantity": 10,
      "executedAtUtc": "2026-05-13T15:40:00Z",
      "transactionHash": "0xtx"
    }
  ]
}
```

Promotion and suspension are destructive operator actions and require explicit
reason capture:

```powershell
dotnet .\interfaces\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll opportunity promote --id <opportunity-id> --actor <operator> --reason "<evidence id and decision>" --yes --json
dotnet .\interfaces\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll opportunity suspend --id <opportunity-id> --actor <operator> --reason "<incident or drift reason>" --yes --json
```

The equivalent API endpoints are:

```text
GET  /api/opportunities/{opportunityId}/score
POST /api/opportunities/user-messages
POST /api/opportunities/account-activity
GET  /api/opportunities/{opportunityId}/replay
POST /api/opportunities/{opportunityId}/promote
GET  /api/opportunities/live-status
POST /api/opportunities/{opportunityId}/suspend
GET  /api/opportunities/{opportunityId}/explain
```

Before any v2 opportunity is considered for Live publication, verify:

- the explain output references persisted evidence, source profiles, conflicts,
  market tape slice, prompt version, model version, feature snapshot, score,
  promotion gates, evaluation runs, policy, and allocation;
- the latest required gates are `Passed`: evidence, backtest, paper,
  execution quality, risk, and compliance;
- replay consumed point-in-time market tape and did not use current order book
  data for historical decisions;
- the executable policy is `Active`, within its validity window, and connected
  to the same active Live allocation recorded on the hypothesis;
- micro-allocation limits are still at reviewed defaults or have a separate
  signed operational review;
- source drift, calibration drift, adverse slippage, drawdown, compliance
  events, and risk events are below kill thresholds;
- no config, fixture, log, or exported replay file contains private material.

If a Live allocation is suspended, preserve all evidence records. Suspension is
an operator state transition and order-cancel workflow, not evidence deletion.

## Incident Review

Use the incident runbook for symptom-specific actions:

```text
docs/operations/autotrade-incident-runbook.md
```

Minimum evidence set:

- current readiness report
- command audit result
- order and order-event exports
- risk drill-down or incident package when a risk event is involved
- replay package for offline analysis

Replay package example:

```powershell
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll export replay-package --limit 200 --output artifacts\operator\replay-package.json
```

## Shutdown

For foreground local runs, stop the process with `Ctrl+C`. After shutdown:

```powershell
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll status --json
dotnet .\Autotrade.Cli\bin\Debug\net10.0\Autotrade.Cli.dll export order-events --limit 200 --output artifacts\operator\shutdown-order-events.csv
```

Keep the final `local-smoke`, `acceptance-report`, quality gate, and exports
with the evaluation record.
