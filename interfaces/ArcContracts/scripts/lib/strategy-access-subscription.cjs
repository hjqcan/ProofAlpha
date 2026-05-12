async function subscribeFromRequest(hre, request) {
  validateRequest(request);

  const network = await hre.ethers.provider.getNetwork();
  const chainId = Number(network.chainId);
  if (request.chainId && Number(request.chainId) !== chainId) {
    throw new Error(`Configured chain ${request.chainId} does not match connected chain ${chainId}.`);
  }

  const token = await hre.ethers.getContractAt("TestUsdc", request.paymentToken);
  const access = await hre.ethers.getContractAt("StrategyAccess", request.strategyAccess);
  const subscriber = await hre.ethers.getSigner(request.subscriberAddress);
  const strategyKeyBytes32 = hre.ethers.id(request.strategyKey);
  const price = BigInt(request.priceUsdcAtomic);

  const mintTx = await token.mint(request.subscriberAddress, price);
  const mintReceipt = await mintTx.wait();

  const planTx = await access.setPlan(
    BigInt(request.planId),
    strategyKeyBytes32,
    price,
    BigInt(request.durationSeconds),
    true
  );
  const planReceipt = await planTx.wait();

  const approvalTx = await token.connect(subscriber).approve(request.strategyAccess, price);
  const approvalReceipt = await approvalTx.wait();

  const subscribeTx = await access.connect(subscriber).subscribe(strategyKeyBytes32, BigInt(request.planId));
  const subscribeReceipt = await subscribeTx.wait();
  const subscribedEvent = findEvent(access, subscribeReceipt, "StrategySubscribed");
  if (!subscribedEvent) {
    throw new Error("StrategySubscribed event was not emitted.");
  }

  const accessExpiresAt = await access.accessExpiresAt(request.subscriberAddress, strategyKeyBytes32);
  const hasAccess = await access.hasAccess(request.subscriberAddress, strategyKeyBytes32);
  const treasuryBalance = await token.balanceOf(request.treasuryAddress);
  const subscriberBalance = await token.balanceOf(request.subscriberAddress);

  return {
    chainId,
    networkName: hre.network.name,
    strategyKeyBytes32,
    mintTransactionHash: mintTx.hash,
    mintBlockNumber: blockNumber(mintReceipt),
    planTransactionHash: planTx.hash,
    planBlockNumber: blockNumber(planReceipt),
    approvalTransactionHash: approvalTx.hash,
    approvalBlockNumber: blockNumber(approvalReceipt),
    subscriptionTransactionHash: subscribeTx.hash,
    subscriptionBlockNumber: blockNumber(subscribeReceipt),
    confirmed: subscribeReceipt?.status === 1,
    event: {
      user: subscribedEvent.args.user,
      strategyKey: subscribedEvent.args.strategyKey,
      planId: subscribedEvent.args.planId.toString(),
      amount: subscribedEvent.args.amount.toString(),
      expiresAt: subscribedEvent.args.expiresAt.toString()
    },
    onchainSummary: {
      hasAccess,
      accessExpiresAt: accessExpiresAt.toString(),
      treasuryBalance: treasuryBalance.toString(),
      subscriberBalance: subscriberBalance.toString()
    }
  };
}

function validateRequest(request) {
  requireString(request.paymentToken, "paymentToken");
  requireString(request.strategyAccess, "strategyAccess");
  requireString(request.subscriberAddress, "subscriberAddress");
  requireString(request.treasuryAddress, "treasuryAddress");
  requireString(request.strategyKey, "strategyKey");
  requireString(request.priceUsdcAtomic, "priceUsdcAtomic");
  if (!Number.isInteger(Number(request.planId)) || Number(request.planId) <= 0) {
    throw new Error("planId must be greater than zero.");
  }

  if (!Number.isInteger(Number(request.durationSeconds)) || Number(request.durationSeconds) <= 0) {
    throw new Error("durationSeconds must be greater than zero.");
  }
}

function findEvent(contract, receipt, eventName) {
  for (const log of receipt?.logs || []) {
    try {
      const parsed = contract.interface.parseLog(log);
      if (parsed?.name === eventName) {
        return parsed;
      }
    } catch {
      // Ignore logs emitted by other contracts in the same transaction.
    }
  }

  return null;
}

function blockNumber(receipt) {
  return receipt ? Number(receipt.blockNumber) : null;
}

function requireString(value, fieldName) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new Error(`${fieldName} is required.`);
  }
}

module.exports = {
  subscribeFromRequest,
  validateRequest
};
