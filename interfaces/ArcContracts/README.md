ProofAlpha Arc Contracts
========================

This package contains the Arc-facing contract foundation for the paid agent
hackathon path. The contracts are intentionally small and auditable:

- `SignalRegistry` anchors signal metadata and proof hashes.
- `StrategyAccess` accepts an ERC20 payment token and records subscription
  entitlement windows.
- `PerformanceLedger` records terminal signal outcomes and explicit
  corrections.
- `RevenueSettlement` records settlement journals and can distribute held ERC20
  subscription revenue to agent owner, strategy author, and platform recipients.
- `TestUsdc` is a local test fixture only; it is not production USDC.

Commands:

```powershell
npm install
npm run build
npm test
npm run deploy:local
npm run export-abi
```

Arc testnet deployment requires environment variables:

```powershell
$env:ARC_TESTNET_RPC_URL="https://rpc.testnet.arc.network"
$env:ARC_TESTNET_CHAIN_ID="5042002"
$env:ARC_SETTLEMENT_USDC_ADDRESS="0x3600000000000000000000000000000000000000"
$env:ARC_SETTLEMENT_PRIVATE_KEY="..."
$env:ARC_SETTLEMENT_TREASURY="0x..."
npm run testnet:preflight
npm run testnet:wallet-preflight
npm run deploy:arc-testnet
npm run testnet:closed-loop
```

Never commit private keys, RPC credentials, or wallet secrets. Deployment
artifacts must contain only public addresses, transaction ids, ABI hashes, and
constructor arguments.
