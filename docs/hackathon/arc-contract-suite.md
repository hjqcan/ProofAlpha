Arc Contract Suite Foundation
=============================

Date
----
2026-05-12


Scope
-----
Phase 2 establishes the local contract and backend configuration foundation for
the ProofAlpha paid-agent loop.


Contract Workspace
------------------
Path: `interfaces/ArcContracts`

Required npm scripts:

- `npm run build`
- `npm test`
- `npm run deploy:local`
- `npm run deploy:arc-testnet`
- `npm run export-abi`

Required folders:

- `contracts/`
- `scripts/`
- `test/`
- `deployments/`
- `abi/`


Contracts
---------
- `SignalRegistry`
  - rejects duplicate signal ids
  - rejects zero agent
  - rejects empty venue
  - stores public signal summary
  - emits `SignalPublished`
- `StrategyAccess`
  - accepts configured ERC20 payment token
  - maps plan id to strategy key, price, and duration
  - supports `subscribe(strategyKey, planId)`
  - exposes `hasAccess(user, strategyKey)`
  - exposes `accessExpiresAt(user, strategyKey)`
  - emits `StrategySubscribed`
- `PerformanceLedger`
  - records one terminal outcome per signal id
  - rejects duplicate terminal outcomes
  - supports explicit correction event path
  - emits `OutcomeRecorded` and `OutcomeCorrected`
- `RevenueSettlement`
  - event-only settlement journal for MVP
  - records settlement id, signal id, token, gross amount, recipients, bps
    shares, and timestamp
  - rejects duplicate settlement ids
- `TestUsdc`
  - local test ERC20 fixture only
  - not production USDC


Backend Config
--------------
Disabled default config was added to:

- `interfaces/Autotrade.Cli/appsettings.json`
- `interfaces/Autotrade.Api/appsettings.json`

Schema:

```json
{
  "ArcSettlement": {
    "Enabled": false,
    "ChainId": 0,
    "RpcUrl": "",
    "BlockExplorerBaseUrl": "",
    "Contracts": {
      "SignalRegistry": "",
      "StrategyAccess": "",
      "PerformanceLedger": "",
      "RevenueSettlement": ""
    },
    "Wallet": {
      "PrivateKeyEnvironmentVariable": "ARC_SETTLEMENT_PRIVATE_KEY"
    }
  }
}
```


Backend Validation
------------------
`ArcSettlementOptionsValidator` verifies:

- disabled config does not require RPC, contract addresses, or private key
- enabled read-only config validates public chain and contract fields
- enabled write config validates public fields and checks secret presence
- validation reports environment variable names only, never secret values


Deployment Artifact
-------------------
Local deployment artifact:

- `interfaces/ArcContracts/deployments/hardhat-31337.json`

Each contract entry includes:

- chain id
- network name
- contract name
- address
- deployer
- tx hash
- block number
- constructor args
- ABI hash
- deployedAtUtc


Verification
------------
Command:

```powershell
npm run build
```

Result:

```text
Compiled 9 Solidity files successfully.
```

Command:

```powershell
npm test
```

Result:

```text
4 passing
```

Command:

```powershell
npm run export-abi
```

Result:

```text
Exported 5 ABI files.
```

Command:

```powershell
npm run deploy:local
```

Result:

```text
Wrote deployment artifact: interfaces\ArcContracts\deployments\hardhat-31337.json
```

Command:

```powershell
dotnet test context\ArcSettlement\Autotrade.ArcSettlement.Tests\Autotrade.ArcSettlement.Tests.csproj --no-restore -v minimal
```

Result:

```text
Passed: 11
Failed: 0
Skipped: 0
```

Command:

```powershell
dotnet build Autotrade.sln --no-restore -v minimal
```

Result:

```text
Build succeeded.
Warnings: 0
Errors: 0
```


Secret Scan Note
----------------
Deployment artifacts contain public addresses, transaction hashes, constructor
arguments, and ABI hashes. They do not contain private keys, mnemonics, API
keys, API secrets, or RPC credentials.
