const fs = require("fs");
const hre = require("hardhat");
const { publishSignalFromRequest } = require("./lib/signal-publication.cjs");

async function main() {
  const requestPath = process.env.ARC_SIGNAL_PUBLISH_REQUEST;
  const resultPath = process.env.ARC_SIGNAL_PUBLISH_RESULT;
  if (!requestPath || !resultPath) {
    throw new Error("ARC_SIGNAL_PUBLISH_REQUEST and ARC_SIGNAL_PUBLISH_RESULT are required.");
  }

  const request = JSON.parse(fs.readFileSync(requestPath, "utf8"));
  const result = await publishSignalFromRequest(hre, request);
  fs.writeFileSync(resultPath, `${JSON.stringify(result, null, 2)}\n`);
  console.log(`Wrote signal publication result: ${resultPath}`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
