const fs = require("fs");
const path = require("path");
const hre = require("hardhat");
const { recordOutcomeFromRequest } = require("./lib/performance-ledger.cjs");

async function main() {
  const artifactRoot = process.env.ARC_PERFORMANCE_DEMO_ROOT
    ? path.resolve(process.env.ARC_PERFORMANCE_DEMO_ROOT)
    : path.resolve(__dirname, "../../..", "artifacts", "arc-hackathon", "demo-run");
  fs.mkdirSync(artifactRoot, { recursive: true });

  const signalPublication = readJsonIfExists(path.join(artifactRoot, "signal-publication.json"));
  const signalId = process.env.ARC_PERFORMANCE_SIGNAL_ID ||
    signalPublication?.signalId ||
    hre.ethers.id("proofalpha-phase7-signal");
  const sourceSignalTransactionHash = process.env.ARC_PERFORMANCE_SIGNAL_TX_HASH ||
    signalPublication?.transactionHash ||
    null;
  const strategyId = process.env.ARC_PERFORMANCE_STRATEGY_ID ||
    signalPublication?.strategyId ||
    "repricing_lag_arbitrage";
  const exportedAtUtc = new Date().toISOString();
  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);

  const factory = await hre.ethers.getContractFactory("PerformanceLedger");
  const ledger = await factory.deploy();
  await ledger.waitForDeployment();
  const performanceLedger = await ledger.getAddress();

  const outcomeHash = process.env.ARC_PERFORMANCE_OUTCOME_ID ||
    hre.ethers.id("proofalpha-phase7-outcome-loss-risk-adjusted");
  const request = {
    chainId,
    performanceLedger,
    signalId,
    status: 1,
    realizedPnlBps: "-12",
    slippageBps: "3",
    outcomeHash
  };
  const result = await recordOutcomeFromRequest(hre, request);
  const summary = await ledger.getOutcome(signalId);

  const performanceOutcome = {
    documentVersion: "proofalpha-arc-performance-outcome.v1",
    networkName: hre.network.name,
    chainId,
    performanceLedger,
    sourceSignalId: signalId,
    sourceSignalTransactionHash,
    eventName: "OutcomeRecorded",
    outcomeId: outcomeHash,
    signalId,
    executionId: "paper-order-phase7-loss-0001",
    strategyId,
    marketId: "demo-polymarket-market",
    status: "ExecutedLoss",
    realizedPnlBps: -12,
    slippageBps: 3,
    fillRate: 1,
    reasonCode: "FILLED_WITH_NEGATIVE_PNL",
    outcomeHash,
    transactionHash: result.transactionHash,
    confirmed: result.confirmed,
    blockNumber: result.blockNumber,
    createdAtUtc: "2026-05-12T09:59:00Z",
    recordedAtUtc: exportedAtUtc,
    onchainSummary: {
      status: Number(summary.status),
      realizedPnlBps: summary.realizedPnlBps.toString(),
      slippageBps: summary.slippageBps.toString(),
      outcomeHash: summary.outcomeHash,
      recorder: summary.recorder,
      recordedAtUnixSeconds: summary.recordedAt.toString(),
      exists: summary.exists
    },
    exportedAtUtc
  };

  const agentReputation = {
    documentVersion: "proofalpha-arc-agent-reputation.v1",
    scope: "agent",
    strategyId: null,
    calculatedAtUtc: exportedAtUtc,
    totalSignals: 5,
    terminalSignals: 4,
    pendingSignals: 1,
    executedSignals: 2,
    expiredSignals: 1,
    rejectedSignals: 1,
    skippedSignals: 0,
    failedSignals: 0,
    cancelledSignals: 0,
    winCount: 1,
    lossCount: 1,
    flatCount: 0,
    averageRealizedPnlBps: -3,
    averageSlippageBps: 2,
    riskRejectionRate: 0.25,
    confidenceCoverage: 0.8,
    outcomeBreakdown: {
      executedWin: 1,
      executedLoss: 1,
      executedFlat: 0,
      rejectedRisk: 1,
      rejectedCompliance: 0,
      expired: 1,
      skippedNoAccess: 0,
      failedExecution: 0,
      cancelledOperator: 0,
      pending: 1
    },
    evidence: {
      signalTransactionHash: sourceSignalTransactionHash,
      outcomeTransactionHash: result.transactionHash,
      performanceOutcomeArtifact: "artifacts/arc-hackathon/demo-run/performance-outcome.json"
    },
    caveat: "Historical terminal outcomes only; future profit is not implied."
  };

  const performanceOutcomePath = path.join(artifactRoot, "performance-outcome.json");
  const agentReputationPath = path.join(artifactRoot, "agent-reputation.json");
  fs.writeFileSync(performanceOutcomePath, `${JSON.stringify(performanceOutcome, null, 2)}\n`);
  fs.writeFileSync(agentReputationPath, `${JSON.stringify(agentReputation, null, 2)}\n`);

  console.log(JSON.stringify({
    performanceOutcomePath,
    agentReputationPath,
    outcomeTransactionHash: result.transactionHash,
    blockNumber: result.blockNumber,
    performanceOutcome,
    agentReputation
  }, null, 2));
}

function readJsonIfExists(filePath) {
  if (!fs.existsSync(filePath)) {
    return null;
  }

  return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
