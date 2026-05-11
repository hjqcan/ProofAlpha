# Local Product Deploy

This path is for local product evaluation only. It starts the API in Paper,
read-only mode and serves the generated Web App assets. It does not arm Live
trading, does not enable control commands, and does not require committed
secrets.

## Smoke Command

Run the tested Phase 7 smoke from the repository root:

```powershell
.\scripts\run-phase7-local-deploy-smoke.ps1
```

The smoke publishes the API to `artifacts/deploy/phase-7/package/api`, builds
`webApp/dist`, starts both surfaces on loopback, checks `/health/live`,
`/health/ready`, `/api/readiness`, and the Web preview root, then writes:

- `artifacts/deploy/phase-7/local-smoke.json`
- `artifacts/deploy/phase-7/local-smoke.md`

If `5080` or `5173` is already occupied, the script selects free loopback ports
and records the fallback in the smoke report. To force this path during testing:

```powershell
.\scripts\run-phase7-local-deploy-smoke.ps1 -ReserveCommonPorts
```

## Manual API Start

Use the published API package from the smoke or run `dotnet publish` yourself.
For safe local evaluation, keep modules disabled unless a local Postgres
instance is intentionally prepared.

```powershell
$env:ASPNETCORE_URLS = "http://127.0.0.1:5080"
$env:ASPNETCORE_ENVIRONMENT = "Phase7Smoke"
$env:AutotradeApi__EnableModules = "false"
$env:AutotradeApi__ControlRoom__CommandMode = "ReadOnly"
$env:AutotradeApi__ControlRoom__EnableControlCommands = "false"
$env:AutotradeApi__ControlRoom__EnablePublicMarketData = "false"
$env:Execution__Mode = "Paper"
dotnet artifacts\deploy\phase-7\package\api\Autotrade.Api.dll
```

## Manual Web Start

Build with the API base URL that the browser should call:

```powershell
$env:VITE_API_BASE = "http://127.0.0.1:5080"
Push-Location webApp
npm run build
.\node_modules\.bin\vite.cmd preview --host 127.0.0.1 --port 5173 --strictPort
Pop-Location
```

## Package Safety

The local package must not include `.env`, `appsettings.local.json`,
`secrets.json`, or text assets containing obvious private-key, mnemonic,
authorization, password, API key, or secret assignments. The smoke enforces this
against the API package and generated Web App assets.
