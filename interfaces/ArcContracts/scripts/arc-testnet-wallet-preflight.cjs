const fs = require("fs");
const path = require("path");
const { ethers } = require("ethers");

const DEFAULT_RPC_URL = "https://rpc.testnet.arc.network";
const DEFAULT_CHAIN_ID = 5042002;
const DEFAULT_USDC_ADDRESS = "0x3600000000000000000000000000000000000000";
const DEFAULT_ARTIFACT_ROOT = path.resolve(__dirname, "../../..", "artifacts", "arc-hackathon", "demo-run");
const PRICE_USDC_ATOMIC = 25_000_000n;
const DEFAULT_GAS_BUFFER_BPS = 15000n;
const DEFAULT_CLOSURE_TX_GAS_BUFFER_UNITS = 800_000n;

const erc20Abi = [
  "function decimals() view returns (uint8)",
  "function balanceOf(address account) view returns (uint256)"
];

async function main() {
  const rpcUrl = process.env.ARC_TESTNET_RPC_URL || DEFAULT_RPC_URL;
  const expectedChainId = Number(process.env.ARC_TESTNET_CHAIN_ID || DEFAULT_CHAIN_ID);
  const usdcAddress = process.env.ARC_SETTLEMENT_USDC_ADDRESS || DEFAULT_USDC_ADDRESS;
  const artifactRoot = process.env.ARC_HACKATHON_ARTIFACT_ROOT
    ? path.resolve(process.env.ARC_HACKATHON_ARTIFACT_ROOT)
    : DEFAULT_ARTIFACT_ROOT;
  fs.mkdirSync(artifactRoot, { recursive: true });
  const outputPath = process.env.ARC_TESTNET_WALLET_PREFLIGHT_OUTPUT
    ? path.resolve(process.env.ARC_TESTNET_WALLET_PREFLIGHT_OUTPUT)
    : path.join(artifactRoot, "arc-testnet-wallet-preflight.json");
  const startedAtUtc = new Date().toISOString();

  try {
    const privateKey = requirePrivateKey(process.env.ARC_SETTLEMENT_PRIVATE_KEY);
    const treasury = requireAddress(process.env.ARC_SETTLEMENT_TREASURY, "ARC_SETTLEMENT_TREASURY");
    const provider = new ethers.JsonRpcProvider(rpcUrl);
    const wallet = new ethers.Wallet(privateKey, provider);
    const walletAddress = await wallet.getAddress();
    const network = await provider.getNetwork();
    const chainId = Number(network.chainId);
    const latestBlockNumber = await provider.getBlockNumber();
    const feeData = await provider.getFeeData();
    const gasPriceWei = feeData.gasPrice || feeData.maxFeePerGas;
    if (!gasPriceWei || gasPriceWei <= 0n) {
      throw new Error("Arc Testnet did not return usable gas price data.");
    }

    if (treasury.toLowerCase() === walletAddress.toLowerCase()) {
      throw new Error("ARC_SETTLEMENT_TREASURY must be different from the subscriber/deployer address.");
    }

    if (!ethers.isAddress(usdcAddress)) {
      throw new Error(`ARC_SETTLEMENT_USDC_ADDRESS is not a valid EVM address: ${usdcAddress}`);
    }

    const usdc = new ethers.Contract(usdcAddress, erc20Abi, provider);
    const usdcDecimals = Number(await usdc.decimals());
    const usdcBalanceAtomic = await usdc.balanceOf(walletAddress);
    const nativeBalanceWei = await provider.getBalance(walletAddress);
    const deploymentEstimate = await estimateDeployment(provider, walletAddress, usdcAddress, treasury);
    const closureTxGasBufferUnits = parsePositiveBigInt(
      process.env.ARC_SETTLEMENT_CLOSURE_TX_GAS_BUFFER_UNITS,
      DEFAULT_CLOSURE_TX_GAS_BUFFER_UNITS,
      "ARC_SETTLEMENT_CLOSURE_TX_GAS_BUFFER_UNITS"
    );
    const gasBufferBps = parsePositiveBigInt(
      process.env.ARC_SETTLEMENT_GAS_BUFFER_BPS,
      DEFAULT_GAS_BUFFER_BPS,
      "ARC_SETTLEMENT_GAS_BUFFER_BPS"
    );
    const estimatedGasUnits = deploymentEstimate.totalGasUnits + closureTxGasBufferUnits;
    const estimatedNativeCostWei = estimatedGasUnits * gasPriceWei;
    const requiredNativeWei = (estimatedNativeCostWei * gasBufferBps + 9999n) / 10000n;

    const checks = [
      {
        id: "chain-id",
        status: chainId === expectedChainId ? "Passed" : "Failed",
        details: `expected=${expectedChainId}; actual=${chainId}`
      },
      {
        id: "latest-block",
        status: latestBlockNumber > 0 ? "Passed" : "Failed",
        details: `latestBlockNumber=${latestBlockNumber}`
      },
      {
        id: "treasury-address",
        status: treasury.toLowerCase() !== walletAddress.toLowerCase() ? "Passed" : "Failed",
        details: `treasury=${treasury}; wallet=${walletAddress}`
      },
      {
        id: "usdc-decimals",
        status: usdcDecimals === 6 ? "Passed" : "Failed",
        details: `decimals=${usdcDecimals}`
      },
      {
        id: "usdc-balance",
        status: usdcBalanceAtomic >= PRICE_USDC_ATOMIC ? "Passed" : "Failed",
        details: `requiredAtomic=${PRICE_USDC_ATOMIC}; actualAtomic=${usdcBalanceAtomic}`
      },
      {
        id: "native-gas-balance",
        status: nativeBalanceWei >= requiredNativeWei ? "Passed" : "Failed",
        details: `requiredWei=${requiredNativeWei}; actualWei=${nativeBalanceWei}; gasPriceWei=${gasPriceWei}`
      }
    ];
    const status = checks.every((check) => check.status === "Passed") ? "Passed" : "Failed";
    const artifact = {
      documentVersion: "proofalpha-arc-testnet-wallet-preflight.v1",
      status,
      rpcUrl,
      expectedChainId,
      chainId,
      latestBlockNumber,
      walletAddress,
      treasury,
      usdc: {
        address: ethers.getAddress(usdcAddress),
        decimals: usdcDecimals,
        requiredAtomic: PRICE_USDC_ATOMIC.toString(),
        balanceAtomic: usdcBalanceAtomic.toString()
      },
      nativeGas: {
        balanceWei: nativeBalanceWei.toString(),
        gasPriceWei: gasPriceWei.toString(),
        estimatedDeploymentGasUnits: deploymentEstimate.totalGasUnits.toString(),
        closureTxGasBufferUnits: closureTxGasBufferUnits.toString(),
        gasBufferBps: gasBufferBps.toString(),
        requiredWei: requiredNativeWei.toString()
      },
      deploymentEstimate: deploymentEstimate.contracts,
      checks,
      startedAtUtc,
      exportedAtUtc: new Date().toISOString()
    };
    writeArtifact(outputPath, artifact);
    console.log(`Arc Testnet wallet preflight status: ${status}`);
    console.log(`Wallet preflight artifact: ${outputPath}`);
    if (status !== "Passed") {
      process.exitCode = 1;
    }
  } catch (error) {
    const artifact = {
      documentVersion: "proofalpha-arc-testnet-wallet-preflight.v1",
      status: "Failed",
      rpcUrl,
      expectedChainId,
      usdc: {
        address: usdcAddress
      },
      error: error instanceof Error ? error.message : String(error),
      checks: [],
      startedAtUtc,
      exportedAtUtc: new Date().toISOString()
    };
    writeArtifact(outputPath, artifact);
    console.error(error);
    process.exitCode = 1;
  }
}

async function estimateDeployment(provider, walletAddress, usdcAddress, treasury) {
  const specs = [
    { name: "SignalRegistry", args: [] },
    { name: "StrategyAccess", args: [usdcAddress, treasury] },
    { name: "PerformanceLedger", args: [] },
    { name: "RevenueSettlement", args: [] }
  ];
  const contracts = [];
  let totalGasUnits = 0n;

  for (const spec of specs) {
    const artifact = readContractArtifact(spec.name);
    const factory = new ethers.ContractFactory(artifact.abi, artifact.bytecode);
    const tx = await factory.getDeployTransaction(...spec.args);
    const gasUnits = await provider.estimateGas({
      ...tx,
      from: walletAddress
    });
    totalGasUnits += gasUnits;
    contracts.push({
      contractName: spec.name,
      gasUnits: gasUnits.toString()
    });
  }

  return { totalGasUnits, contracts };
}

function readContractArtifact(name) {
  const artifactPath = path.resolve(__dirname, "..", "artifacts", "contracts", `${name}.sol`, `${name}.json`);
  if (!fs.existsSync(artifactPath)) {
    throw new Error(`Contract artifact is missing for ${name}. Run npm --prefix interfaces\\ArcContracts run build first.`);
  }

  return JSON.parse(fs.readFileSync(artifactPath, "utf8"));
}

function requirePrivateKey(value) {
  if (!value || !/^0x[0-9a-fA-F]{64}$/.test(value)) {
    throw new Error("ARC_SETTLEMENT_PRIVATE_KEY must be a 0x-prefixed 32-byte private key.");
  }

  return value;
}

function requireAddress(value, name) {
  if (!value || !ethers.isAddress(value)) {
    throw new Error(`${name} must be a valid EVM address.`);
  }

  return ethers.getAddress(value);
}

function parsePositiveBigInt(value, fallback, name) {
  if (!value || value.trim() === "") {
    return fallback;
  }

  if (!/^[1-9][0-9]*$/.test(value)) {
    throw new Error(`${name} must be a positive integer.`);
  }

  return BigInt(value);
}

function writeArtifact(outputPath, value) {
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(value, null, 2)}\n`);
}

main();
