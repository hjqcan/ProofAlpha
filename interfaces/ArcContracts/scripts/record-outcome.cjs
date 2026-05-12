const fs = require("fs");
const hre = require("hardhat");
const { recordOutcomeFromRequest } = require("./lib/performance-ledger.cjs");

async function main() {
  const requestPath = process.env.ARC_OUTCOME_RECORD_REQUEST;
  const resultPath = process.env.ARC_OUTCOME_RECORD_RESULT;
  if (!requestPath || !resultPath) {
    throw new Error("ARC_OUTCOME_RECORD_REQUEST and ARC_OUTCOME_RECORD_RESULT are required.");
  }

  const request = JSON.parse(fs.readFileSync(requestPath, "utf8"));
  const result = await recordOutcomeFromRequest(hre, request);
  fs.writeFileSync(resultPath, `${JSON.stringify(result, null, 2)}\n`);
  console.log(`Wrote outcome record result: ${resultPath}`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
