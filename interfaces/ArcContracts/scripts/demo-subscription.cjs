const fs = require("fs");
const path = require("path");
const hre = require("hardhat");
const { subscribeFromRequest } = require("./lib/strategy-access-subscription.cjs");

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
      blockNumber: receipt ? Number(receipt.blockNumber) : null,
      constructorArgs: args.map((arg) => arg.toString())
    }
  };
}

async function main() {
  const [deployer, subscriber, treasury] = await hre.ethers.getSigners();
  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);
  const tokenDeployment = await deployContract("TestUsdc");
  const accessDeployment = await deployContract("StrategyAccess", [
    tokenDeployment.deployment.address,
    treasury.address
  ]);

  const request = {
    chainId,
    paymentToken: tokenDeployment.deployment.address,
    strategyAccess: accessDeployment.deployment.address,
    subscriberAddress: subscriber.address,
    treasuryAddress: treasury.address,
    planId: 1,
    strategyKey: "repricing_lag_arbitrage",
    tier: "SignalViewer",
    planName: "Signal Viewer",
    priceUsdc: "10.00",
    priceUsdcAtomic: hre.ethers.parseUnits("10", 6).toString(),
    durationSeconds: (7 * 24 * 60 * 60).toString(),
    permissions: ["ViewSignals", "ViewReasoning", "ExportSignal"],
    maxMarkets: 12,
    autoTradingAllowed: false,
    liveTradingAllowed: false
  };

  const result = await subscribeFromRequest(hre, request);
  const artifact = {
    documentVersion: "proofalpha-local-evm-subscription.v1",
    networkName: hre.network.name,
    chainId,
    deployer: deployer.address,
    subscriber: subscriber.address,
    treasury: treasury.address,
    paymentToken: tokenDeployment.deployment.address,
    strategyAccess: accessDeployment.deployment.address,
    deployments: [tokenDeployment.deployment, accessDeployment.deployment],
    request,
    eventName: "StrategySubscribed",
    transactionHash: result.subscriptionTransactionHash,
    confirmed: result.confirmed,
    blockNumber: result.subscriptionBlockNumber,
    strategyKeyBytes32: result.strategyKeyBytes32,
    expiresAtUnixSeconds: result.event.expiresAt,
    expiresAtUtc: unixSecondsToUtc(result.event.expiresAt),
    setupTransactions: {
      mint: {
        transactionHash: result.mintTransactionHash,
        blockNumber: result.mintBlockNumber
      },
      planConfigured: {
        transactionHash: result.planTransactionHash,
        blockNumber: result.planBlockNumber
      },
      approval: {
        transactionHash: result.approvalTransactionHash,
        blockNumber: result.approvalBlockNumber
      }
    },
    event: result.event,
    onchainSummary: result.onchainSummary,
    exportedAtUtc: new Date().toISOString()
  };

  const defaultOutput = path.join(__dirname, "..", "deployments", "local-subscription.json");
  const outputPath = process.env.ARC_SUBSCRIPTION_DEMO_OUTPUT || defaultOutput;
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(artifact, null, 2)}\n`);
  console.log(`Wrote local EVM subscription artifact: ${outputPath}`);
  console.log(`StrategySubscribed tx: ${result.subscriptionTransactionHash}`);
}

function unixSecondsToUtc(value) {
  return new Date(Number(value) * 1000).toISOString();
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
