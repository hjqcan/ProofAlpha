# Acceptance Report Template

## Run Metadata

- Date:
- Operator:
- Git commit:
- Environment:
- Database:
- Mode: Paper / Live mock / Live manual

## Configuration Summary

- `Execution:Mode`:
- `Compliance:GeoKycAllowed`:
- `Compliance:AllowUnsafeLiveParameters`:
- Enabled strategies:
- Risk limits:
- Notes on secrets handling:

## Commands

Record exact commands and exit codes:

```bash
dotnet build
dotnet test
dotnet run --project Autotrade.Cli -- config validate --config Autotrade.Cli/appsettings.local.json
dotnet run --project Autotrade.Cli -- health --mode readiness --config Autotrade.Cli/appsettings.local.json
```

## Evidence Files

- `artifacts/config_validate.json`:
- `artifacts/status.json`:
- `artifacts/readiness.json`:
- `artifacts/decisions.csv`:
- `artifacts/orders.csv`:
- `artifacts/order_events.csv`:
- `artifacts/trades.csv`:
- `artifacts/pnl.csv`:
- Logs:

## Acceptance Results

| AC | Result | Evidence | Notes |
| --- | --- | --- | --- |
| AC-001 | Pass / Fail / N/A | | |
| AC-002 | Pass / Fail / N/A | | |
| AC-003 | Pass / Fail / N/A | | |
| AC-004 | Pass / Fail / N/A | | |
| AC-005 | Pass / Fail / N/A | | |
| AC-101 | Pass / Fail / N/A | | |
| AC-102 | Pass / Fail / N/A | | |
| AC-103 | Pass / Fail / N/A | | |

## Live Manual Evidence

Do not paste private keys, API secrets, or full wallet credentials.

- Account used:
- Order notional:
- Order ID:
- Exchange order ID:
- Restart timestamp:
- Cancel/query result:
- Export files:

## Conclusion

- Overall result:
- Blocking issues:
- Residual risks:
- Follow-up tasks:

