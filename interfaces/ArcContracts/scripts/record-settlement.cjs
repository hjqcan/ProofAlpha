const fs = require("fs");
const hre = require("hardhat");
const { recordSettlementFromRequest } = require("./lib/revenue-settlement.cjs");

async function main() {
  const requestPath = process.env.ARC_REVENUE_SETTLEMENT_REQUEST;
  const resultPath = process.env.ARC_REVENUE_SETTLEMENT_RESULT;
  if (!requestPath || !resultPath) {
    throw new Error("ARC_REVENUE_SETTLEMENT_REQUEST and ARC_REVENUE_SETTLEMENT_RESULT are required.");
  }

  const request = JSON.parse(fs.readFileSync(requestPath, "utf8"));
  const result = await recordSettlementFromRequest(hre, request);
  fs.writeFileSync(resultPath, `${JSON.stringify(result, null, 2)}\n`);
  console.log(`Wrote revenue settlement result: ${resultPath}`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
