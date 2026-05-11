# Arc Hackathon Source Notes And Assumptions

Retrieval date: 2026-05-11.

## Event Source

- Primary event page: `https://agora.thecanteenapp.com/`
- Event name used for positioning: Agora Agents Hackathon, Canteen x Circle.
- Event dates recorded on retrieval: May 11 to May 25, 2026.
- Settlement positioning recorded on retrieval: Arc and USDC.
- Judging lens recorded on retrieval: agency, traction, Circle tool usage, and
  innovation.

## Technical Sources

- Arc developer docs: `https://docs.arc.network/`
- Arc network overview:
  `https://docs.arc.network/arc-chain`
- Circle developer docs: `https://developers.circle.com/products`
- Circle Paymaster docs:
  `https://developers.circle.com/stablecoins/paymaster-overview`
- Polymarket builder overview:
  `https://docs.polymarket.com/builders/overview`
- Polymarket builder client methods:
  `https://docs.polymarket.com/trading/clients/builder`
- Polymarket CLOB overview:
  `https://docs.polymarket.com/trading/overview`

## Arc And USDC Assumptions

- Arc is the trust and settlement layer for ProofAlpha proof, access, outcome,
  and revenue records. It does not replace Polymarket as the execution venue.
- USDC is the unit for demo subscription pricing and settlement records.
- Demo amounts are fixture prices until real testnet USDC funding and Arc
  contract deployments are present.
- Chain writes must have durable local intent and result records so failed or
  duplicated writes can be retried idempotently by domain id.
- If Arc RPC, faucet, account funding, or explorer support is unavailable,
  ProofAlpha may use a labeled fixture path, but the demo must show the exact
  external failure and must not claim a real tx hash.

## Circle Tool Usage Assumptions

- Circle developer tooling can be used for wallet, USDC payment, app-kit, or
  paymaster flows where available to the team.
- Paymaster or App Kit integration is optional for Phase 0 and should not be
  mocked as complete. Later phases must label whether they use direct contract
  calls, Circle tooling, or fixtures.
- Circle API keys, wallet secrets, and private keys must stay in environment
  variables, user secrets, or ignored local files.

## Polymarket Builder Assumptions

- Polymarket remains the venue and CLOB/order-signing system.
- ProofAlpha can provide builder-attribution evidence by attaching builder
  metadata to the signed order path and later querying builder-attributed
  orders or trades when credentials and endpoints are available.
- Builder credentials are sensitive operational secrets and must never be
  exposed to browser state, API JSON, screenshots, docs, or logs.
- If the external builder-trades endpoint is unavailable, the demo can still
  include redacted signed-order evidence and a local correlation record.

## Open Questions To Resolve During Later Phases

- Which Arc chain id, RPC endpoint, explorer URL, and testnet USDC contract
  address are approved for the final demo environment?
- Which Circle product surface will be used in the final flow: direct contract
  calls only, Circle Wallets, App Kit, Paymaster, Gateway, or a combination?
- Is the event judging team expecting real Arc testnet tx hashes for every
  proof, or are local fixtures acceptable when network access is unavailable?
- Which wallet identity model is preferred for subscribers: raw EVM address,
  Circle wallet id, local operator alias, or all three?
- Which Polymarket builder account and builder code will be used for redacted
  attribution evidence?
- Are revenue split records expected to be onchain transfer events in the demo
  or is an auditable settlement journal sufficient?

## Phase 0 Backtest Note

Phase 0 changes only product contract documents and task-board status. It does
not change strategy behavior, order generation, execution routing, or risk
logic. The required regression evidence for this phase is a deterministic
strategy replay/backtest test plus build/test gates. Future implementation
phases that change strategy behavior or trading permissions must add or update
their own replay, Paper run, or backtest evidence before being marked done.

