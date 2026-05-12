# ProofAlpha Polymarket Builder Attribution

## What this proves

Phase 6 proves that ProofAlpha can produce Polymarket order-flow evidence with
builder metadata attached to the signed CLOB order envelope. The redacted
evidence links:

- `arcSignalId`
- `clientOrderId`
- strategy and market identifiers
- redacted builder-code hash
- redacted signed-order hash

This is enough for the hackathon demo to truthfully show that a ProofAlpha
signal can become attributable Polymarket order flow without exposing signing
material.

## What this does not prove

This evidence does not claim real builder revenue, matched trades, or profitable
execution. Real builder revenue still requires external Polymarket settlement
or builder-trades verification against live attributed orders.

## Redaction boundary

Evidence files must not contain:

- private keys
- CLOB API secrets or passphrases
- raw reusable signatures
- raw builder code

The exporter hashes order-owner, maker, signer, token, salt, metadata, builder,
and signature values before writing evidence.

## Demo command

```powershell
dotnet run --project interfaces\Autotrade.Cli -- arc builder evidence --demo --json --yes --client-order-id demo-client-order-1 --strategy-id dual_leg_arbitrage --market-id demo-market-1 --arc-signal-id 0xcccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc --token-id 1 --price 0.42 --size 10 --output artifacts\arc-hackathon\demo-run\builder-attribution.json --envelope-output artifacts\arc-hackathon\demo-run\order-envelope-redacted.json
```

The `--demo` mode uses deterministic local-only signer settings and should be
described as simulated order evidence, not real venue execution.
