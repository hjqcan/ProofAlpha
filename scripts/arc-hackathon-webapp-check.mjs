import { createServer } from 'node:http'
import { readFileSync } from 'node:fs'
import { access, mkdir, readFile, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(scriptDir, '..')
const webRoot = path.join(repoRoot, 'interfaces/WebApp')
const distRoot = path.join(webRoot, 'dist')
const screenshotRoot = path.join(repoRoot, 'artifacts/arc-hackathon/screenshots')
const artifactRoot = path.join(repoRoot, 'artifacts/arc-hackathon/demo-run')
const require = createRequire(path.join(webRoot, 'package.json'))
const { chromium } = require('@playwright/test')

const signalProofArtifact = readJsonArtifact('signal-proof.json')
const signalPublicationArtifact = readJsonArtifact('signal-publication.json')
const subscriptionArtifact = readJsonArtifact('subscription.json')
const accessAllowedArtifact = readJsonArtifact('access-allowed.json')
const performanceOutcomeArtifact = readJsonArtifact('performance-outcome.json')
const agentReputationArtifact = readJsonArtifact('agent-reputation.json')
const revenueSettlementArtifact = readJsonArtifact('revenue-settlement.json')

const now = performanceOutcomeArtifact.recordedAtUtc ||
  revenueSettlementArtifact.exportedAtUtc ||
  signalProofArtifact.createdAtUtc ||
  '2026-05-12T13:00:00Z'
const signalId = signalPublicationArtifact.signalId ||
  signalProofArtifact.opportunityHash ||
  performanceOutcomeArtifact.signalId ||
  revenueSettlementArtifact.signalId ||
  '0x7cc384e6393c4b85f9340bec439a81eca1d31778996494429c750946c7bb5cff'
const subscriptionTransactionHash = subscriptionArtifact.transactionHash ||
  accessAllowedArtifact.data?.sourceTransactionHash ||
  revenueSettlementArtifact.sourceTransactionHash ||
  null
const unlockedWallet = accessAllowedArtifact.data?.walletAddress ||
  subscriptionArtifact.subscriber ||
  '0x1111111111111111111111111111111111111111'
const blockedWallet = '0x2222222222222222222222222222222222222222'
const strategyKey = signalProofArtifact.strategyId ||
  performanceOutcomeArtifact.strategyId ||
  revenueSettlementArtifact.strategyId ||
  'repricing_lag_arbitrage'
const marketId = signalProofArtifact.marketId ||
  performanceOutcomeArtifact.marketId ||
  revenueSettlementArtifact.marketId ||
  'demo-polymarket-market'
const opportunityId = signalProofArtifact.sourceId || 'demo-opportunity-arc-phase-11'
const provenanceHash = signalProofArtifact.reasoningHash ||
  '0xeef6b4492fa63326f52dbf07392a257d7627cbcdba227357e3d0e93de5e49a2a'
const grossUsdc = Number(revenueSettlementArtifact.grossUsdc ??
  (Number(revenueSettlementArtifact.grossAmountMicroUsdc ?? 10000000) / 1_000_000))
const grossMicroUsdc = Number(revenueSettlementArtifact.grossAmountMicroUsdc ??
  revenueSettlementArtifact.grossMicroUsdc ??
  Math.round(grossUsdc * 1_000_000))

const plan = {
  planId: 1,
  strategyKey,
  planName: 'ProofAlpha SignalViewer',
  tier: accessAllowedArtifact.data?.tier || 'SignalViewer',
  priceUsdc: grossUsdc,
  durationSeconds: 604800,
  permissions: Array.from(new Set([
    ...(accessAllowedArtifact.data?.permissions ?? ['ViewSignals', 'ViewReasoning', 'ExportSignal']),
    'RequestPaperAutoTrade'
  ])),
  maxMarkets: 12,
  autoTradingAllowed: true,
  liveTradingAllowed: false,
  createdAtUtc: '2026-05-12T00:00:00Z'
}

const markets = [
  {
    marketId,
    conditionId: 'condition-phase11',
    name: 'Will the Phase 11 subscriber signal remain inside risk envelope?',
    category: 'Macro',
    status: 'Active',
    yesPrice: 0.47,
    noPrice: 0.53,
    liquidity: 182000,
    volume24h: 96000,
    expiresAtUtc: '2026-05-18T12:00:00Z',
    signalScore: 0.87,
    slug: 'fixture-phase11',
    description: 'Subscriber portal fixture with paid signal gating.',
    acceptingOrders: true,
    tokens: [
      { tokenId: 'token-phase11-yes', outcome: 'YES', price: 0.47, winner: null },
      { tokenId: 'token-phase11-no', outcome: 'NO', price: 0.53, winner: null }
    ],
    tags: ['fixture'],
    spread: 0.02,
    source: 'Phase11Demo',
    rankScore: 0.93,
    rankReason: 'strong signal; paid proof; active 24h volume; accepting orders',
    unsuitableReasons: []
  }
]

const strategies = [
  {
    strategyId: strategyKey,
    name: strategyKey.replace(/_/g, ' '),
    state: 'Paused',
    enabled: true,
    configVersion: signalProofArtifact.configVersion || 'phase-11-demo',
    desiredState: 'Running',
    activeMarkets: 3,
    cycleCount: 142,
    snapshotsProcessed: 720,
    channelBacklog: 0,
    isKillSwitchBlocked: false,
    lastHeartbeatUtc: '2026-05-12T12:59:45Z',
    lastDecisionAtUtc: '2026-05-12T12:59:12Z',
    lastError: null,
    blockedReason: null,
    parameters: [
      { name: 'max_notional', value: '2500' },
      { name: 'min_signal', value: '0.32' }
    ]
  }
]

const snapshot = {
  timestampUtc: now,
  dataMode: 'deterministic',
  commandMode: 'paper',
  process: {
    apiStatus: 'Ready',
    environment: 'Mock',
    executionMode: 'Paper',
    modulesEnabled: true,
    readyChecks: 3,
    degradedChecks: 0,
    unhealthyChecks: 0
  },
  risk: {
    killSwitchActive: false,
    killSwitchLevel: 'None',
    killSwitchReason: null,
    killSwitchActivatedAtUtc: null,
    totalCapital: 100000,
    availableCapital: 90000,
    capitalUtilizationPct: 10,
    openNotional: 10000,
    openOrders: 1,
    unhedgedExposures: 0,
    limits: [{ name: 'Capital utilization', current: 10, limit: 35, unit: '%', state: 'good' }]
  },
  metrics: [
    { label: 'Running strategies', value: '0', delta: 'paper paused', tone: 'watch' },
    { label: 'Market liquidity', value: '$182K', delta: 'fixture', tone: 'neutral' }
  ],
  strategies,
  markets,
  orders: [],
  positions: [],
  decisions: [],
  timeline: [],
  capitalCurve: [],
  latencyCurve: []
}

const readiness = {
  contractVersion: 'phase11-smoke',
  checkedAtUtc: now,
  status: 'Ready',
  checks: [
    {
      id: 'mock',
      category: 'Runtime',
      requirement: 'Required',
      status: 'Ready',
      source: 'Phase9Mock',
      lastCheckedAtUtc: now,
      summary: 'Mock API ready.',
      remediationHint: 'None.',
      evidence: { mode: 'mock' }
    }
  ],
  capabilities: [
    { capability: 'PaperTrading', status: 'Ready', blockingCheckIds: [], summary: 'Paper controls available.' },
    { capability: 'LiveTrading', status: 'Blocked', blockingCheckIds: ['paper'], summary: 'Live blocked.' }
  ]
}

const reputation = {
  scope: agentReputationArtifact.scope || 'agent',
  strategyId: agentReputationArtifact.strategyId ?? null,
  totalSignals: agentReputationArtifact.totalSignals ?? 5,
  terminalSignals: agentReputationArtifact.terminalSignals ?? 4,
  pendingSignals: agentReputationArtifact.pendingSignals ?? 1,
  executedSignals: agentReputationArtifact.executedSignals ?? 2,
  expiredSignals: agentReputationArtifact.expiredSignals ?? 1,
  rejectedSignals: agentReputationArtifact.rejectedSignals ?? 1,
  skippedSignals: agentReputationArtifact.skippedSignals ?? 0,
  failedSignals: agentReputationArtifact.failedSignals ?? 0,
  cancelledSignals: agentReputationArtifact.cancelledSignals ?? 0,
  winCount: agentReputationArtifact.winCount ?? 1,
  lossCount: agentReputationArtifact.lossCount ?? 1,
  flatCount: agentReputationArtifact.flatCount ?? 0,
  averageRealizedPnlBps: agentReputationArtifact.averageRealizedPnlBps ?? -3,
  averageSlippageBps: agentReputationArtifact.averageSlippageBps ?? 2,
  riskRejectionRate: agentReputationArtifact.riskRejectionRate ?? 0.25,
  confidenceCoverage: agentReputationArtifact.confidenceCoverage ?? 0.8,
  calculatedAtUtc: agentReputationArtifact.calculatedAtUtc ?? now
}

const opportunitySummary = {
  opportunityId,
  marketId: markets[0].marketId,
  outcome: 'Yes',
  edge: 0.032,
  status: 'Published',
  validUntilUtc: signalProofArtifact.validUntilUtc || '2027-01-15T08:00:00Z',
  createdAtUtc: now,
  updatedAtUtc: now
}

const opportunityDetail = {
  ...opportunitySummary,
  researchRunId: '6e7d41e5-404f-497f-ab26-35aebf0f32c7',
  fairProbability: 0.515,
  confidence: 0.81,
  compiledPolicyJson: JSON.stringify({
    maxNotionalUsdc: signalProofArtifact.maxNotionalUsdc ?? 100,
    minEdgeBps: signalProofArtifact.expectedEdgeBps ?? 42,
    riskTier: 'paper'
  }),
  reason: 'Unlocked alpha: price dislocation remains after spread, latency, and max-notional constraints.',
  scoreJson: JSON.stringify({ liquidity: 0.92, timing: 0.78, risk: 0.14 }),
  evidence: [
    {
      id: '150f248d-78d7-4d6a-bf10-22cd76f4eabc',
      researchRunId: '6e7d41e5-404f-497f-ab26-35aebf0f32c7',
      sourceKind: 'OrderBook',
      sourceName: 'Polymarket L2 snapshot',
      url: 'https://example.invalid/phase11',
      title: 'Order book supports paid signal',
      summary: 'Depth, spread, and imbalance remained inside the subscriber strategy policy.',
      publishedAtUtc: null,
      observedAtUtc: now,
      contentHash: '0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
      sourceQuality: 0.91
    }
  ],
  signalDecision: allowedDecision('arc-opportunity', opportunityId, 'ViewSignals'),
  reasoningDecision: allowedDecision('arc-opportunity-reasoning', opportunityId, 'ViewReasoning')
}

const signalSummary = {
  signalId,
  sourceKind: 'Opportunity',
  sourceId: opportunityId,
  strategyId: strategyKey,
  marketId: markets[0].marketId,
  venue: 'polymarket',
  expectedEdgeBps: signalProofArtifact.expectedEdgeBps ?? 42,
  maxNotionalUsdc: signalProofArtifact.maxNotionalUsdc ?? 100,
  validUntilUtc: signalProofArtifact.validUntilUtc || '2027-01-15T08:00:00Z',
  status: 'Published',
  createdAtUtc: now,
  publishedAtUtc: now,
  provenanceHash,
  evidenceUri: 'artifacts/arc-hackathon/demo-run/provenance/opportunity-phase11-demo.json'
}

const signalDetail = {
  ...signalSummary,
  agentId: signalProofArtifact.agentId || '0x9999999999999999999999999999999999999999',
  reasoningHash: signalProofArtifact.reasoningHash || '0xdddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd',
  riskEnvelopeHash: signalProofArtifact.riskEnvelopeHash || '0xffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff',
  signalHash: signalId,
  sourcePolicyHash: '0xcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd',
  transactionHash: signalPublicationArtifact.transactionHash || '0x3434343434343434343434343434343434343434343434343434343434343434',
  explorerUrl: `https://example.invalid/tx/${signalPublicationArtifact.transactionHash || '0x3434'}`,
  errorCode: null,
  actor: 'phase11-smoke',
  reason: 'Published for subscriber portal smoke.',
  signalDecision: allowedDecision('arc-signal', signalId, 'ViewSignals'),
  reasoningDecision: allowedDecision('arc-signal-reasoning', signalId, 'ViewReasoning')
}

const performanceOutcome = {
  outcomeId: performanceOutcomeArtifact.outcomeId || 'outcome-phase11-1',
  signalId,
  executionId: performanceOutcomeArtifact.executionId || 'paper-exec-phase11-1',
  strategyId: performanceOutcomeArtifact.strategyId || strategyKey,
  marketId: performanceOutcomeArtifact.marketId || markets[0].marketId,
  status: performanceOutcomeArtifact.status || 'ExecutedLoss',
  realizedPnlBps: performanceOutcomeArtifact.realizedPnlBps ?? -12,
  slippageBps: performanceOutcomeArtifact.slippageBps ?? 3,
  fillRate: performanceOutcomeArtifact.fillRate ?? 1,
  reasonCode: performanceOutcomeArtifact.reasonCode ?? null,
  outcomeHash: performanceOutcomeArtifact.outcomeHash || performanceOutcomeArtifact.outcomeId,
  transactionHash: performanceOutcomeArtifact.transactionHash || '0x7878787878787878787878787878787878787878787878787878787878787878',
  explorerUrl: `https://example.invalid/tx/${performanceOutcomeArtifact.transactionHash || '0x7878'}`,
  recordStatus: performanceOutcomeArtifact.confirmed === false ? 'Pending' : 'Confirmed',
  errorCode: null,
  createdAtUtc: performanceOutcomeArtifact.createdAtUtc || now,
  recordedAtUtc: performanceOutcomeArtifact.recordedAtUtc || now
}

const revenueSettlement = {
  settlementId: revenueSettlementArtifact.settlementId || '0x7171717171717171717171717171717171717171717171717171717171717171',
  sourceKind: revenueSettlementArtifact.sourceKind || 'SubscriptionFee',
  signalId,
  executionId: performanceOutcome.executionId,
  walletAddress: unlockedWallet,
  strategyId: strategyKey,
  grossUsdc,
  grossMicroUsdc,
  tokenAddress: revenueSettlementArtifact.paymentToken || '0x0101010101010101010101010101010101010101',
  shares: mapSettlementShares(revenueSettlementArtifact.shares, grossMicroUsdc),
  reason: 'Phase 10 subscription settlement fixture.',
  simulated: revenueSettlementArtifact.simulated ?? false,
  sourceTransactionHash: revenueSettlementArtifact.sourceTransactionHash || subscriptionTransactionHash,
  settlementHash: revenueSettlementArtifact.settlementId || '0x8181818181818181818181818181818181818181818181818181818181818181',
  transactionHash: revenueSettlementArtifact.transactionHash || '0x9191919191919191919191919191919191919191919191919191919191919191',
  explorerUrl: `https://example.invalid/tx/${revenueSettlementArtifact.transactionHash || '0x9191'}`,
  status: revenueSettlementArtifact.confirmed === false ? 'Pending' : 'Confirmed',
  errorCode: null,
  createdAtUtc: revenueSettlementArtifact.exportedAtUtc || now,
  recordedAtUtc: revenueSettlementArtifact.exportedAtUtc || now
}

const provenance = {
  provenanceHash,
  sourceModule: 'OpportunityDiscovery',
  sourceId: 'opportunity-phase8-1',
  agentId: '0x9999999999999999999999999999999999999999',
  marketId: markets[0].marketId,
  strategyId: strategyKey,
  validationStatus: 'Published',
  evidence: [
    {
      evidenceId: 'ev-order-book',
      title: 'Polymarket order-book imbalance',
      summary: 'Reviewed liquidity and spread evidence supported the paper signal.',
      contentHash: '0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
      sourceUri: 'artifact://ev-order-book',
      observedAtUtc: now
    }
  ],
  evidenceSummaryHash: '0xcccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc',
  llmOutputHash: '0xdddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd',
  compiledPolicyHash: '0xeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee',
  generatedPackageHash: null,
  riskEnvelopeHash: '0xffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff',
  evidenceUri: 'artifacts/arc-hackathon/demo-run/provenance/opportunity-phase8-1.json',
  privacyNote: 'Subscriber-safe summary only; raw prompts and local files may remain private or redacted.',
  createdAtUtc: now
}

await mkdir(screenshotRoot, { recursive: true })
const server = await startStaticServer()
const baseUrl = `http://127.0.0.1:${server.address().port}`
const browser = await launchBrowser()
const page = await browser.newPage({ viewport: { width: 1440, height: 980 } })
const consoleFailures = []

page.on('console', (message) => {
  if (message.type() === 'error' && !message.text().includes('status of 403')) {
    consoleFailures.push(message.text())
  }
})
page.on('pageerror', (error) => consoleFailures.push(error.message))
await page.addInitScript(() => window.localStorage.setItem('autotrade.locale', 'en-US'))
await page.route('http://localhost:5080/api/**', fulfillApiFixture)
await page.route('http://127.0.0.1:5080/api/**', fulfillApiFixture)

await page.goto(`${baseUrl}/?strategy=${strategyKey}&wallet=${blockedWallet}#performance`, { waitUntil: 'networkidle' })
await page.getByRole('heading', { name: 'Subscriber portal' }).waitFor({ timeout: 10000 })
await page.getByText('Access denied / ACCESS_NOT_FOUND').first().waitFor({ timeout: 10000 })
await page.getByText('Full reasoning and execution policy are hidden by the API.').waitFor({ timeout: 10000 })
await assertNoHorizontalOverflow(page, 'blocked subscriber portal')
const blockedScreenshot = path.join(screenshotRoot, 'subscriber-blocked.png')
await writeFile(blockedScreenshot, await page.screenshot({ fullPage: true }))

await page.goto(`${baseUrl}/?strategy=${strategyKey}&wallet=${unlockedWallet}&provenance=${provenanceHash}#performance`, { waitUntil: 'networkidle' })
await page.getByText('Unlocked alpha: price dislocation').waitFor({ timeout: 10000 })
await page.getByText('Signal tx').waitFor({ timeout: 10000 })
await page.getByText('Last signal outcome proof').waitFor({ timeout: 10000 })
await page.getByText('Settlement journal').waitFor({ timeout: 10000 })
await page.getByRole('button', { name: /Request paper auto-trade/i }).click()
await page.getByText('Accepted').first().waitFor({ timeout: 10000 })
await assertNoHorizontalOverflow(page, 'unlocked subscriber portal')
const unlockedScreenshot = path.join(screenshotRoot, 'subscriber-unlocked.png')
await writeFile(unlockedScreenshot, await page.screenshot({ fullPage: true }))

await page.getByText('Published signal detail').scrollIntoViewIfNeeded()
const signalProofScreenshot = path.join(screenshotRoot, 'signal-proof.png')
await writeFile(signalProofScreenshot, await page.screenshot())

await page.getByText('Last signal outcome proof').scrollIntoViewIfNeeded()
await page.getByText('Outcome tx').waitFor({ timeout: 10000 })
const performanceScreenshot = path.join(screenshotRoot, 'performance-ledger.png')
await writeFile(performanceScreenshot, await page.screenshot())

await page.getByText('Settlement journal').scrollIntoViewIfNeeded()
const settlementScreenshot = path.join(screenshotRoot, 'revenue-settlement.png')
await writeFile(settlementScreenshot, await page.screenshot())

await page.setViewportSize({ width: 390, height: 900 })
await page.goto(`${baseUrl}/?strategy=${strategyKey}&wallet=${unlockedWallet}&provenance=${provenanceHash}#performance`, { waitUntil: 'networkidle' })
await page.getByRole('heading', { name: 'Subscriber portal' }).waitFor({ timeout: 10000 })
await page.getByText('Published signal detail').waitFor({ timeout: 10000 })
await assertNoHorizontalOverflow(page, 'mobile subscriber portal')
const mobileScreenshot = path.join(screenshotRoot, 'subscriber-mobile.png')
await writeFile(mobileScreenshot, await page.screenshot({ fullPage: true }))

await browser.close()
await new Promise((resolve) => server.close(resolve))

if (consoleFailures.length > 0) {
  throw new Error(`Browser console errors during arc hackathon subscriber smoke:\n${consoleFailures.join('\n')}`)
}

console.log(JSON.stringify({
  blockedScreenshot,
  unlockedScreenshot,
  signalProofScreenshot,
  performanceScreenshot,
  settlementScreenshot,
  mobileScreenshot
}))

function allowedDecision(resourceKind, resourceId, permission) {
  return {
    allowed: true,
    reasonCode: 'ACCESS_ALLOWED',
    reason: 'Subscriber entitlement is active.',
    requiredPermission: permission,
    strategyKey,
    walletAddress: unlockedWallet,
    resourceKind,
    resourceId,
    tier: plan.tier,
    expiresAtUtc: '2026-05-19T00:00:00Z',
    evidenceTransactionHash: subscriptionTransactionHash
  }
}

function deniedDecision(resourceKind, resourceId, permission, walletAddress) {
  return {
    allowed: false,
    reasonCode: 'ACCESS_NOT_FOUND',
    reason: 'No active Arc subscription entitlement was found for this wallet and strategy.',
    requiredPermission: permission,
    strategyKey,
    walletAddress,
    resourceKind,
    resourceId,
    tier: null,
    expiresAtUtc: null,
    evidenceTransactionHash: null
  }
}

async function startStaticServer() {
  const mime = new Map([
    ['.html', 'text/html; charset=utf-8'],
    ['.js', 'text/javascript; charset=utf-8'],
    ['.css', 'text/css; charset=utf-8'],
    ['.svg', 'image/svg+xml']
  ])
  const server = createServer(async (request, response) => {
    const url = new URL(request.url ?? '/', 'http://127.0.0.1')
    const requested = url.pathname === '/' ? '/index.html' : url.pathname
    const root = path.resolve(distRoot)
    const candidate = path.resolve(root, `.${requested}`)
    const filePath = candidate.startsWith(root) ? candidate : path.join(root, 'index.html')
    try {
      const bytes = await readFile(filePath)
      response.writeHead(200, { 'content-type': mime.get(path.extname(filePath)) ?? 'application/octet-stream' })
      response.end(bytes)
    } catch {
      const bytes = await readFile(path.join(root, 'index.html'))
      response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' })
      response.end(bytes)
    }
  })

  await new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, '127.0.0.1', resolve)
  })
  return server
}

async function fulfillApiFixture(route) {
  const url = new URL(route.request().url())
  const result = await resolveApiPayload(url, route.request())
  await route.fulfill({
    status: result.status ?? 200,
    contentType: 'application/json',
    body: JSON.stringify(result.body)
  })
}

async function resolveApiPayload(url, request) {
  if (url.pathname === '/api/control-room/snapshot') return { body: snapshot }
  if (url.pathname === '/api/readiness') return { body: readiness }
  if (url.pathname === '/api/control-room/markets') {
    return { body: { timestampUtc: now, source: 'Phase11Demo', totalCount: markets.length, isComplete: true, categories: ['Macro'], markets } }
  }
  if (url.pathname === `/api/control-room/markets/${markets[0].marketId}`) {
    return { body: { timestampUtc: now, source: 'Phase11Demo', market: markets[0], orderBook: null, orders: [], positions: [], decisions: [], microstructure: [] } }
  }
  if (url.pathname === '/api/strategy-decisions') return { body: { timestampUtc: now, count: 0, limit: 12, decisions: [] } }
  if (url.pathname.startsWith('/api/strategy-parameters/')) return { body: { strategyId: strategyKey, configVersion: signalProofArtifact.configVersion || 'phase-11-demo', parameters: [], recentVersions: [] } }
  if (url.pathname === '/api/arc/access/plans') return { body: [plan] }
  if (url.pathname.startsWith('/api/arc/access/')) return { body: accessStatusFromPath(url.pathname) }
  if (url.pathname === '/api/arc/opportunities') return { body: [opportunitySummary] }
  if (url.pathname === `/api/arc/opportunities/${opportunityId}`) {
    const wallet = url.searchParams.get('walletAddress')?.toLowerCase()
    return wallet === unlockedWallet.toLowerCase()
      ? { body: opportunityDetail }
      : { status: 403, body: deniedDecision('arc-opportunity', opportunityId, 'ViewSignals', wallet) }
  }
  if (url.pathname === '/api/arc/signals') return { body: [signalSummary] }
  if (url.pathname === `/api/arc/signals/${signalId}`) {
    const wallet = url.searchParams.get('walletAddress')?.toLowerCase()
    return wallet === unlockedWallet.toLowerCase()
      ? { body: signalDetail }
      : { status: 403, body: deniedDecision('arc-signal', signalId, 'ViewSignals', wallet) }
  }
  if (url.pathname === '/api/arc/performance/agent') return { body: reputation }
  if (url.pathname === `/api/arc/performance/strategies/${strategyKey}`) {
    return { body: { ...reputation, scope: 'strategy', strategyId: strategyKey, terminalSignals: 5, confidenceCoverage: 0.82 } }
  }
  if (url.pathname === `/api/arc/performance/signals/${signalId}/outcome`) return { body: performanceOutcome }
  if (url.pathname === '/api/arc/revenue') return { body: [revenueSettlement] }
  if (url.pathname === `/api/arc/provenance/${provenanceHash}`) return { body: provenance }
  if (url.pathname === `/api/control-room/strategies/${strategyKey}/arc-paper-autotrade`) {
    let payload = {}
    try {
      payload = request.postDataJSON() ?? {}
    } catch {
      payload = {}
    }
    const wallet = String(payload.walletAddress ?? '').toLowerCase()
    const body = wallet === unlockedWallet.toLowerCase()
      ? {
          status: 'Accepted',
          message: `Strategy ${strategyKey} target state set to Running.`,
          accessDecision: allowedDecision('arc-paper-autotrade', strategyKey, 'RequestPaperAutoTrade'),
          command: { status: 'Accepted', commandMode: 'paper', message: `Strategy ${strategyKey} target state set to Running.`, snapshot }
        }
      : {
          status: 'AccessDenied',
          message: 'No active Arc subscription entitlement was found for this wallet and strategy.',
          accessDecision: deniedDecision('arc-paper-autotrade', strategyKey, 'RequestPaperAutoTrade', wallet),
          command: null
        }
    return { status: wallet === unlockedWallet.toLowerCase() ? 202 : 403, body }
  }
  return { body: {} }
}

function accessStatusFromPath(pathname) {
  const [, , , , encodedWallet, encodedStrategy] = pathname.split('/')
  const wallet = decodeURIComponent(encodedWallet ?? '')
  const normalized = wallet.toLowerCase()
  if (normalized === unlockedWallet.toLowerCase() && decodeURIComponent(encodedStrategy ?? '') === strategyKey) {
    return {
      walletAddress: unlockedWallet,
      strategyKey,
      statusCode: 'Active',
      hasAccess: true,
      reason: 'ACCESS_ACTIVE',
      permissions: plan.permissions,
      checkedAtUtc: now,
      tier: plan.tier,
      expiresAtUtc: '2026-05-19T00:00:00Z',
      sourceTransactionHash: subscriptionTransactionHash,
      syncedAtUtc: now,
      canViewSignals: true
    }
  }

  return {
    walletAddress: wallet,
    strategyKey,
    statusCode: 'NotFound',
    hasAccess: false,
    reason: 'ACCESS_NOT_FOUND',
    permissions: [],
    checkedAtUtc: now,
    tier: null,
    expiresAtUtc: null,
    sourceTransactionHash: null,
    syncedAtUtc: null,
    canViewSignals: false
  }
}

function readJsonArtifact(fileName, fallback = {}) {
  try {
    return JSON.parse(readFileSync(path.join(artifactRoot, fileName), 'utf8'))
  } catch {
    return fallback
  }
}

function mapSettlementShares(shares, grossAmountMicroUsdc) {
  if (Array.isArray(shares) && shares.length > 0) {
    return shares.map((share) => {
      const amountMicroUsdc = Number(share.amountMicroUsdc ?? 0)
      return {
        recipientKind: share.recipientKind,
        walletAddress: share.walletAddress,
        shareBps: Number(share.shareBps ?? 0),
        amountMicroUsdc,
        amountUsdc: amountMicroUsdc / 1_000_000
      }
    })
  }

  return [
    {
      recipientKind: 'AgentOwner',
      walletAddress: '0x1000000000000000000000000000000000000001',
      shareBps: 7000,
      amountMicroUsdc: Math.round(grossAmountMicroUsdc * 0.7),
      amountUsdc: Math.round(grossAmountMicroUsdc * 0.7) / 1_000_000
    },
    {
      recipientKind: 'StrategyAuthor',
      walletAddress: '0x2000000000000000000000000000000000000002',
      shareBps: 2000,
      amountMicroUsdc: Math.round(grossAmountMicroUsdc * 0.2),
      amountUsdc: Math.round(grossAmountMicroUsdc * 0.2) / 1_000_000
    },
    {
      recipientKind: 'Platform',
      walletAddress: '0x3000000000000000000000000000000000000003',
      shareBps: 1000,
      amountMicroUsdc: Math.round(grossAmountMicroUsdc * 0.1),
      amountUsdc: Math.round(grossAmountMicroUsdc * 0.1) / 1_000_000
    }
  ]
}

async function assertNoHorizontalOverflow(pageToCheck, label) {
  const overflow = await pageToCheck.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    viewportWidth: window.innerWidth
  }))
  if (overflow.scrollWidth > overflow.viewportWidth + 2) {
    throw new Error(`${label} has horizontal overflow: scrollWidth=${overflow.scrollWidth}, viewport=${overflow.viewportWidth}`)
  }
}

async function launchBrowser() {
  try {
    return await chromium.launch()
  } catch (error) {
    const candidates = [
      'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
      'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
      'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe'
    ]

    for (const executablePath of candidates) {
      try {
        await access(executablePath)
        return await chromium.launch({ executablePath })
      } catch {
        // Try the next locally installed browser.
      }
    }

    throw error
  }
}
