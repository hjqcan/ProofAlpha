Arc Phase 1 Utility Proof Evidence
==================================

Date
----
2026-05-12


Scope
-----
Phase 1 implemented the local strategy utility proof model for ProofAlpha's
Arc-backed paid agent flow. The implementation is intentionally offline-first:
proof documents can be generated, hashed, redaction-checked, exported, and
tested without Arc RPC availability.


Implemented Surface
-------------------
- `Autotrade.ArcSettlement.Application.Contract`
  - canonical signal proof document
  - canonical outcome proof document
  - risk envelope document
  - evidence summary document
  - utility metrics document
  - hash manifest and export result contracts
  - stable JSON serializer options
- `Autotrade.ArcSettlement.Application`
  - deterministic proof hash service
  - utility metrics calculator
  - public proof redaction guard
  - local proof export service
- `Autotrade.ArcSettlement.Tests`
  - deterministic signal hash coverage
  - deterministic outcome hash coverage
  - secret redaction coverage
  - normal chain hash redaction false-positive coverage
  - reputation denominator coverage for expired/rejected/failed outcomes
  - derived expired-signal coverage from `validUntilUtc`
  - rejected proof export coverage


Anti-Cherry-Pick Rule
---------------------
The metrics calculator does not rely only on successful executions. It counts:

- executed outcomes
- expired outcomes
- rejected outcomes
- skipped outcomes
- failed outcomes
- revoked/tombstoned outcomes
- signals with no terminal outcome after `validUntilUtc`, derived as expired

Signals that have not yet reached `validUntilUtc` are counted as pending and do
not enter the reputation denominator until they become terminal.


Secret Handling Rule
--------------------
The redaction guard blocks obvious public-proof leaks:

- private keys
- API keys
- API secrets
- passphrases
- mnemonics / seed phrases
- authorization bearer strings
- raw CLOB/order signatures unless the value is explicitly redacted

The guard intentionally allows normal 32-byte chain hashes so later Arc
contracts can use `bytes32` proof ids without being misclassified as secrets.


Verification
------------
Command:

```powershell
dotnet restore context\ArcSettlement\Autotrade.ArcSettlement.Tests\Autotrade.ArcSettlement.Tests.csproj
```

Result:

```text
Restored Autotrade.ArcSettlement.Application.Contract, Application, and Tests.
```

Command:

```powershell
dotnet test context\ArcSettlement\Autotrade.ArcSettlement.Tests\Autotrade.ArcSettlement.Tests.csproj --no-restore -v minimal
```

Result:

```text
Passed: 7
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


Acceptance Map
--------------
1. Strategy utility proof can be generated without Arc online.
   - Covered by local proof contracts, hash service, metrics calculator, and
     export service tests.
2. Proof documents are stable and hashable.
   - Covered by deterministic signal and outcome hash tests.
3. Proof documents cannot contain obvious secrets.
   - Covered by redaction guard tests.
4. Reputation model includes non-winning outcomes.
   - Covered by expired/rejected/derived-expired denominator tests.
5. Demo artifact can explain why a strategy looked useful.
   - Covered by signal proof fields, evidence ids, expected edge, risk
     envelope hash, outcome proof, and utility metrics.
