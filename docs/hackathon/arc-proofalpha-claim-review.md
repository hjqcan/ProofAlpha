# ProofAlpha Arc Hackathon Claim Review

| Public claim | Implemented | Evidence | Required disclosure |
| --- | --- | --- | --- |
| ProofAlpha blocks unsubscribed wallets from full signal access. | Yes | `access-denied.json`, Subscriber Portal blocked screenshot, API/CLI access contracts. | Backend entitlement gate, not only UI state. |
| A subscribed wallet can unlock signal details and Paper auto-trade permission. | Yes | `subscription.json`, `entitlement-status.json`, `access-allowed.json`, `autotrade-permission.json`, unlocked screenshot. | Default run uses local Hardhat EVM/testnet-style evidence; Live remains separately gated. |
| Signal proof is recorded before outcome. | Yes | `signal-proof.json`, `signal-publication.json`, signal proof screenshot. | Proof records hashes and metadata, not future performance. |
| Polymarket order flow can carry builder attribution evidence. | Yes | `builder-attribution.json`, `order-envelope-redacted.json`. | Evidence is redacted and demo/Paper; no raw signature or CLOB secret is exposed. |
| Performance history includes negative or rejected outcomes. | Yes | `performance-outcome.json`, `agent-reputation.json`, performance screenshot. | Historical record only; not investment advice. |
| Revenue settlement path records and distributes splits. | Yes | `revenue-settlement.json`, `revenue-settlement-cli-journal.json`, revenue screenshot. | Demo/testnet payout evidence only; no production accounting or tax claim. |
| Paid access does not bypass safety controls. | Yes | Control Room risk/permission surfaces and Live arming design. | Live remains blocked unless separately armed and approved. |
| Arc settles the Polymarket venue trade. | No | N/A | Do not make this claim. Arc records proof/access/performance/revenue evidence around the agent product. |
| ProofAlpha guarantees profit or reduces risk to zero. | No | N/A | Do not make this claim. |
| ProofAlpha custodies user funds. | No | N/A | Do not make this claim. |

## Final Wording Guardrails

- Say "Paper-first, risk-gated execution."
- Say "local EVM/testnet-style evidence" unless a real Arc testnet run is shown.
- Say "builder-attributed order evidence," not "verified production order flow,"
  unless real attributed orders are available.
- Say "revenue settlement and testnet payout evidence," not "production payout."
