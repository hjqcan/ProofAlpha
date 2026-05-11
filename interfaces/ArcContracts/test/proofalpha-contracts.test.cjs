const { expect } = require("chai");
const { ethers } = require("hardhat");

describe("ProofAlpha Arc contract suite", function () {
  async function deploy(name, args = []) {
    const factory = await ethers.getContractFactory(name);
    const contract = await factory.deploy(...args);
    await contract.waitForDeployment();
    return contract;
  }

  async function expectRevert(action, fragment) {
    try {
      await action;
      expect.fail(`Expected revert containing ${fragment}`);
    } catch (error) {
      expect(error.message).to.contain(fragment);
    }
  }

  it("SignalRegistry publishes immutable signal summaries", async function () {
    const [deployer, agent] = await ethers.getSigners();
    const registry = await deploy("SignalRegistry");
    const signalId = ethers.id("signal-1");
    const reasoningHash = ethers.id("reasoning-1");
    const riskEnvelopeHash = ethers.id("risk-envelope-1");

    await registry.publishSignal(
      signalId,
      agent.address,
      "polymarket",
      "repricing_lag_arbitrage",
      reasoningHash,
      riskEnvelopeHash,
      42,
      ethers.parseUnits("100", 6),
      1_800_000_000
    );

    const summary = await registry.getSignal(signalId);
    expect(summary.agent).to.equal(agent.address);
    expect(summary.venue).to.equal("polymarket");
    expect(summary.strategyKey).to.equal("repricing_lag_arbitrage");
    expect(summary.reasoningHash).to.equal(reasoningHash);
    expect(summary.riskEnvelopeHash).to.equal(riskEnvelopeHash);
    expect(summary.expectedEdgeBps).to.equal(42n);
    expect(summary.exists).to.equal(true);

    await expectRevert(
      registry.publishSignal(
        signalId,
        agent.address,
        "polymarket",
        "repricing_lag_arbitrage",
        reasoningHash,
        riskEnvelopeHash,
        42,
        100,
        1_800_000_000
      ),
      "DuplicateSignal"
    );

    await expectRevert(
      registry.publishSignal(
        ethers.id("signal-2"),
        ethers.ZeroAddress,
        "polymarket",
        "repricing_lag_arbitrage",
        reasoningHash,
        riskEnvelopeHash,
        42,
        100,
        1_800_000_000
      ),
      "ZeroAgent"
    );

    await expectRevert(
      registry.publishSignal(
        ethers.id("signal-3"),
        deployer.address,
        "",
        "repricing_lag_arbitrage",
        reasoningHash,
        riskEnvelopeHash,
        42,
        100,
        1_800_000_000
      ),
      "EmptyVenue"
    );
  });

  it("StrategyAccess accepts test USDC subscriptions and exposes access state", async function () {
    const [deployer, subscriber, treasury] = await ethers.getSigners();
    const token = await deploy("TestUsdc");
    const access = await deploy("StrategyAccess", [await token.getAddress(), treasury.address]);
    const strategyKey = ethers.id("repricing_lag_arbitrage");
    const planId = 1;
    const price = ethers.parseUnits("25", 6);

    await token.mint(subscriber.address, price);
    await access.setPlan(planId, strategyKey, price, 30 * 24 * 60 * 60, true);
    await token.connect(subscriber).approve(await access.getAddress(), price);
    await access.connect(subscriber).subscribe(strategyKey, planId);

    expect(await access.hasAccess(subscriber.address, strategyKey)).to.equal(true);
    expect((await access.accessExpiresAt(subscriber.address, strategyKey)) > 0n).to.equal(true);
    expect(await token.balanceOf(treasury.address)).to.equal(price);

    await expectRevert(access.connect(subscriber).subscribe(ethers.id("wrong_strategy"), planId), "StrategyPlanMismatch");
    await expectRevert(access.connect(subscriber).setPlan(2, strategyKey, price, 1, true), "NotOwner");
    expect(await access.owner()).to.equal(deployer.address);
  });

  it("PerformanceLedger records one terminal outcome and requires correction events", async function () {
    const ledger = await deploy("PerformanceLedger");
    const signalId = ethers.id("signal-1");
    const outcomeHash = ethers.id("outcome-1");

    await ledger.recordOutcome(signalId, 1, 15, 2, outcomeHash);
    const summary = await ledger.getOutcome(signalId);
    expect(summary.status).to.equal(1n);
    expect(summary.realizedPnlBps).to.equal(15n);
    expect(summary.slippageBps).to.equal(2n);
    expect(summary.outcomeHash).to.equal(outcomeHash);
    expect(summary.exists).to.equal(true);

    await expectRevert(ledger.recordOutcome(signalId, 5, 0, 0, ethers.id("failed")), "TerminalOutcomeAlreadyRecorded");

    const correctionHash = ethers.id("operator-correction-1");
    await ledger.correctOutcome(signalId, 5, -20, 4, ethers.id("corrected-outcome"), correctionHash);
    const corrected = await ledger.getOutcome(signalId);
    expect(corrected.status).to.equal(5n);
    expect(corrected.realizedPnlBps).to.equal(-20n);
  });

  it("RevenueSettlement records event-only settlement journals without duplicate ids", async function () {
    const [, agent, platform, subscriber, token] = await ethers.getSigners();
    const settlement = await deploy("RevenueSettlement");
    const settlementId = ethers.id("settlement-1");
    const signalId = ethers.id("signal-1");
    const recipients = [agent.address, platform.address, subscriber.address];
    const shares = [7000, 2000, 1000];

    await settlement.recordSettlement(settlementId, signalId, token.address, ethers.parseUnits("10", 6), recipients, shares);
    expect(await settlement.settlementRecorded(settlementId)).to.equal(true);

    await expectRevert(
      settlement.recordSettlement(settlementId, signalId, token.address, ethers.parseUnits("10", 6), recipients, shares),
      "DuplicateSettlement"
    );
    await expectRevert(
      settlement.recordSettlement(ethers.id("settlement-2"), signalId, token.address, 1, recipients, [9000, 1000]),
      "ShareLengthMismatch"
    );
    await expectRevert(
      settlement.recordSettlement(ethers.id("settlement-3"), signalId, token.address, 1, recipients, [9000, 500, 400]),
      "InvalidShareBps"
    );
  });
});
