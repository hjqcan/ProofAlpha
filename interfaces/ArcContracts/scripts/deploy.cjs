const crypto = require("crypto");
const fs = require("fs");
const path = require("path");
const hre = require("hardhat");

const CONTRACTS = [
  "SignalRegistry",
  "StrategyAccess",
  "PerformanceLedger",
  "RevenueSettlement"
];

async function deployContract(name, args = []) {
  const factory = await hre.ethers.getContractFactory(name);
  const contract = await factory.deploy(...args);
  await contract.waitForDeployment();
  const tx = contract.deploymentTransaction();
  const receipt = await tx.wait();
  const artifact = await hre.artifacts.readArtifact(name);

  return {
    contract,
    deployment: {
      contractName: name,
      address: await contract.getAddress(),
      txHash: tx.hash,
      blockNumber: receipt.blockNumber,
      constructorArgs: args.map((arg) => arg.toString()),
      abiHash: sha256(JSON.stringify(artifact.abi))
    }
  };
}

async function main() {
  const [deployer] = await hre.ethers.getSigners();
  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);
  const deployerAddress = await deployer.getAddress();
  const settlementTreasuryRecipient = process.env.ARC_SETTLEMENT_TREASURY || deployerAddress;

  let paymentTokenAddress = process.env.ARC_SETTLEMENT_USDC_ADDRESS;
  const deployments = [];

  if (!paymentTokenAddress) {
    if (hre.network.name !== "hardhat" && hre.network.name !== "localhost") {
      throw new Error("ARC_SETTLEMENT_USDC_ADDRESS is required outside local networks.");
    }

    const testToken = await deployContract("TestUsdc");
    paymentTokenAddress = testToken.deployment.address;
    deployments.push(testToken.deployment);
  }

  const revenueSettlement = await deployContract("RevenueSettlement");
  const signalRegistry = await deployContract("SignalRegistry");
  const strategyAccessTreasury = revenueSettlement.deployment.address;
  const strategyAccess = await deployContract("StrategyAccess", [paymentTokenAddress, strategyAccessTreasury]);
  const performanceLedger = await deployContract("PerformanceLedger");

  deployments.push(
    revenueSettlement.deployment,
    signalRegistry.deployment,
    strategyAccess.deployment,
    performanceLedger.deployment
  );

  const exportedAt = new Date().toISOString();
  const deploymentArtifact = {
    chainId,
    networkName: hre.network.name,
    deployer: deployerAddress,
    paymentToken: paymentTokenAddress,
    treasury: strategyAccessTreasury,
    strategyAccessTreasury,
    settlementTreasuryRecipient,
    deployedAtUtc: exportedAt,
    contracts: deployments.map((deployment) => ({
      chainId,
      networkName: hre.network.name,
      deployer: deployerAddress,
      deployedAtUtc: exportedAt,
      ...deployment
    }))
  };

  const outputDirectory = path.join(__dirname, "..", "deployments");
  fs.mkdirSync(outputDirectory, { recursive: true });
  const outputPath = path.join(outputDirectory, `${hre.network.name}-${chainId}.json`);
  fs.writeFileSync(outputPath, `${JSON.stringify(deploymentArtifact, null, 2)}\n`);
  console.log(`Wrote deployment artifact: ${outputPath}`);
}

function sha256(value) {
  return crypto.createHash("sha256").update(value).digest("hex");
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
