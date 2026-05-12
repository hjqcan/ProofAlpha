const fs = require("fs");
const path = require("path");
const hre = require("hardhat");
const { recordSettlementFromRequest } = require("./lib/revenue-settlement.cjs");

async function deploy(name, args = []) {
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
      blockNumber: Number(receipt.blockNumber),
      constructorArgs: args.map((arg) => arg.toString())
    }
  };
}

async function main() {
  const [deployer, agentOwner, strategyAuthor, platform, subscriber] = await hre.ethers.getSigners();
  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);
  const signalId = process.env.ARC_REVENUE_SIGNAL_ID ||
    "0x7cc384e6393c4b85f9340bec439a81eca1d31778996494429c750946c7bb5cff";
  const sourceTransactionHash = process.env.ARC_REVENUE_SOURCE_TX_HASH ||
    "0xf5f60a2de3f184f38ed7b66b919d8f5056c0025f8a70ab9baad9d535b7b6c28d";
  const grossAmountMicroUsdc = process.env.ARC_REVENUE_GROSS_MICRO_USDC || "10000000";
  const grossUsdc = formatUsdc(grossAmountMicroUsdc);
  const shareBps = [7000, 2000, 1000];
  const recipients = [
    await agentOwner.getAddress(),
    await strategyAuthor.getAddress(),
    await platform.getAddress()
  ];
  const settlementId = process.env.ARC_REVENUE_SETTLEMENT_ID ||
    hre.ethers.id(`subscription-fee:${signalId}:${sourceTransactionHash}:${grossAmountMicroUsdc}`);

  const token = await deploy("TestUsdc");
  const settlement = await deploy("RevenueSettlement");
  const fundTx = await token.contract.mint(settlement.deployment.address, BigInt(grossAmountMicroUsdc));
  const fundReceipt = await fundTx.wait();
  const request = {
    chainId,
    revenueSettlement: settlement.deployment.address,
    settlementId,
    signalId,
    tokenAddress: token.deployment.address,
    grossAmountMicroUsdc,
    recipients,
    shareBps,
    distribute: true
  };
  const result = await recordSettlementFromRequest(hre, request);
  const exportedAtUtc = new Date().toISOString();
  const artifact = {
    documentVersion: "proofalpha-local-evm-revenue-settlement.v1",
    networkName: hre.network.name,
    chainId,
    deployer: await deployer.getAddress(),
    subscriber: await subscriber.getAddress(),
    sourceKind: "SubscriptionFee",
    simulated: false,
    localEvm: true,
    strategyId: "repricing_lag_arbitrage",
    signalId,
    sourceTransactionHash,
    eventName: "SettlementRecorded",
    settlementId,
    grossUsdc,
    grossAmountMicroUsdc,
    paymentToken: token.deployment.address,
    revenueSettlement: settlement.deployment.address,
    transactionHash: result.transactionHash,
    confirmed: result.confirmed,
    blockNumber: result.blockNumber,
    setupTransactions: {
      fundSettlementVault: {
        transactionHash: fundTx.hash,
        blockNumber: fundReceipt ? Number(fundReceipt.blockNumber) : null
      }
    },
    deployments: [
      token.deployment,
      settlement.deployment
    ],
    shares: recipients.map((recipient, index) => ({
      recipientKind: index === 0 ? "AgentOwner" : index === 1 ? "StrategyAuthor" : "Platform",
      walletAddress: recipient,
      shareBps: shareBps[index],
      amountMicroUsdc: ((BigInt(grossAmountMicroUsdc) * BigInt(shareBps[index])) / 10000n).toString()
    })),
    distribution: result.distribution,
    request,
    exportedAtUtc
  };
  const outputPath = process.env.ARC_REVENUE_DEMO_RESULT ||
    path.join(__dirname, "..", "..", "..", "artifacts", "arc-hackathon", "demo-run", "revenue-settlement.json");
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(artifact, null, 2)}\n`);
  console.log(`Wrote revenue settlement demo artifact: ${outputPath}`);
}

function formatUsdc(microUsdc) {
  const value = BigInt(microUsdc);
  const whole = value / 1000000n;
  const fractional = (value % 1000000n).toString().padStart(6, "0").replace(/0+$/, "");
  return fractional.length === 0 ? whole.toString() : `${whole}.${fractional}`;
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
