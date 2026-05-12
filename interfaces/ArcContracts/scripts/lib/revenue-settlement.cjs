async function recordSettlementFromRequest(hre, request) {
  validateRequest(request);

  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);
  if (request.chainId && Number(request.chainId) !== chainId) {
    throw new Error(`Configured chain ${request.chainId} does not match connected chain ${chainId}.`);
  }

  const settlement = await hre.ethers.getContractAt("RevenueSettlement", request.revenueSettlement);
  const shouldDistribute = request.distribute === true;
  const token = shouldDistribute
    ? await hre.ethers.getContractAt("TestUsdc", request.tokenAddress)
    : null;
  const vaultBalanceBefore = token ? await token.balanceOf(request.revenueSettlement) : null;
  const recipientBalancesBefore = token
    ? await Promise.all(request.recipients.map((recipient) => token.balanceOf(recipient)))
    : [];
  try {
    const tx = shouldDistribute
      ? await settlement.recordAndDistributeSettlement(
        request.settlementId,
        request.signalId,
        request.tokenAddress,
        BigInt(request.grossAmountMicroUsdc),
        request.recipients,
        request.shareBps
      )
      : await settlement.recordSettlement(
      request.settlementId,
      request.signalId,
      request.tokenAddress,
      BigInt(request.grossAmountMicroUsdc),
      request.recipients,
      request.shareBps
    );
    const receipt = await tx.wait();
    const vaultBalanceAfter = token ? await token.balanceOf(request.revenueSettlement) : null;
    const recipientBalancesAfter = token
      ? await Promise.all(request.recipients.map((recipient) => token.balanceOf(recipient)))
      : [];
    return {
      transactionHash: tx.hash,
      confirmed: receipt?.status === 1,
      duplicate: false,
      errorCode: null,
      chainId,
      networkName: hre.network.name,
      blockNumber: receipt ? Number(receipt.blockNumber) : null,
      distribution: token ? {
        distributed: true,
        vaultBeforeAtomic: vaultBalanceBefore.toString(),
        vaultAfterAtomic: vaultBalanceAfter.toString(),
        vaultDeltaAtomic: (vaultBalanceAfter - vaultBalanceBefore).toString(),
        recipients: request.recipients.map((recipient, index) => ({
          walletAddress: recipient,
          beforeAtomic: recipientBalancesBefore[index].toString(),
          afterAtomic: recipientBalancesAfter[index].toString(),
          deltaAtomic: (recipientBalancesAfter[index] - recipientBalancesBefore[index]).toString()
        }))
      } : {
        distributed: false
      }
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
