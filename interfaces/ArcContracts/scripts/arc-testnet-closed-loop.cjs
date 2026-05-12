const fs = require("fs");
const path = require("path");
const hre = require("hardhat");

const ARC_TESTNET_CHAIN_ID = 5042002;
const ARC_TESTNET_USDC = "0x3600000000000000000000000000000000000000";
const DEFAULT_ARTIFACT_ROOT = path.resolve(__dirname, "../../..", "artifacts", "arc-hackathon", "demo-run");
const PLAN_ID = 2;
const STRATEGY_ID = "repricing_lag_arbitrage";
const PRICE_USDC_ATOMIC = 25_000_000n;
const DURATION_SECONDS = 7n * 24n * 60n * 60n;
const SIGNAL_ID = "0x7cc384e6393c4b85f9340bec439a81eca1d31778996494429c750946c7bb5cff";
const REASONING_HASH = "0x664020618931484b30288d9976c61e9ebf16d6ec304290c920bc891b24c6264a";
const RISK_ENVELOPE_HASH = "0x9c7b7b3ccfb4e55ab07a273b974365eb8fcee2659f80f0c9b3b27a2fde65987c";
const OUTCOME_HASH = "0xa4c4224b6ab4bd1208af8f010cebbe899436035fb59e50f8fa636002941069a3";

const erc20Abi = [
  "function decimals() view returns (uint8)",
  "function balanceOf(address account) view returns (uint256)",
  "function allowance(address owner, address spender) view returns (uint256)",
  "function approve(address spender, uint256 amount) returns (bool)"
];

async function main() {
  if (hre.network.name !== "arcTestnet") {
    throw new Error("arc-testnet-closed-loop must run with --network arcTestnet.");
  }

  const artifactRoot = process.env.ARC_HACKATHON_ARTIFACT_ROOT
    ? path.resolve(process.env.ARC_HACKATHON_ARTIFACT_ROOT)
    : DEFAULT_ARTIFACT_ROOT;
  fs.mkdirSync(artifactRoot, { recursive: true });

  const [subscriber] = await hre.ethers.getSigners();
  const subscriberAddress = await subscriber.getAddress();
  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);
  if (chainId !== ARC_TESTNET_CHAIN_ID) {
    throw new Error(`Connected chain ${chainId} is not Arc Testnet ${ARC_TESTNET_CHAIN_ID}.`);
  }

  const deployment = readDeployment(chainId);
  const addresses = deployment.contracts.reduce((acc, item) => {
    acc[item.contractName] = item.address;
    return acc;
  }, {});
  for (const required of ["SignalRegistry", "StrategyAccess", "PerformanceLedger", "RevenueSettlement"]) {
    if (!addresses[required]) {
      throw new Error(`Deployment artifact is missing ${required}.`);
    }
  }

  const paymentToken = (deployment.paymentToken || process.env.ARC_SETTLEMENT_USDC_ADDRESS || ARC_TESTNET_USDC).toLowerCase();
  if (paymentToken !== ARC_TESTNET_USDC.toLowerCase()) {
    throw new Error(`Arc Testnet closure requires the official USDC ERC20 interface ${ARC_TESTNET_USDC}, got ${paymentToken}.`);
  }

  const treasury = requireAddress(process.env.ARC_SETTLEMENT_TREASURY, "ARC_SETTLEMENT_TREASURY");
  if (treasury.toLowerCase() === subscriberAddress.toLowerCase()) {
    throw new Error("ARC_SETTLEMENT_TREASURY must be different from the subscriber/deployer address for payment evidence.");
  }

  const usdc = new hre.ethers.Contract(paymentToken, erc20Abi, subscriber);
  const decimals = Number(await usdc.decimals());
  if (decimals !== 6) {
    throw new Error(`Expected Arc USDC ERC20 decimals to be 6, got ${decimals}.`);
  }

  const subscriberBalanceBefore = await usdc.balanceOf(subscriberAddress);
  const settlementVaultBalanceBefore = await usdc.balanceOf(addresses.RevenueSettlement);
  if (subscriberBalanceBefore < PRICE_USDC_ATOMIC) {
    throw new Error(`Subscriber ${subscriberAddress} needs at least 25 Arc Testnet USDC. Current ERC20 balance: ${subscriberBalanceBefore.toString()}.`);
  }

  const signalRegistry = await hre.ethers.getContractAt("SignalRegistry", addresses.SignalRegistry, subscriber);
  const strategyAccess = await hre.ethers.getContractAt("StrategyAccess", addresses.StrategyAccess, subscriber);
  const performanceLedger = await hre.ethers.getContractAt("PerformanceLedger", addresses.PerformanceLedger, subscriber);
  const revenueSettlement = await hre.ethers.getContractAt("RevenueSettlement", addresses.RevenueSettlement, subscriber);
  const strategyAccessTreasury = await strategyAccess.treasury();
  if (strategyAccessTreasury.toLowerCase() !== addresses.RevenueSettlement.toLowerCase()) {
    throw new Error(`StrategyAccess treasury must be RevenueSettlement settlement vault. got=${strategyAccessTreasury}; expected=${addresses.RevenueSettlement}`);
  }

  const strategyKeyBytes32 = hre.ethers.id(STRATEGY_ID);
  const setPlanTx = await strategyAccess.setPlan(
    BigInt(PLAN_ID),
    strategyKeyBytes32,
    PRICE_USDC_ATOMIC,
    DURATION_SECONDS,
    true
  );
  const setPlanReceipt = await setPlanTx.wait(1);

  const approveTx = await usdc.approve(addresses.StrategyAccess, PRICE_USDC_ATOMIC);
  const approveReceipt = await approveTx.wait(1);

  const subscribeTx = await strategyAccess.subscribe(strategyKeyBytes32, BigInt(PLAN_ID));
  const subscribeReceipt = await subscribeTx.wait(1);
  const subscribeEvent = parseEvent(strategyAccess, subscribeReceipt, "StrategySubscribed");
  if (!subscribeEvent) {
    throw new Error("StrategySubscribed event was not emitted on Arc Testnet.");
  }
  const subscriberBalanceAfterSubscription = await usdc.balanceOf(subscriberAddress);
  const settlementVaultAfterSubscription = await usdc.balanceOf(addresses.RevenueSettlement);
  const settlementVaultSubscriptionDelta = settlementVaultAfterSubscription - settlementVaultBalanceBefore;
  if (settlementVaultSubscriptionDelta !== PRICE_USDC_ATOMIC) {
    throw new Error(`Expected subscription payment to add 25 USDC to the RevenueSettlement vault. delta=${settlementVaultSubscriptionDelta.toString()}.`);
  }

  const signalTx = await signalRegistry.publishSignal(
    SIGNAL_ID,
    subscriberAddress,
    "polymarket",
    STRATEGY_ID,
    REASONING_HASH,
    RISK_ENVELOPE_HASH,
    42,
    100_000_000,
    1800000000
  );
  const signalReceipt = await signalTx.wait(1);

  const outcomeTx = await performanceLedger.recordOutcome(
    SIGNAL_ID,
    1,
    -12,
    3,
    OUTCOME_HASH
  );
  const outcomeReceipt = await outcomeTx.wait(1);

  const recipients = resolveRevenueRecipients(subscriberAddress, treasury);
  const shareBps = [7000, 2000, 1000];
  const recipientBalancesBefore = await Promise.all(recipients.map((recipient) => usdc.balanceOf(recipient)));
  const settlementId = hre.ethers.id(`proofalpha-arc-testnet-settlement-${SIGNAL_ID}-${subscribeTx.hash}`);
  const settlementTx = await revenueSettlement.recordAndDistributeSettlement(
    settlementId,
    SIGNAL_ID,
    paymentToken,
    PRICE_USDC_ATOMIC,
    recipients,
    shareBps
  );
  const settlementReceipt = await settlementTx.wait(1);

  const subscriberBalanceAfter = await usdc.balanceOf(subscriberAddress);
  const treasuryBalanceAfter = await usdc.balanceOf(treasury);
  const settlementVaultAfterDistribution = await usdc.balanceOf(addresses.RevenueSettlement);
  const recipientBalancesAfter = await Promise.all(recipients.map((recipient) => usdc.balanceOf(recipient)));
  const accessExpiresAt = await strategyAccess.accessExpiresAt(subscriberAddress, strategyKeyBytes32);
  const hasAccess = await strategyAccess.hasAccess(subscriberAddress, strategyKeyBytes32);
  const outcomeSummary = await performanceLedger.getOutcome(SIGNAL_ID);

  const closure = {
    documentVersion: "proofalpha-arc-testnet-closure.v1",
    networkName: hre.network.name,
    chainId,
    explorerBaseUrl: "https://testnet.arcscan.app",
    subscriber: subscriberAddress,
    treasury,
    paymentToken,
    deploymentArtifact: path.relative(path.resolve(__dirname, ".."), deployment.path).replace(/\\/g, "/"),
    contracts: addresses,
    plan: {
      planId: PLAN_ID,
      strategyId: STRATEGY_ID,
      tier: "PaperAutotrade",
      priceUsdc: "25.00",
      priceUsdcAtomic: PRICE_USDC_ATOMIC.toString(),
      durationSeconds: DURATION_SECONDS.toString(),
      permissions: ["ViewSignals", "ViewReasoning", "ExportSignal", "RequestPaperAutoTrade"],
      autoTradingAllowed: true,
      liveTradingAllowed: false,
      setPlanTransactionHash: setPlanTx.hash,
      setPlanBlockNumber: Number(setPlanReceipt.blockNumber)
    },
    approval: {
      transactionHash: approveTx.hash,
      blockNumber: Number(approveReceipt.blockNumber),
      allowanceAtomic: (await usdc.allowance(subscriberAddress, addresses.StrategyAccess)).toString()
    },
    subscription: {
      transactionHash: subscribeTx.hash,
      blockNumber: Number(subscribeReceipt.blockNumber),
      user: subscribeEvent.args.user,
      strategyKey: subscribeEvent.args.strategyKey,
      planId: subscribeEvent.args.planId.toString(),
      amountAtomic: subscribeEvent.args.amount.toString(),
      expiresAtUnixSeconds: subscribeEvent.args.expiresAt.toString(),
      hasAccess
    },
    signalPublication: {
      signalId: SIGNAL_ID,
      strategyId: STRATEGY_ID,
      transactionHash: signalTx.hash,
      blockNumber: Number(signalReceipt.blockNumber),
      reasoningHash: REASONING_HASH,
      riskEnvelopeHash: RISK_ENVELOPE_HASH
    },
    performanceOutcome: {
      signalId: SIGNAL_ID,
      strategyId: STRATEGY_ID,
      status: "ExecutedLoss",
      realizedPnlBps: -12,
      slippageBps: 3,
      outcomeHash: OUTCOME_HASH,
      transactionHash: outcomeTx.hash,
      blockNumber: Number(outcomeReceipt.blockNumber),
      onchainSummary: {
        status: Number(outcomeSummary.status),
        realizedPnlBps: outcomeSummary.realizedPnlBps.toString(),
        slippageBps: outcomeSummary.slippageBps.toString(),
        exists: outcomeSummary.exists
      }
    },
    revenueSettlement: {
      settlementId,
      signalId: SIGNAL_ID,
      strategyId: STRATEGY_ID,
      grossUsdc: "25",
      grossAmountMicroUsdc: PRICE_USDC_ATOMIC.toString(),
      sourceTransactionHash: subscribeTx.hash,
      transactionHash: settlementTx.hash,
      blockNumber: Number(settlementReceipt.blockNumber),
      recipients,
      shareBps,
      distributed: true,
      recipientDeltas: recipients.map((recipient, index) => ({
        recipient,
        shareBps: shareBps[index],
        beforeAtomic: recipientBalancesBefore[index].toString(),
        afterAtomic: recipientBalancesAfter[index].toString(),
        deltaAtomic: (recipientBalancesAfter[index] - recipientBalancesBefore[index]).toString()
      }))
    },
    balances: {
      subscriberBeforeAtomic: subscriberBalanceBefore.toString(),
      subscriberAfterSubscriptionAtomic: subscriberBalanceAfterSubscription.toString(),
      subscriberSubscriptionDeltaAtomic: (subscriberBalanceAfterSubscription - subscriberBalanceBefore).toString(),
      subscriberAfterAtomic: subscriberBalanceAfter.toString(),
      subscriberDeltaAtomic: (subscriberBalanceAfter - subscriberBalanceBefore).toString(),
      settlementVaultBeforeAtomic: settlementVaultBalanceBefore.toString(),
      settlementVaultAfterSubscriptionAtomic: settlementVaultAfterSubscription.toString(),
      settlementVaultSubscriptionDeltaAtomic: settlementVaultSubscriptionDelta.toString(),
      settlementVaultAfterDistributionAtomic: settlementVaultAfterDistribution.toString(),
      settlementVaultDistributionDeltaAtomic: (settlementVaultAfterDistribution - settlementVaultAfterSubscription).toString(),
      treasuryRecipientBeforeAtomic: recipientBalancesBefore[0].toString(),
      treasuryRecipientAfterAtomic: treasuryBalanceAfter.toString(),
      treasuryRecipientDeltaAtomic: (treasuryBalanceAfter - recipientBalancesBefore[0]).toString(),
      treasuryAfterAtomic: treasuryBalanceAfter.toString(),
      treasuryDeltaAtomic: (treasuryBalanceAfter - recipientBalancesBefore[0]).toString()
    },
    exportedAtUtc: new Date().toISOString()
  };

  const outputPath = process.env.ARC_TESTNET_CLOSURE_OUTPUT
    ? path.resolve(process.env.ARC_TESTNET_CLOSURE_OUTPUT)
    : path.join(artifactRoot, "arc-testnet-closure.json");
  fs.writeFileSync(outputPath, `${JSON.stringify(closure, null, 2)}\n`);
  console.log(`Wrote Arc Testnet closure artifact: ${outputPath}`);
  console.log(`Subscription tx: ${subscribeTx.hash}`);
  console.log(`Signal tx: ${signalTx.hash}`);
  console.log(`Outcome tx: ${outcomeTx.hash}`);
  console.log(`Settlement tx: ${settlementTx.hash}`);
}

function readDeployment(chainId) {
  const explicitPath = process.env.ARC_TESTNET_DEPLOYMENT_PATH
    ? path.resolve(process.env.ARC_TESTNET_DEPLOYMENT_PATH)
    : path.resolve(__dirname, "..", "deployments", `arcTestnet-${chainId}.json`);
  if (!fs.existsSync(explicitPath)) {
    throw new Error(`Arc Testnet deployment artifact not found: ${explicitPath}`);
  }

  return {
    ...JSON.parse(fs.readFileSync(explicitPath, "utf8")),
    path: explicitPath
  };
}

function requireAddress(value, name) {
  if (!value || !hre.ethers.isAddress(value)) {
    throw new Error(`${name} must be a valid EVM address.`);
  }

  return hre.ethers.getAddress(value);
}

function resolveRevenueRecipients(subscriberAddress, treasury) {
  const strategyAuthor = process.env.ARC_SETTLEMENT_STRATEGY_AUTHOR || subscriberAddress;
  const platform = process.env.ARC_SETTLEMENT_PLATFORM || subscriberAddress;
  return [
    requireAddress(treasury, "ARC_SETTLEMENT_TREASURY"),
    requireAddress(strategyAuthor, "ARC_SETTLEMENT_STRATEGY_AUTHOR"),
    requireAddress(platform, "ARC_SETTLEMENT_PLATFORM")
  ];
}

function parseEvent(contract, receipt, eventName) {
  for (const log of receipt?.logs || []) {
    try {
      const parsed = contract.interface.parseLog(log);
      if (parsed?.name === eventName) {
        return parsed;
      }
    } catch {
      // Ignore logs from other contracts.
    }
  }

  return null;
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
