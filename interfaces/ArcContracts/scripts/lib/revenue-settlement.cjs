async function recordSettlementFromRequest(hre, request) {
  validateRequest(request);

  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);
  if (request.chainId && Number(request.chainId) !== chainId) {
    throw new Error(`Configured chain ${request.chainId} does not match connected chain ${chainId}.`);
  }

  const settlement = await hre.ethers.getContractAt("RevenueSettlement", request.revenueSettlement);
  try {
    const tx = await settlement.recordSettlement(
      request.settlementId,
      request.signalId,
      request.tokenAddress,
      BigInt(request.grossAmountMicroUsdc),
      request.recipients,
      request.shareBps
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
    if (!isDuplicateSettlementError(error)) {
      throw error;
    }

    return {
      transactionHash: null,
      confirmed: false,
      duplicate: true,
      errorCode: "DuplicateSettlement",
      chainId,
      networkName: hre.network.name,
      blockNumber: null
    };
  }
}

function validateRequest(request) {
  requireString(request.revenueSettlement, "revenueSettlement");
  requireString(request.settlementId, "settlementId");
  requireString(request.signalId, "signalId");
  requireString(request.tokenAddress, "tokenAddress");
  requireString(request.grossAmountMicroUsdc, "grossAmountMicroUsdc");
  if (!Array.isArray(request.recipients) || request.recipients.length === 0) {
    throw new Error("recipients must contain at least one recipient.");
  }

  if (!Array.isArray(request.shareBps) || request.shareBps.length !== request.recipients.length) {
    throw new Error("shareBps must have the same length as recipients.");
  }

  const totalBps = request.shareBps.reduce((total, share) => total + Number(share), 0);
  if (totalBps !== 10000 || request.shareBps.some((share) => Number(share) <= 0)) {
    throw new Error("shareBps must be positive and sum to 10000.");
  }
}

function requireString(value, fieldName) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new Error(`${fieldName} is required.`);
  }
}

function isDuplicateSettlementError(error) {
  const text = [
    error?.shortMessage,
    error?.message,
    error?.errorName,
    error?.info?.error?.message
  ]
    .filter(Boolean)
    .join("\n");
  return text.includes("DuplicateSettlement");
}

module.exports = {
  recordSettlementFromRequest,
  validateRequest,
  isDuplicateSettlementError
};
