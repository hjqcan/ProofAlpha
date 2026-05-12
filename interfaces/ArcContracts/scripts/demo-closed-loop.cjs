const fs = require("fs");
const path = require("path");
const hre = require("hardhat");

const DEFAULT_ARTIFACT_ROOT = path.resolve(__dirname, "../../..", "artifacts", "arc-hackathon", "demo-run");
const PLAN_ID = 2;
const STRATEGY_ID = "repricing_lag_arbitrage";
const PRICE_USDC_ATOMIC = 25_000_000n;
const DURATION_SECONDS = 7n * 24n * 60n * 60n;
const SIGNAL_ID = "0x7cc384e6393c4b85f9340bec439a81eca1d31778996494429c750946c7bb5cff";
const REASONING_HASH = "0x664020618931484b30288d9976c61e9ebf16d6ec304290c920bc891b24c6264a";
const RISK_ENVELOPE_HASH = "0x9c7b7b3ccfb4e55ab07a273b974365eb8fcee2659f80f0c9b3b27a2fde65987c";
const OUTCOME_HASH = "0xa4c4224b6ab4bd1208af8f010cebbe899436035fb59e50f8fa636002941069a3";

async function main() {
  if (hre.network.name !== "hardhat") {
    throw new Error("demo-closed-loop must run with --network hardhat.");
  }

  const artifactRoot = process.env.ARC_HACKATHON_ARTIFACT_ROOT
    ? path.resolve(process.env.ARC_HACKATHON_ARTIFACT_ROOT)
    : DEFAULT_ARTIFACT_ROOT;
  fs.mkdirSync(artifactRoot, { recursive: true });

  const [deployer, subscriber, agentOwner, strategyAuthor, platform] = await hre.ethers.getSigners();
  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);

  const testUsdc = await deployContract("TestUsdc");
  const revenueSettlement = await deployContract("RevenueSettlement");
  const signalRegistry = await deployContract("SignalRegistry");
  const strategyAccess = await deployContract("StrategyAccess", [
    testUsdc.deployment.address,
    revenueSettlement.deployment.address
  ]);
  const performanceLedger = await deployContract("PerformanceLedger");

  const strategyKeyBytes32 = hre.ethers.id(STRATEGY_ID);
  const mintTx = await testUsdc.contract.mint(subscriber.address, PRICE_USDC_ATOMIC);
  const mintReceipt = await mintTx.wait();
  const subscriberBefore = await testUsdc.contract.balanceOf(subscriber.address);
  const vaultBefore = await testUsdc.contract.balanceOf(revenueSettlement.deployment.address);

  const setPlanTx = await strategyAccess.contract.setPlan(
    BigInt(PLAN_ID),
    strategyKeyBytes32,
    PRICE_USDC_ATOMIC,
    DURATION_SECONDS,
    true
  );
  const setPlanReceipt = await setPlanTx.wait();

  const approveTx = await testUsdc.contract.connect(subscriber).approve(
    strategyAccess.deployment.address,
    PRICE_USDC_ATOMIC
  );
  const approveReceipt = await approveTx.wait();

  const subscribeTx = await strategyAccess.contract.connect(subscriber).subscribe(strategyKeyBytes32, BigInt(PLAN_ID));
  const subscribeReceipt = await subscribeTx.wait();
  const subscribeEvent = parseEvent(strategyAccess.contract, subscribeReceipt, "StrategySubscribed");
  if (!subscribeEvent) {
    throw new Error("StrategySubscribed event was not emitted in local closed loop.");
  }
  const subscriberAfterSubscription = await testUsdc.contract.balanceOf(subscriber.address);
  const vaultAfterSubscription = await testUsdc.contract.balanceOf(revenueSettlement.deployment.address);

  const signalTx = await signalRegistry.contract.publishSignal(
    SIGNAL_ID,
    subscriber.address,
    "polymarket",
    STRATEGY_ID,
    REASONING_HASH,
    RISK_ENVELOPE_HASH,
    42,
    100_000_000,
    1800000000
  );
  const signalReceipt = await signalTx.wait();

  const outcomeTx = await performanceLedger.contract.recordOutcome(
    SIGNAL_ID,
    1,
    -12,
    3,
    OUTCOME_HASH
  );
  const outcomeReceipt = await outcomeTx.wait();

  const recipients = [agentOwner.address, strategyAuthor.address, platform.address];
  const shareBps = [7000, 2000, 1000];
  const recipientBalancesBefore = await Promise.all(
    recipients.map((recipient) => testUsdc.contract.balanceOf(recipient))
  );
  const settlementId = hre.ethers.id(`proofalpha-local-closed-loop-settlement-${SIGNAL_ID}-${subscribeTx.hash}`);
  const settlementTx = await revenueSettlement.contract.recordAndDistributeSettlement(
    settlementId,
    SIGNAL_ID,
    testUsdc.deployment.address,
    PRICE_USDC_ATOMIC,
    recipients,
    shareBps
  );
  const settlementReceipt = await settlementTx.wait();
  const recipientBalancesAfter = await Promise.all(
    recipients.map((recipient) => testUsdc.contract.balanceOf(recipient))
  );
  const vaultAfterDistribution = await testUsdc.contract.balanceOf(revenueSettlement.deployment.address);
  const hasAccess = await strategyAccess.contract.hasAccess(subscriber.address, strategyKeyBytes32);

  const artifact = {
    documentVersion: "proofalpha-local-evm-closed-loop.v1",
    networkName: hre.network.name,
    chainId,
    deployer: deployer.address,
    subscriber: subscriber.address,
    contracts: {
      TestUsdc: testUsdc.deployment.address,
      RevenueSettlement: revenueSettlement.deployment.address,
      SignalRegistry: signalRegistry.deployment.address,
      StrategyAccess: strategyAccess.deployment.address,
      PerformanceLedger: performanceLedger.deployment.address
    },
    deployments: [
      testUsdc.deployment,
      revenueSettlement.deployment,
      signalRegistry.deployment,
      strategyAccess.deployment,
      performanceLedger.deployment
    ],
    plan: {
      planId: PLAN_ID,
      strategyId: STRATEGY_ID,
      tier: "PaperAutotrade",
      priceUsdcAtomic: PRICE_USDC_ATOMIC.toString(),
      durationSeconds: DURATION_SECONDS.toString(),
      permissions: ["ViewSignals", "ViewReasoning", "ExportSignal", "RequestPaperAutoTrade"],
      setPlanTransactionHash: setPlanTx.hash,
      setPlanBlockNumber: blockNumber(setPlanReceipt)
    },
    setupTransactions: {
      mint: {
        transactionHash: mintTx.hash,
        blockNumber: blockNumber(mintReceipt)
      },
      approval: {
        transactionHash: approveTx.hash,
        blockNumber: blockNumber(approveReceipt)
      }
    },
    subscription: {
      transactionHash: subscribeTx.hash,
      blockNumber: blockNumber(subscribeReceipt),
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
      blockNumber: blockNumber(signalReceipt),
      reasoningHash: REASONING_HASH,
      riskEnvelopeHash: RISK_ENVELOPE_HASH
    },
    performanceOutcome: {
      signalId: SIGNAL_ID,
      strategyId: STRATEGY_ID,
      status: "ExecutedLoss",
      transactionHash: outcomeTx.hash,
      blockNumber: blockNumber(outcomeReceipt)
    },
    revenueSettlement: {
      settlementId,
      signalId: SIGNAL_ID,
      strategyId: STRATEGY_ID,
      grossAmountMicroUsdc: PRICE_USDC_ATOMIC.toString(),
      sourceTransactionHash: subscribeTx.hash,
      transactionHash: settlementTx.hash,
      blockNumber: blockNumber(settlementReceipt),
      distributed: true,
      recipients,
      shareBps,
      recipientDeltas: recipients.map((recipient, index) => ({
        recipient,
        shareBps: shareBps[index],
        beforeAtomic: recipientBalancesBefore[index].toString(),
        afterAtomic: recipientBalancesAfter[index].toString(),
        deltaAtomic: (recipientBalancesAfter[index] - recipientBalancesBefore[index]).toString()
      }))
    },
    balances: {
      subscriberBeforeAtomic: subscriberBefore.toString(),
      subscriberAfterSubscriptionAtomic: subscriberAfterSubscription.toString(),
      subscriberSubscriptionDeltaAtomic: (subscriberAfterSubscription - subscriberBefore).toString(),
      settlementVaultBeforeAtomic: vaultBefore.toString(),
      settlementVaultAfterSubscriptionAtomic: vaultAfterSubscription.toString(),
      settlementVaultSubscriptionDeltaAtomic: (vaultAfterSubscription - vaultBefore).toString(),
      settlementVaultAfterDistributionAtomic: vaultAfterDistribution.toString(),
      settlementVaultDistributionDeltaAtomic: (vaultAfterDistribution - vaultAfterSubscription).toString()
    },
    exportedAtUtc: new Date().toISOString()
  };

  const outputPath = process.env.ARC_LOCAL_CLOSED_LOOP_OUTPUT
    ? path.resolve(process.env.ARC_LOCAL_CLOSED_LOOP_OUTPUT)
    : path.join(artifactRoot, "local-evm-closed-loop.json");
  fs.writeFileSync(outputPath, `${JSON.stringify(artifact, null, 2)}\n`);
  console.log(`Wrote local EVM closed-loop artifact: ${outputPath}`);
}

async function deployContract(name, args = []) {
  const factory = await hre.ethers.getContractFactory(name);
  const contract = await factory.deploy(...args);
  await contract.waitForDeployment();
  const tx = contract.deploymentTransaction();
  const receipt = await tx.wait();

  return {
    contract,
    deployment: {
      contractName: name,
      address: await contract.getAddress(),
      txHash: tx.hash,
      blockNumber: blockNumber(receipt),
      constructorArgs: args.map((arg) => arg.toString())
    }
  };
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

function blockNumber(receipt) {
  return receipt ? Number(receipt.blockNumber) : null;
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
