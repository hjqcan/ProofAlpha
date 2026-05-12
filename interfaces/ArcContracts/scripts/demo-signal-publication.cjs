const crypto = require("crypto");
const fs = require("fs");
const path = require("path");
const hre = require("hardhat");
const { publishSignalFromRequest } = require("./lib/signal-publication.cjs");

async function main() {
  const [deployer, agent] = await hre.ethers.getSigners();
  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);
  const registryFactory = await hre.ethers.getContractFactory("SignalRegistry");
  const registry = await registryFactory.deploy();
  await registry.waitForDeployment();

  const request = {
    chainId,
    signalRegistry: await registry.getAddress(),
    signalId: process.env.ARC_SIGNAL_ID || hash("proofalpha-demo-signal"),
    agentAddress: process.env.ARC_SIGNAL_AGENT_ADDRESS || await agent.getAddress(),
    venue: process.env.ARC_SIGNAL_VENUE || "polymarket",
    strategyKey: process.env.ARC_SIGNAL_STRATEGY_KEY || "repricing_lag_arbitrage",
    reasoningHash: process.env.ARC_SIGNAL_REASONING_HASH || hash("reasoning trace hash for demo signal"),
    riskEnvelopeHash: process.env.ARC_SIGNAL_RISK_ENVELOPE_HASH || hash("risk envelope hash for demo signal"),
    expectedEdgeBps: process.env.ARC_SIGNAL_EXPECTED_EDGE_BPS || "42",
    maxNotionalUsdcAtomic: process.env.ARC_SIGNAL_MAX_NOTIONAL_USDC_ATOMIC || "100000000",
    validUntilUnixSeconds: process.env.ARC_SIGNAL_VALID_UNTIL_UNIX_SECONDS || "1800000000"
  };

  const result = await publishSignalFromRequest(hre, request);
  const summary = await registry.getSignal(request.signalId);
  const artifact = {
    documentVersion: "proofalpha-local-evm-signal-publication.v1",
    networkName: hre.network.name,
    chainId,
    deployer: await deployer.getAddress(),
    signalRegistry: request.signalRegistry,
    signalId: request.signalId,
    agentAddress: request.agentAddress,
    transactionHash: result.transactionHash,
    confirmed: result.confirmed,
    blockNumber: result.blockNumber,
    eventName: "SignalPublished",
    request,
    onchainSummary: {
      agent: summary.agent,
      venue: summary.venue,
      strategyKey: summary.strategyKey,
      reasoningHash: summary.reasoningHash,
      riskEnvelopeHash: summary.riskEnvelopeHash,
      expectedEdgeBps: summary.expectedEdgeBps.toString(),
      maxNotionalUsdc: summary.maxNotionalUsdc.toString(),
      validUntil: summary.validUntil.toString(),
      publishedAt: summary.publishedAt.toString(),
      exists: summary.exists
    },
    exportedAtUtc: new Date().toISOString()
  };

  const defaultOutput = path.join(__dirname, "..", "deployments", "local-signal-publication.json");
  const outputPath = process.env.ARC_SIGNAL_DEMO_OUTPUT || defaultOutput;
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(artifact, null, 2)}\n`);
  console.log(`Wrote local EVM signal publication artifact: ${outputPath}`);
  console.log(`SignalPublished tx: ${result.transactionHash}`);
}

function hash(value) {
  return `0x${crypto.createHash("sha256").update(value).digest("hex")}`;
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
