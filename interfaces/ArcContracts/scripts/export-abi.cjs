const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const contracts = [
  "SignalRegistry",
  "StrategyAccess",
  "PerformanceLedger",
  "RevenueSettlement",
  "TestUsdc"
];

const root = path.join(__dirname, "..");
const outputDirectory = path.join(root, "abi");
fs.mkdirSync(outputDirectory, { recursive: true });

const manifest = [];

for (const contractName of contracts) {
  const artifactPath = path.join(root, "artifacts", "contracts", contractName === "TestUsdc" ? "test" : "", `${contractName}.sol`, `${contractName}.json`);
  const artifact = JSON.parse(fs.readFileSync(artifactPath, "utf8"));
  const abiJson = JSON.stringify(artifact.abi, null, 2);
  const abiPath = path.join(outputDirectory, `${contractName}.json`);
  fs.writeFileSync(abiPath, `${abiJson}\n`);
  manifest.push({
    contractName,
    abiPath: path.relative(root, abiPath).replaceAll("\\", "/"),
    abiHash: crypto.createHash("sha256").update(JSON.stringify(artifact.abi)).digest("hex")
  });
}

fs.writeFileSync(path.join(outputDirectory, "abi-manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);
console.log(`Exported ${manifest.length} ABI files to ${outputDirectory}`);
