# ProofAlpha Arc Hackathon Submission

## One-Liner

ProofAlpha is a paid, proof-backed Polymarket agent gateway: Arc records access,
signal proof, performance history, and revenue settlement evidence while
ProofAlpha keeps execution Paper-first and risk-gated.

## Problem: AI Trading Agents Need Trust And Monetization

Prediction-market agents can produce useful signals, but subscribers need more
than a screenshot of a winning call. They need to know what the agent knew before
execution, whether losing or rejected calls remain in history, and whether paid
access is enforced by backend entitlement checks instead of UI state.

Agent builders also need a clean monetization path. A useful agent should be able
to sell access, attribute downstream order flow, and record revenue splits without
claiming custody of user funds or settlement of the venue trade.

## Product: Paid Arc-Backed ProofAlpha Agent Gateway

The demo packages ProofAlpha as a subscriber portal and backend proof pipeline:

1. The agent publishes a pre-execution signal proof.
2. An unsubscribed wallet is blocked from full signal details.
3. A USDC/testnet subscription is mirrored into local entitlement state.
4. A subscribed wallet unlocks signal details and Paper auto-trade permission.
5. A redacted Polymarket order envelope records builder attribution.
6. Performance outcome and reputation records keep losses, rejects, and expired
   signals in the ledger.
7. Revenue settlement records split the subscription revenue path.

## Why Arc

Arc is the proof and monetization layer in this demo:

- `SignalRegistry` anchors the signal id, reasoning hash, risk envelope hash,
  expected edge, max notional, and validity window before outcome.
- `StrategyAccess` records paid access with a USDC/testnet token subscription.
- `PerformanceLedger` records post-signal outcomes for reputation.
- `RevenueSettlement` records monetization evidence and can distribute held
  ERC20 subscription revenue to the configured split recipients.

The default demo run uses local Hardhat EVM artifacts. The public copy must label
that as local/testnet evidence unless a real Arc testnet deployment is supplied.

## Why Polymarket

Polymarket is the execution venue and market data surface. ProofAlpha keeps
Polymarket venue execution separate from Arc proof records:

- Paper execution remains the default demo mode.
- Builder attribution is captured as redacted signed-order evidence.
- Arc does not settle the Polymarket trade; it records proof, access,
  performance, and settlement events around the agent product.

## Architecture

See `docs/hackathon/arc-proofalpha-architecture.md` for the final Mermaid
diagram. The main path is:

- OpportunityDiscovery and SelfImprove feed candidate strategies.
- Strategy creates a signal under a risk envelope.
- ArcSettlement publishes the signal proof and mirrors subscription access.
- Control Room and Subscriber Portal render the gated experience.
- Trading simulates or executes Paper orders against Polymarket CLOB interfaces.
- Builder attribution links the order envelope back to the Arc signal id.
- Performance and revenue records update reputation and monetization evidence.

## Demo Flow

Run:

```powershell
.\scripts\run-arc-hackathon-demo.ps1
.\scripts\verify-arc-hackathon-closure.ps1
```

The runner builds contracts, runs contract tests, restores/builds/tests .NET,
builds the WebApp, publishes signal proof, records subscription/access evidence,
exports builder-attributed Paper order evidence, records performance and revenue
events, captures screenshots, scans for secrets, and writes:

- `artifacts/arc-hackathon/demo-run/demo-summary.md`
- `artifacts/arc-hackathon/demo-run/demo-run-record.json`
- `artifacts/arc-hackathon/demo-run/completion-audit.json`
- `artifacts/arc-hackathon/demo-run/secret-scan.md`
- `artifacts/arc-hackathon/screenshots/*.png`

## Subscription And Entitlement Model

The demo plan is `PaperAutotrade` for `repricing_lag_arbitrage`. A wallet
without an active mirror record receives a denied access decision. After the
local `StrategySubscribed` event is mirrored, the subscribed wallet receives an
active status with `ViewSignals`, `ViewReasoning`, `ExportSignal`, and
`RequestPaperAutoTrade` permissions.

Paid access does not bypass risk, compliance, kill switch, command audit, or Live
arming gates. Paper auto-trade permission is separate from Live execution.

## Strategy Utility Proof Model

The signal proof includes:

- `signalId` / opportunity hash
- agent address
- strategy and venue
- evidence ids
- reasoning hash
- risk envelope hash
- expected edge bps
- max notional USDC
- validity window

This proves a pre-outcome record existed. It does not predict returns or imply
future profitability.

## Performance And Reputation Ledger

The demo records an `ExecutedLoss` outcome to prove that the ledger can include
negative results. Reputation evidence includes total signals, terminal signals,
pending signals, wins, losses, expired signals, rejected signals, average PnL
bps, slippage bps, and risk rejection rate.

This is historical evidence only. It is not investment advice.

## Revenue Settlement Model

The revenue path records a `SubscriptionFee` settlement for the signal id and
subscription transaction hash. The split is deterministic:

- Agent owner: 70%
- Strategy author: 20%
- Platform: 10%

The local EVM path proves both the `RevenueSettlement` recording path and the
ERC20 split mechanics. Production accounting and tax reporting are out of scope.

## Safety

- Paper-first execution.
- Live trading remains blocked unless the existing ProofAlpha Live arming,
  operator confirmation, risk, compliance, and kill-switch checks pass.
- Demo artifacts and browser state must not expose raw private keys, mnemonics,
  API secrets, passphrases, bearer tokens, or unredacted signatures.
- Redacted order evidence contains hashes, not raw signatures or CLOB secrets.

## Testnet And Simulated Disclosures

The default hackathon runner uses local Hardhat EVM contracts. If Arc testnet
credentials are configured later, the same proof model can point at testnet
transactions, but this submission should present the current artifacts as local
EVM/testnet-style evidence.

Use `.\scripts\verify-arc-hackathon-closure.ps1 -RequireArcTestnet` before
claiming real Arc Testnet completion. Without Arc Testnet deployment artifacts,
the verifier reports local evidence as passed while keeping the Arc Testnet
requirement open.

To produce those real Arc Testnet artifacts, configure a dedicated funded
testnet wallet and run:

```powershell
$env:ARC_TESTNET_RPC_URL="https://rpc.testnet.arc.network"
$env:ARC_TESTNET_CHAIN_ID="5042002"
$env:ARC_SETTLEMENT_USDC_ADDRESS="0x3600000000000000000000000000000000000000"
$env:ARC_SETTLEMENT_PRIVATE_KEY="0x..."
$env:ARC_SETTLEMENT_TREASURY="0x..."
.\scripts\run-arc-testnet-closure.ps1
```

The runner uses Arc Testnet RPC `https://rpc.testnet.arc.network`, chain id
`5042002`, and the Arc USDC ERC20 interface
`0x3600000000000000000000000000000000000000` by default. It writes
`artifacts/arc-hackathon/demo-run/arc-testnet-preflight.json`,
`artifacts/arc-hackathon/demo-run/arc-testnet-wallet-preflight.json`,
`interfaces/ArcContracts/deployments/arcTestnet-5042002.json`, and
`artifacts/arc-hackathon/demo-run/arc-testnet-closure.json`, then reruns the
completion verifier with `-RequireArcTestnet`.

Polymarket execution evidence is Paper/demo evidence. The demo does not claim
production live trading, venue settlement on Arc, custody of user funds, or
guaranteed profit.

## Evidence Links And Transaction Hashes

Fresh hashes are generated by the runner and summarized in:

- `artifacts/arc-hackathon/demo-run/demo-summary.md`
- `artifacts/arc-hackathon/demo-run/completion-audit.json`
- `artifacts/arc-hackathon/demo-run/signal-publication.json`
- `artifacts/arc-hackathon/demo-run/subscription.json`
- `artifacts/arc-hackathon/demo-run/autotrade-permission.json`
- `artifacts/arc-hackathon/demo-run/builder-attribution.json`
- `artifacts/arc-hackathon/demo-run/performance-outcome.json`
- `artifacts/arc-hackathon/demo-run/revenue-settlement.json`
- `artifacts/arc-hackathon/screenshots/revenue-settlement.png`

## Next Steps

1. Deploy the Arc contracts to a stable Arc testnet environment.
2. Replace local fixture subscription funding with testnet USDC funding.
3. Add wallet-scoped revenue and performance filtering in the public portal.
4. Add a replay job for failed testnet writes from local intent records.
5. Expand subscriber product copy after legal/compliance review.
