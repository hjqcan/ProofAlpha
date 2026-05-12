async function publishSignalFromRequest(hre, request) {
  validateRequest(request);

  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);
  if (request.chainId && Number(request.chainId) !== chainId) {
    throw new Error(`Configured chain ${request.chainId} does not match connected chain ${chainId}.`);
  }

  const registry = await hre.ethers.getContractAt("SignalRegistry", request.signalRegistry);
  try {
    const tx = await registry.publishSignal(
      request.signalId,
      request.agentAddress,
      request.venue,
      request.strategyKey,
      request.reasoningHash,
      request.riskEnvelopeHash,
      BigInt(request.expectedEdgeBps),
      BigInt(request.maxNotionalUsdcAtomic),
      BigInt(request.validUntilUnixSeconds)
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
    if (!isDuplicateSignalError(error)) {
      throw error;
    }

    return {
      transactionHash: null,
      confirmed: false,
      duplicate: true,
      errorCode: "DuplicateSignal",
      chainId,
      networkName: hre.network.name,
      blockNumber: null
    };
  }
}

function validateRequest(request) {
  requireString(request.signalRegistry, "signalRegistry");
  requireString(request.signalId, "signalId");
  requireString(request.agentAddress, "agentAddress");
  requireString(request.venue, "venue");
  requireString(request.strategyKey, "strategyKey");
  requireString(request.reasoningHash, "reasoningHash");
  requireString(request.riskEnvelopeHash, "riskEnvelopeHash");
  requireString(request.expectedEdgeBps, "expectedEdgeBps");
  requireString(request.maxNotionalUsdcAtomic, "maxNotionalUsdcAtomic");
  if (request.validUntilUnixSeconds === undefined || request.validUntilUnixSeconds === null) {
    throw new Error("validUntilUnixSeconds is required.");
  }
}

function requireString(value, fieldName) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new Error(`${fieldName} is required.`);
  }
}

function isDuplicateSignalError(error) {
  const text = [
    error?.shortMessage,
    error?.message,
    error?.errorName,
    error?.info?.error?.message
  ]
    .filter(Boolean)
    .join("\n");
  return text.includes("DuplicateSignal");
}

module.exports = {
  publishSignalFromRequest,
  validateRequest,
  isDuplicateSignalError
};
