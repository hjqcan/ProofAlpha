async function recordOutcomeFromRequest(hre, request) {
  validateRequest(request);

  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);
  if (request.chainId && Number(request.chainId) !== chainId) {
    throw new Error(`Configured chain ${request.chainId} does not match connected chain ${chainId}.`);
  }

  const ledger = await hre.ethers.getContractAt("PerformanceLedger", request.performanceLedger);
  try {
    const tx = await ledger.recordOutcome(
      request.signalId,
      Number(request.status),
      BigInt(request.realizedPnlBps),
      BigInt(request.slippageBps),
      request.outcomeHash
    );
    const receipt = await tx.wait();
    return {
      transactionHash: tx.hash,
      confirmed: receipt?.status === 1,
      duplicate: false,
      errorCode: null,
      chainId,
      networkName: hre.network.name,
      blockNumber: receipt ? Number(receipt.blockNumber) : null
    };
  } catch (error) {
    if (!isDuplicateOutcomeError(error)) {
      throw error;
    }

    return {
      transactionHash: null,
      confirmed: false,
      duplicate: true,
      errorCode: "TerminalOutcomeAlreadyRecorded",
      chainId,
      networkName: hre.network.name,
      blockNumber: null
    };
  }
}

function validateRequest(request) {
  requireString(request.performanceLedger, "performanceLedger");
  requireString(request.signalId, "signalId");
  requireString(request.outcomeHash, "outcomeHash");
  requireString(request.realizedPnlBps, "realizedPnlBps");
  requireString(request.slippageBps, "slippageBps");
  if (request.status === undefined || request.status === null || Number(request.status) <= 0) {
    throw new Error("status must be a non-zero PerformanceLedger outcome status.");
  }
}

function requireString(value, fieldName) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new Error(`${fieldName} is required.`);
  }
}

function isDuplicateOutcomeError(error) {
  const text = [
    error?.shortMessage,
    error?.message,
    error?.errorName,
    error?.info?.error?.message
  ]
    .filter(Boolean)
    .join("\n");
  return text.includes("TerminalOutcomeAlreadyRecorded");
}

module.exports = {
  recordOutcomeFromRequest,
  validateRequest,
  isDuplicateOutcomeError
};
