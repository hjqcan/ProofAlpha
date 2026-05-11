import { chromium } from '@playwright/test'
import { access, mkdir } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const webAppRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5173'
const outDir = path.resolve(process.argv[3] ?? path.join(webAppRoot, '..', 'artifacts', 'webapp', 'phase-6'))
const now = '2026-05-03T12:00:00Z'
const runSessionId = 'phase6-run-session'

const markets = [
  {
    marketId: 'pm-election',
    conditionId: 'condition-election',
    name: 'Will the election fixture resolve yes?',
    category: 'Politics',
    status: 'Active',
    yesPrice: 0.48,
    noPrice: 0.52,
    liquidity: 125000,
    volume24h: 85000,
    expiresAtUtc: '2026-05-18T12:00:00Z',
    signalScore: 0.83,
    slug: 'fixture-election',
    description: 'Liquid control-room fixture with strong signal and tight spread.',
    acceptingOrders: true,
    tokens: [
      { tokenId: 'token-election-yes', outcome: 'YES', price: 0.48, winner: null },
      { tokenId: 'token-election-no', outcome: 'NO', price: 0.52, winner: null }
    ],
    tags: ['fixture'],
    spread: 0.02,
    source: 'PlaywrightFixture',
    rankScore: 0.91,
    rankReason: 'strong signal; deep liquidity; active 24h volume; accepting orders; expires in 15d',
    unsuitableReasons: []
  },
  {
    marketId: 'pm-paused',
    conditionId: 'condition-paused',
    name: 'Will the paused fixture become tradable?',
    category: 'Macro',
    status: 'Paused',
    yesPrice: 0.34,
    noPrice: 0.66,
    liquidity: 650,
    volume24h: 120,
    expiresAtUtc: '2026-05-08T12:00:00Z',
    signalScore: 0.18,
    slug: 'fixture-paused',
    description: 'Shows unsuitable reasons while preserving market state.',
    acceptingOrders: false,
    tokens: [
      { tokenId: 'token-paused-yes', outcome: 'YES', price: 0.34, winner: null },
      { tokenId: 'token-paused-no', outcome: 'NO', price: 0.66, winner: null }
    ],
    tags: ['fixture'],
    spread: 0.11,
    source: 'PlaywrightFixture',
    rankScore: 0.12,
    rankReason: 'weak signal; thin liquidity; quiet 24h volume; not accepting orders; expires in 5d',
    unsuitableReasons: [
      'Market status is Paused.',
      'Market is not accepting orders.',
      'Liquidity is below 1000.',
      '24h volume is below 500.',
      'Signal score is below 0.25.'
    ]
  },
  {
    marketId: 'pm-sports',
    conditionId: 'condition-sports',
    name: 'Will the sports fixture settle above the line?',
    category: 'Sports',
    status: 'Active',
    yesPrice: 0.57,
    noPrice: 0.43,
    liquidity: 18000,
    volume24h: 9500,
    expiresAtUtc: '2026-05-20T12:00:00Z',
    signalScore: 0.55,
    slug: 'fixture-sports',
    description: 'Moderate watchlist fixture for grid coverage.',
    acceptingOrders: true,
    tokens: [
      { tokenId: 'token-sports-yes', outcome: 'YES', price: 0.57, winner: null },
      { tokenId: 'token-sports-no', outcome: 'NO', price: 0.43, winner: null }
    ],
    tags: ['fixture'],
    spread: 0.04,
    source: 'PlaywrightFixture',
    rankScore: 0.67,
    rankReason: 'moderate signal; usable liquidity; some 24h volume; accepting orders; expires in 17d',
    unsuitableReasons: []
  }
]

const strategies = [
  {
    strategyId: 'dual_leg_arbitrage',
    name: 'Dual-leg arbitrage',
    state: 'Running',
    enabled: true,
    configVersion: 'cfg-phase6',
    desiredState: 'Running',
    activeMarkets: 3,
    cycleCount: 128,
    snapshotsProcessed: 640,
    channelBacklog: 2,
    isKillSwitchBlocked: false,
    lastHeartbeatUtc: '2026-05-03T11:59:45Z',
    lastDecisionAtUtc: '2026-05-03T11:59:30Z',
    lastError: null,
    blockedReason: null,
    parameters: [
      { name: 'max_notional', value: '2500' },
      { name: 'min_signal', value: '0.32' }
    ]
  },
  {
    strategyId: 'liquidity_maker',
    name: 'Liquidity maker',
    state: 'Paused',
    enabled: true,
    configVersion: 'cfg-phase6',
    desiredState: 'Paused',
    activeMarkets: 1,
    cycleCount: 44,
    snapshotsProcessed: 210,
    channelBacklog: 0,
    isKillSwitchBlocked: false,
    lastHeartbeatUtc: '2026-05-03T11:58:00Z',
    lastDecisionAtUtc: '2026-05-03T11:57:20Z',
    lastError: null,
    blockedReason: { kind: 'StaleData', code: 'STALE_BOOK', message: 'Paused until order-book freshness recovers.' },
    parameters: [
      { name: 'quote_size', value: '120' },
      { name: 'max_spread', value: '0.04' }
    ]
  }
]

const orders = [
  {
    clientOrderId: 'ord-phase6-0001-long-identifier',
    strategyId: 'dual_leg_arbitrage',
    marketId: 'pm-election',
    side: 'Buy',
    outcome: 'YES',
    price: 0.48,
    quantity: 120,
    filledQuantity: 80,
    status: 'PartiallyFilled',
    updatedAtUtc: '2026-05-03T11:59:20Z'
  },
  {
    clientOrderId: 'ord-phase6-0002',
    strategyId: 'liquidity_maker',
    marketId: 'pm-sports',
    side: 'Sell',
    outcome: 'NO',
    price: 0.43,
    quantity: 60,
    filledQuantity: 60,
    status: 'Filled',
    updatedAtUtc: '2026-05-03T11:58:40Z'
  }
]

const positions = [
  {
    marketId: 'pm-election',
    outcome: 'YES',
    quantity: 80,
    averageCost: 0.47,
    notional: 37.6,
    realizedPnl: 8.2,
    markPrice: 0.48,
    unrealizedPnl: 0.8,
    totalPnl: 9.0,
    returnPct: 0.024,
    markSource: 'PlaywrightFixture',
    updatedAtUtc: '2026-05-03T11:59:20Z'
  },
  {
    marketId: 'pm-sports',
    outcome: 'NO',
    quantity: -60,
    averageCost: 0.43,
    notional: 25.8,
    realizedPnl: -1.1,
    markPrice: 0.43,
    unrealizedPnl: -0.4,
    totalPnl: -1.5,
    returnPct: -0.018,
    markSource: 'PlaywrightFixture',
    updatedAtUtc: '2026-05-03T11:58:40Z'
  }
]

const decisions = [
  {
    strategyId: 'dual_leg_arbitrage',
    action: 'Quote',
    marketId: 'pm-election',
    reason: 'Signal and liquidity passed paper routing checks.',
    createdAtUtc: '2026-05-03T11:59:30Z'
  },
  {
    strategyId: 'liquidity_maker',
    action: 'Hold',
    marketId: 'pm-paused',
    reason: 'Paused market and stale book blocked routing.',
    createdAtUtc: '2026-05-03T11:58:15Z'
  }
]

const snapshot = {
  timestampUtc: now,
  dataMode: 'deterministic',
  commandMode: 'paper',
  process: {
    apiStatus: 'Ready',
    environment: 'Playwright',
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
    openOrders: orders.length,
    unhedgedExposures: 1,
    limits: [
      { name: 'Capital utilization', current: 10, limit: 35, unit: '%', state: 'good' },
      { name: 'Open orders', current: orders.length, limit: 20, unit: 'orders', state: 'good' }
    ]
  },
  metrics: [
    { label: 'Running strategies', value: '3', delta: 'paper', tone: 'good' },
    { label: 'Market liquidity', value: '$143K', delta: 'fixture', tone: 'neutral' }
  ],
  strategies,
  markets,
  orders,
  positions,
  decisions,
  timeline: [],
  capitalCurve: [],
  latencyCurve: []
}

const staleOrderBook = {
  marketId: 'pm-election',
  tokenId: 'token-election-yes',
  outcome: 'YES',
  lastUpdatedUtc: '2026-05-03T11:58:50Z',
  bestBidPrice: 0.47,
  bestBidSize: 42,
  bestAskPrice: 0.5,
  bestAskSize: 38,
  spread: 0.03,
  midpoint: 0.485,
  totalBidSize: 300,
  totalAskSize: 280,
  imbalancePct: 3.45,
  maxLevelNotional: 150,
  source: 'PlaywrightFixture',
  freshness: {
    status: 'Stale',
    ageSeconds: 70,
    freshSeconds: 5,
    staleSeconds: 30,
    message: 'Order book is stale by 70s; do not treat it as live.'
  },
  bids: [
    { level: 1, price: 0.47, size: 42, notional: 19.74, depthPct: 82 },
    { level: 2, price: 0.46, size: 64, notional: 29.44, depthPct: 61 }
  ],
  asks: [
    { level: 1, price: 0.5, size: 38, notional: 19, depthPct: 79 },
    { level: 2, price: 0.51, size: 71, notional: 36.21, depthPct: 66 }
  ]
}

const readiness = {
  contractVersion: 'phase6-smoke',
  checkedAtUtc: now,
  status: 'Ready',
  checks: [
    {
      id: 'database',
      category: 'Database',
      requirement: 'Required',
      status: 'Ready',
      source: 'PlaywrightFixture',
      lastCheckedAtUtc: now,
      summary: 'Paper database is reachable.',
      remediationHint: 'No action required for this fixture.',
      evidence: { mode: 'paper' }
    },
    {
      id: 'market-data',
      category: 'MarketData',
      requirement: 'Required',
      status: 'Ready',
      source: 'PlaywrightFixture',
      lastCheckedAtUtc: now,
      summary: 'Market discovery and order-book fixtures are loaded.',
      remediationHint: 'No action required for this fixture.',
      evidence: { markets: String(markets.length) }
    }
  ],
  capabilities: [
    { capability: 'PaperTrading', status: 'Ready', blockingCheckIds: [], summary: 'Paper controls are available.' },
    { capability: 'LiveTrading', status: 'Blocked', blockingCheckIds: ['paper-gate'], summary: 'Live remains blocked in visual smoke.' }
  ]
}

const incidentActions = {
  generatedAtUtc: now,
  commandMode: 'paper',
  runbookPath: 'docs/operations/autotrade-incident-runbook.md',
  actions: [
    {
      id: 'hard-stop',
      label: 'Activate hard stop',
      category: 'Risk',
      scope: 'Global',
      method: 'POST',
      path: '/api/control-room/risk/kill-switch',
      enabled: false,
      disabledReason: 'Control room is read-only',
      confirmationText: 'CONFIRM',
      result: 'Blocks strategy routing and order creation.'
    },
    {
      id: 'cancel-open-orders',
      label: 'Cancel open orders',
      category: 'Execution',
      scope: 'Strategy or global',
      method: 'POST',
      path: '/api/control-room/incidents/cancel-open-orders',
      enabled: true,
      disabledReason: null,
      confirmationText: 'CONFIRM',
      result: 'Requests cancellation for current open orders.'
    }
  ]
}

const strategyDecisions = {
  timestampUtc: now,
  count: 2,
  limit: 12,
  decisions: [
    {
      decisionId: 'decision-phase6-1',
      strategyId: 'dual_leg_arbitrage',
      action: 'Quote',
      reason: 'Signal and liquidity passed paper routing checks.',
      marketId: 'pm-election',
      createdAtUtc: '2026-05-03T11:59:30Z',
      configVersion: 'cfg-phase6',
      correlationId: 'corr-phase6-1',
      executionMode: 'Paper'
    },
    {
      decisionId: 'decision-phase6-2',
      strategyId: 'dual_leg_arbitrage',
      action: 'Skip',
      reason: 'Paused market did not pass routing checks.',
      marketId: 'pm-paused',
      createdAtUtc: '2026-05-03T11:58:30Z',
      configVersion: 'cfg-phase6',
      correlationId: 'corr-phase6-2',
      executionMode: 'Paper'
    }
  ]
}

const strategyParameters = {
  strategyId: 'dual_leg_arbitrage',
  configVersion: 'cfg-phase6',
  parameters: [
    { name: 'max_notional', value: '2500', type: 'decimal', editable: true },
    { name: 'min_signal', value: '0.32', type: 'decimal', editable: true },
    { name: 'max_spread', value: '0.05', type: 'decimal', editable: false }
  ],
  recentVersions: [
    {
      versionId: 'version-phase6-1',
      strategyId: 'dual_leg_arbitrage',
      configVersion: 'cfg-phase6',
      previousConfigVersion: 'cfg-phase5',
      changeType: 'Update',
      source: 'PlaywrightFixture',
      actor: 'operator',
      reason: 'Tighten paper routing limits for phase 6.',
      createdAtUtc: '2026-05-03T11:55:00Z',
      diff: [
        { name: 'min_signal', previousValue: '0.28', nextValue: '0.32' }
      ],
      rollbackSourceVersionId: null
    }
  ]
}

const exposureResponse = {
  generatedAtUtc: now,
  count: 1,
  limit: 20,
  query: {
    strategyId: null,
    marketId: null,
    riskEventId: null,
    fromUtc: null,
    toUtc: null,
    limit: 20
  },
  exposures: [
    {
      evidenceId: 'exposure-phase6-1',
      strategyId: 'dual_leg_arbitrage',
      marketId: 'pm-election',
      tokenId: 'token-election-yes',
      hedgeTokenId: 'token-election-no',
      outcome: 'YES',
      side: 'Long',
      quantity: 80,
      price: 0.48,
      notional: 38.4,
      durationSeconds: 42,
      startedAtUtc: '2026-05-03T11:58:58Z',
      endedAtUtc: null,
      timeoutSeconds: 120,
      hedgeState: 'Open',
      mitigationResult: 'Monitoring under paper limits',
      source: 'PlaywrightFixture'
    }
  ]
}

const runReport = {
  generatedAtUtc: now,
  reportStatus: 'Complete',
  completenessNotes: ['All phase-6 fixture evidence is deterministic.'],
  session: {
    sessionId: runSessionId,
    executionMode: 'Paper',
    configVersion: 'cfg-phase6',
    strategies: ['dual_leg_arbitrage', 'liquidity_maker'],
    riskProfileJson: '{"mode":"paper"}',
    operatorSource: 'PlaywrightFixture',
    startedAtUtc: '2026-05-03T10:00:00Z',
    stoppedAtUtc: '2026-05-03T12:00:00Z',
    stopReason: 'visual-smoke-complete',
    isActive: false,
    recovered: false
  },
  summary: {
    decisionCount: 18,
    orderEventCount: 8,
    orderCount: 5,
    tradeCount: 3,
    positionCount: 2,
    riskEventCount: 1,
    filledOrderEventCount: 3,
    rejectedOrderEventCount: 0,
    totalBuyNotional: 1200,
    totalSellNotional: 1240,
    totalFees: 1.2,
    grossPnl: 25,
    netPnl: 23.8
  },
  strategyBreakdown: [
    {
      strategyId: 'dual_leg_arbitrage',
      decisionCount: 12,
      orderEventCount: 6,
      orderCount: 4,
      tradeCount: 2,
      riskEventCount: 1,
      totalBuyNotional: 900,
      totalSellNotional: 930,
      totalFees: 0.8,
      netPnl: 18.2,
      realizedPnl: 16.4,
      unrealizedPnl: 1.8,
      estimatedSlippage: -0.7,
      averageDecisionToFillLatencyMs: 420,
      staleDataEventCount: 1,
      unhedgedExposureNotional: 38.4,
      unhedgedExposureSeconds: 42
    }
  ],
  marketBreakdown: [
    {
      marketId: 'pm-election',
      decisionCount: 10,
      orderEventCount: 5,
      orderCount: 3,
      tradeCount: 2,
      positionCount: 1,
      totalBuyNotional: 700,
      totalSellNotional: 730,
      netPnl: 16.7,
      realizedPnl: 15.2,
      unrealizedPnl: 1.5,
      estimatedSlippage: -0.5,
      averageDecisionToFillLatencyMs: 390,
      staleDataEventCount: 1,
      unhedgedExposureNotional: 38.4,
      unhedgedExposureSeconds: 42
    }
  ],
  riskEvents: [
    {
      id: 'risk-phase6-1',
      code: 'STALE_BOOK',
      severity: 'Warning',
      message: 'Order book freshness was stale during smoke.',
      strategyId: 'dual_leg_arbitrage',
      contextJson: '{"source":"visual-smoke"}',
      createdAtUtc: '2026-05-03T11:59:00Z',
      marketId: 'pm-election'
    }
  ],
  notableIncidents: [
    {
      timestampUtc: '2026-05-03T11:59:00Z',
      source: 'PlaywrightFixture',
      severity: 'Warning',
      code: 'STALE_BOOK',
      message: 'Stale book evidence was captured.',
      strategyId: 'dual_leg_arbitrage',
      marketId: 'pm-election',
      evidenceId: 'risk-phase6-1'
    }
  ],
  evidenceLinks: {
    decisionIds: ['decision-phase6-1'],
    orderEventIds: ['order-event-phase6-1'],
    orderIds: ['ord-phase6-0001-long-identifier'],
    tradeIds: ['trade-phase6-1'],
    positionIds: ['position-phase6-1'],
    riskEventIds: ['risk-phase6-1']
  },
  exportReferences: {
    jsonApi: '/api/run-reports/phase6-run-session',
    jsonCli: 'dotnet run -- export run-report --format json',
    csvCli: 'dotnet run -- export run-report --format csv',
    csvTables: ['orders', 'positions', 'decisions']
  },
  attribution: {
    pnl: {
      realizedPnl: 16.4,
      unrealizedPnl: 1.8,
      fees: 0.8,
      grossPnl: 19,
      netPnl: 18.2,
      realizedPnlSource: 'PlaywrightFixture',
      unrealizedPnlSource: 'PlaywrightFixture',
      feeSource: 'PlaywrightFixture',
      markSource: 'PlaywrightFixture',
      notes: ['PnL values are fixture data for visual coverage.']
    },
    slippage: {
      estimatedSlippage: -0.7,
      adverseSlippage: -1.1,
      favorablePriceImprovement: 0.4,
      source: 'PlaywrightFixture',
      tradeCountWithEstimate: 2,
      tradeCountWithoutEstimate: 0,
      evidenceIds: ['trade-phase6-1'],
      notes: []
    },
    latency: {
      averageDecisionToFillLatencyMs: 420,
      p95DecisionToFillLatencyMs: 620,
      averageAcceptedToFillLatencyMs: 210,
      fillEventCountWithDecisionLatency: 2,
      fillEventCountWithAcceptedLatency: 2,
      evidenceIds: ['fill-phase6-1'],
      notes: []
    },
    staleData: {
      eventCount: 1,
      estimatedPnlContribution: null,
      source: 'PlaywrightFixture',
      evidenceIds: ['risk-phase6-1'],
      notes: ['Stale book was blocked from being treated as live.']
    },
    unhedgedExposure: {
      eventCount: 1,
      totalNotional: 38.4,
      totalDurationSeconds: 42,
      averageDurationSeconds: 42,
      exposures: exposureResponse.exposures,
      notes: []
    },
    strategyTotalsReconcile: true,
    marketTotalsReconcile: true,
    reconciliationNotes: ['Strategy and market totals reconcile in fixture data.']
  }
}

const promotionChecklist = {
  sessionId: runSessionId,
  generatedAtUtc: now,
  overallStatus: 'Passed',
  canConsiderLive: false,
  liveArmingUnchanged: true,
  criteria: [
    {
      id: 'paper-evidence',
      name: 'Paper evidence captured',
      status: 'Passed',
      reason: 'Run report and replay references are available.',
      evidenceIds: ['decision-phase6-1', 'risk-phase6-1'],
      residualRisks: []
    },
    {
      id: 'live-arming',
      name: 'Live arming unchanged',
      status: 'Passed',
      reason: 'Checklist evaluation did not mutate live arming state.',
      evidenceIds: [],
      residualRisks: []
    }
  ],
  residualRisks: ['Live arming remains a separate operator-controlled workflow.']
}

await mkdir(outDir, { recursive: true })
const browser = await launchBrowser()
const page = await browser.newPage({ viewport: { width: 1440, height: 980 } })
const consoleFailures = []

page.on('console', (message) => {
  if (message.type() === 'error') {
    if (message.text().includes('503 (Service Unavailable)')) {
      return
    }

    consoleFailures.push(message.text())
  }
})
page.on('pageerror', (error) => consoleFailures.push(error.message))
await page.addInitScript(() => window.localStorage.setItem('autotrade.locale', 'zh-CN'))
await page.route('http://localhost:5080/api/**', fulfillApiFixture)
await page.route('http://127.0.0.1:5080/api/**', fulfillApiFixture)

await captureDesktopViews(page)
await captureMobileViews(page)
await browser.close()

if (consoleFailures.length > 0) {
  throw new Error(`Browser console errors during phase-6 smoke:\n${consoleFailures.join('\n')}`)
}

console.log(`Phase 6 visual smoke screenshots written to ${outDir}`)

async function captureDesktopViews(page) {
  await page.setViewportSize({ width: 1440, height: 980 })

  await gotoApp(page, '/#markets')
  await waitForVisible(page, '.market-rank-band', 'debug-market-discovery-timeout.png')
  await assertNoHorizontalOverflow(page, 'desktop markets')
  await screenshot(page, 'market-discovery-ranking-desktop.png')

  await gotoApp(page, '/#trade')
  await waitForVisible(page, '.book-freshness-strip.danger', 'debug-trade-timeout.png')
  await assertNoHorizontalOverflow(page, 'desktop trade')
  await screenshot(page, 'trade-desktop.png')
  await screenshot(page, 'orderbook-stale-state-desktop.png')

  await page.locator('.book-panel .icon-button').first().click()
  await waitForVisible(page, '.book-state-panel.error', 'debug-orderbook-error-timeout.png')
  await screenshot(page, 'orderbook-error-state-desktop.png')

  await gotoApp(page, '/#ops')
  await waitForVisible(page, '.readiness-check', 'debug-ops-readiness-timeout.png')
  await waitForVisible(page, '.incident-action-row', 'debug-ops-incident-timeout.png')
  await assertNoHorizontalOverflow(page, 'desktop ops')
  await screenshot(page, 'ops-desktop.png')

  await gotoApp(page, '/?strategy=dual_leg_arbitrage#ops')
  await waitForVisible(page, '.strategy-detail-panel .strategy-state-grid', 'debug-strategy-detail-timeout.png')
  await assertNoHorizontalOverflow(page, 'desktop strategy detail')
  await screenshot(page, 'strategy-detail-desktop.png')

  await gotoApp(page, '/#activity')
  await waitForVisible(page, '.decision-item', 'debug-activity-timeout.png')
  await assertNoHorizontalOverflow(page, 'desktop activity')
  await screenshot(page, 'activity-desktop.png')

  await gotoApp(page, `/?runSessionId=${runSessionId}#reports`)
  await waitForVisible(page, '.report-gate', 'debug-report-gate-timeout.png')
  await waitForVisible(page, '.criterion-row', 'debug-report-criterion-timeout.png')
  await assertNoHorizontalOverflow(page, 'desktop run report')
  await screenshot(page, 'run-report-desktop.png')
}

async function captureMobileViews(page) {
  await page.setViewportSize({ width: 390, height: 900 })

  await gotoApp(page, '/#markets')
  await waitForVisible(page, '.market-rank-band', 'debug-market-mobile-timeout.png')
  await assertNoHorizontalOverflow(page, 'mobile markets')
  await screenshot(page, 'market-discovery-ranking-mobile.png')

  await gotoApp(page, '/#trade')
  await waitForVisible(page, '.book-freshness-strip.danger', 'debug-trade-mobile-timeout.png')
  await assertNoHorizontalOverflow(page, 'mobile trade')
  await screenshot(page, 'trade-mobile.png')
}

async function fulfillApiFixture(route) {
  const url = new URL(route.request().url())
  const parts = url.pathname.split('/').filter(Boolean)

  if (parts.length === 5 && parts[0] === 'api' && parts[1] === 'control-room' && parts[2] === 'markets' && parts[4] === 'order-book') {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({
        title: 'Order book stream unavailable',
        detail: 'Order book stream unavailable for smoke test'
      })
    })
    return
  }

  const payload = resolveApiPayload(url, parts)
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(payload)
  })
}

function resolveApiPayload(url, parts) {
  if (url.pathname === '/api/control-room/snapshot') {
    return snapshot
  }

  if (url.pathname === '/api/readiness') {
    return readiness
  }

  if (url.pathname === '/api/control-room/markets') {
    return {
      timestampUtc: now,
      source: 'PlaywrightFixture',
      totalCount: markets.length,
      isComplete: true,
      categories: ['Macro', 'Politics', 'Sports'],
      markets
    }
  }

  if (parts.length === 4 && parts[0] === 'api' && parts[1] === 'control-room' && parts[2] === 'markets') {
    const market = markets.find((item) => item.marketId === parts[3]) ?? markets[0]
    return {
      timestampUtc: now,
      source: 'PlaywrightFixture',
      market,
      orderBook: market.marketId === 'pm-election' ? staleOrderBook : null,
      orders: orders.filter((order) => order.marketId === market.marketId),
      positions: positions.filter((position) => position.marketId === market.marketId),
      decisions: decisions.filter((decision) => decision.marketId === market.marketId),
      microstructure: market.marketId === 'pm-election'
        ? [
            { label: 'Book freshness', value: 'Stale', delta: staleOrderBook.freshness.message, tone: 'watch' }
          ]
        : []
    }
  }

  if (url.pathname === '/api/strategy-decisions') {
    return strategyDecisions
  }

  if (parts.length >= 3 && parts[0] === 'api' && parts[1] === 'strategy-parameters') {
    return { ...strategyParameters, strategyId: parts[2] }
  }

  if (url.pathname === '/api/control-room/incidents/actions') {
    return incidentActions
  }

  if (url.pathname === '/api/control-room/risk/unhedged-exposures') {
    return exposureResponse
  }

  if (parts.length === 3 && parts[0] === 'api' && parts[1] === 'run-reports') {
    return runReport
  }

  if (parts.length === 4 && parts[0] === 'api' && parts[1] === 'run-reports' && parts[3] === 'promotion-checklist') {
    return promotionChecklist
  }

  return {}
}

async function gotoApp(page, pathSuffix) {
  await page.goto(new URL(pathSuffix, baseUrl).toString(), { waitUntil: 'networkidle' })
  await page.reload({ waitUntil: 'networkidle' })
}

async function screenshot(page, filename) {
  await page.screenshot({ path: path.join(outDir, filename), fullPage: true })
}

async function waitForVisible(page, selector, debugFilename) {
  try {
    await page.locator(selector).first().waitFor({ timeout: 10_000 })
  } catch (error) {
    await screenshot(page, debugFilename)
    if (consoleFailures.length > 0) {
      console.error(`Browser console errors before ${selector} became visible:`)
      for (const failure of consoleFailures) {
        console.error(`- ${failure}`)
      }
    }
    throw error
  }
}

async function assertNoHorizontalOverflow(page, label) {
  const overflow = await page.evaluate(() => {
    const documentElement = document.documentElement
    const scrollWidth = documentElement.scrollWidth
    const viewportWidth = window.innerWidth
    if (scrollWidth <= viewportWidth + 2) {
      return { scrollWidth, viewportWidth, offenders: [] }
    }

    const offenders = Array.from(document.body.querySelectorAll('*'))
      .map((element) => {
        const rect = element.getBoundingClientRect()
        return {
          className: element.className,
          tagName: element.tagName,
          right: Math.round(rect.right),
          width: Math.round(rect.width)
        }
      })
      .filter((item) => item.right > viewportWidth + 2 && item.width > 0)
      .sort((left, right) => right.right - left.right)
      .slice(0, 8)

    return { scrollWidth, viewportWidth, offenders }
  })

  if (overflow.scrollWidth > overflow.viewportWidth + 2) {
    throw new Error(
      `${label} has horizontal overflow: scrollWidth=${overflow.scrollWidth}, viewport=${overflow.viewportWidth}, offenders=${JSON.stringify(overflow.offenders)}`
    )
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
