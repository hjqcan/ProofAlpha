const fs = require("fs");
const path = require("path");
const { ethers } = require("ethers");

const DEFAULT_RPC_URL = "https://rpc.testnet.arc.network";
const DEFAULT_CHAIN_ID = 5042002;
const DEFAULT_USDC_ADDRESS = "0x3600000000000000000000000000000000000000";
const DEFAULT_ARTIFACT_ROOT = path.resolve(__dirname, "../../..", "artifacts", "arc-hackathon", "demo-run");

const erc20Abi = [
  "function decimals() view returns (uint8)",
  "function symbol() view returns (string)",
  "function name() view returns (string)"
];

async function main() {
  const rpcUrl = process.env.ARC_TESTNET_RPC_URL || DEFAULT_RPC_URL;
  const expectedChainId = Number(process.env.ARC_TESTNET_CHAIN_ID || DEFAULT_CHAIN_ID);
  const usdcAddress = process.env.ARC_SETTLEMENT_USDC_ADDRESS || DEFAULT_USDC_ADDRESS;
  const artifactRoot = process.env.ARC_HACKATHON_ARTIFACT_ROOT
    ? path.resolve(process.env.ARC_HACKATHON_ARTIFACT_ROOT)
    : DEFAULT_ARTIFACT_ROOT;
  fs.mkdirSync(artifactRoot, { recursive: true });
  const outputPath = process.env.ARC_TESTNET_PREFLIGHT_OUTPUT
    ? path.resolve(process.env.ARC_TESTNET_PREFLIGHT_OUTPUT)
    : path.join(artifactRoot, "arc-testnet-preflight.json");

  const startedAtUtc = new Date().toISOString();
  try {
    if (!ethers.isAddress(usdcAddress)) {
      throw new Error(`ARC_SETTLEMENT_USDC_ADDRESS is not a valid EVM address: ${usdcAddress}`);
    }

    const provider = new ethers.JsonRpcProvider(rpcUrl);
    const network = await provider.getNetwork();
    const chainId = Number(network.chainId);
    const latestBlockNumber = await provider.getBlockNumber();
    const feeData = await provider.getFeeData();
    const code = await provider.getCode(usdcAddress);
    const usdc = new ethers.Contract(usdcAddress, erc20Abi, provider);
    const decimals = Number(await usdc.decimals());
    const symbol = await optionalString(() => usdc.symbol());
    const name = await optionalString(() => usdc.name());

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
        id: "usdc-code",
        status: code && code !== "0x" ? "Passed" : "Failed",
        details: `address=${ethers.getAddress(usdcAddress)}; codeBytes=${code === "0x" ? 0 : (code.length - 2) / 2}`
      },
      {
        id: "usdc-decimals",
        status: decimals === 6 ? "Passed" : "Failed",
        details: `decimals=${decimals}`
      },
      {
        id: "gas-price",
        status: feeData.gasPrice && feeData.gasPrice > 0n ? "Passed" : "Failed",
        details: `gasPriceWei=${feeData.gasPrice ? feeData.gasPrice.toString() : "null"}`
      }
    ];
    const status = checks.every((check) => check.status === "Passed") ? "Passed" : "Failed";
    const artifact = {
      documentVersion: "proofalpha-arc-testnet-preflight.v1",
      status,
      rpcUrl,
      expectedChainId,
      chainId,
      latestBlockNumber,
      feeData: {
        gasPriceWei: feeData.gasPrice ? feeData.gasPrice.toString() : null,
        maxFeePerGasWei: feeData.maxFeePerGas ? feeData.maxFeePerGas.toString() : null,
        maxPriorityFeePerGasWei: feeData.maxPriorityFeePerGas ? feeData.maxPriorityFeePerGas.toString() : null
      },
      usdc: {
        address: ethers.getAddress(usdcAddress),
        decimals,
        symbol,
        name,
        codePresent: code && code !== "0x"
      },
      checks,
      startedAtUtc,
      exportedAtUtc: new Date().toISOString()
    };
    writeArtifact(outputPath, artifact);
    console.log(`Arc Testnet preflight status: ${status}`);
    console.log(`Preflight artifact: ${outputPath}`);
    if (status !== "Passed") {
      process.exitCode = 1;
    }
  } catch (error) {
    const artifact = {
      documentVersion: "proofalpha-arc-testnet-preflight.v1",
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

async function optionalString(fn) {
  try {
    const value = await fn();
    return typeof value === "string" ? value : String(value);
  } catch {
    return null;
  }
}

function writeArtifact(outputPath, value) {
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, `${JSON.stringify(value, null, 2)}\n`);
}

main();
