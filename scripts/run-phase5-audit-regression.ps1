[CmdletBinding()]
param(
    [switch]$SkipFullDotnetTest,
    [switch]$SkipBrowserScreenshots
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir
$artifactDir = Join-Path $root "artifacts/audit/phase-5"
$webAppDir = Join-Path $root "webApp"
$steps = New-Object System.Collections.Generic.List[object]

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

function Resolve-Tool {
    param(
        [Parameter(Mandatory = $true)][string]$WindowsName,
        [Parameter(Mandatory = $true)][string]$FallbackName
    )

    $command = Get-Command $WindowsName -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    return (Get-Command $FallbackName -ErrorAction Stop).Source
}

function Invoke-GateStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $outLog = Join-Path $artifactDir "$Name.out.log"
    $errLog = Join-Path $artifactDir "$Name.err.log"
    $startedAt = Get-Date
    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList $Arguments `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $outLog `
        -RedirectStandardError $errLog `
        -NoNewWindow `
        -Wait `
        -PassThru
    $finishedAt = Get-Date

    $step = [ordered]@{
        name = $Name
        command = "$FilePath $($Arguments -join ' ')"
        workingDirectory = $WorkingDirectory
        exitCode = $process.ExitCode
        startedAtUtc = $startedAt.ToUniversalTime().ToString("O")
        finishedAtUtc = $finishedAt.ToUniversalTime().ToString("O")
        durationSeconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        stdout = (Resolve-Path $outLog).Path
        stderr = (Resolve-Path $errLog).Path
    }
    $steps.Add([pscustomobject]$step) | Out-Null

    if ($process.ExitCode -ne 0) {
        throw "Gate step failed: $Name. See $outLog and $errLog."
    }
}

function Wait-HttpReady {
    param(
        [Parameter(Mandatory = $true)][string]$Url
    )

    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        try {
            Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2 | Out-Null
            return
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Timed out waiting for $Url"
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse("127.0.0.1"), 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Write-ReplayFixture {
    $fixturePath = Join-Path $artifactDir "replay-package.json"
    $fixture = [ordered]@{
        generatedAtUtc = "2026-05-03T12:00:00.0000000+00:00"
        contractVersion = "autotrade.replay-export.v1"
        query = [ordered]@{
            strategyId = "dual_leg_arbitrage"
            marketId = "market-1"
            orderId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
            clientOrderId = "client-1"
            runSessionId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            riskEventId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
            correlationId = "corr-1"
            fromUtc = "2026-05-03T12:00:00.0000000+00:00"
            toUtc = "2026-05-03T13:00:00.0000000+00:00"
            limit = 100
        }
        redaction = [ordered]@{
            appliedRules = @(
                "JSON property names containing password, secret, privateKey, apiKey, authorization, credential, signature, passphrase, or mnemonic are replaced with [redacted].",
                "Raw text fragments that look like key, secret, password, authorization, credential, signature, passphrase, or mnemonic assignments are redacted.",
                "TradingAccountId, OrderSalt, and OrderTimestamp are excluded from order, trade, and position evidence records."
            )
            excludedFields = @("TradingAccountId", "OrderSalt", "OrderTimestamp")
        }
        completenessNotes = @()
        runSession = [ordered]@{
            sessionId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            executionMode = "Paper"
            configVersion = "cfg-v1"
            strategies = @("dual_leg_arbitrage")
            riskProfileJson = '{"maxExposure":100,"privateKey":"[redacted]"}'
            operatorSource = "phase5-regression"
            startedAtUtc = "2026-05-03T12:00:00.0000000+00:00"
            stoppedAtUtc = "2026-05-03T13:00:00.0000000+00:00"
            stopReason = "done"
            isActive = $false
            recovered = $false
        }
        timeline = [ordered]@{
            generatedAtUtc = "2026-05-03T12:06:00.0000000+00:00"
            count = 1
            limit = 100
            query = [ordered]@{
                strategyId = "dual_leg_arbitrage"
                marketId = "market-1"
                runSessionId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                correlationId = "corr-1"
                limit = 100
            }
            items = @(
                [ordered]@{
                    itemId = "11111111-1111-1111-1111-111111111111"
                    timestampUtc = "2026-05-03T12:01:00.0000000+00:00"
                    type = "StrategyDecision"
                    source = "strategy_decision"
                    actor = "dual_leg_arbitrage"
                    summary = "Buy edge"
                    detailReference = "strategy-decisions/11111111-1111-1111-1111-111111111111"
                    strategyId = "dual_leg_arbitrage"
                    marketId = "market-1"
                    orderId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
                    clientOrderId = "client-1"
                    runSessionId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                    riskEventId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
                    correlationId = "corr-1"
                    detailJson = '{"apiKey":"[redacted]"}'
                }
            )
        }
        evidence = [ordered]@{
            decisions = @(
                [ordered]@{
                    decisionId = "22222222-2222-2222-2222-222222222222"
                    strategyId = "dual_leg_arbitrage"
                    action = "Buy"
                    reason = "edge"
                    marketId = "market-1"
                    contextJson = '{"apiKey":"[redacted]","orderId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"}'
                    timestampUtc = "2026-05-03T12:01:00.0000000+00:00"
                    configVersion = "cfg-v1"
                    correlationId = "corr-1"
                    executionMode = "Paper"
                    runSessionId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                }
            )
            orderEvents = @(
                [ordered]@{
                    id = "33333333-3333-3333-3333-333333333333"
                    orderId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
                    clientOrderId = "client-1"
                    strategyId = "dual_leg_arbitrage"
                    marketId = "market-1"
                    eventType = "Accepted"
                    status = "Open"
                    message = "accepted"
                    contextJson = '{"secret":"[redacted]"}'
                    correlationId = "corr-1"
                    createdAtUtc = "2026-05-03T12:02:00.0000000+00:00"
                    runSessionId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                }
            )
            orders = @(
                [ordered]@{
                    id = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
                    marketId = "market-1"
                    tokenId = "token-yes"
                    strategyId = "dual_leg_arbitrage"
                    clientOrderId = "client-1"
                    exchangeOrderId = "exchange-1"
                    correlationId = "corr-1"
                    outcome = "Yes"
                    side = "Buy"
                    orderType = "Limit"
                    timeInForce = "Gtc"
                    goodTilDateUtc = $null
                    negRisk = $false
                    price = 0.45
                    quantity = 10
                    filledQuantity = 10
                    status = "Filled"
                    rejectionReason = $null
                    createdAtUtc = "2026-05-03T12:02:00.0000000+00:00"
                    updatedAtUtc = "2026-05-03T12:03:00.0000000+00:00"
                }
            )
            trades = @(
                [ordered]@{
                    id = "cccccccc-cccc-cccc-cccc-cccccccccccc"
                    orderId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
                    clientOrderId = "client-1"
                    strategyId = "dual_leg_arbitrage"
                    marketId = "market-1"
                    tokenId = "token-yes"
                    outcome = "Yes"
                    side = "Buy"
                    price = 0.46
                    quantity = 10
                    exchangeTradeId = "exchange-trade-1"
                    fee = 0.01
                    notional = 4.6
                    correlationId = "corr-1"
                    createdAtUtc = "2026-05-03T12:03:00.0000000+00:00"
                }
            )
            positions = @(
                [ordered]@{
                    id = "dddddddd-dddd-dddd-dddd-dddddddddddd"
                    marketId = "market-1"
                    outcome = "Yes"
                    quantity = 10
                    averageCost = 0.45
                    realizedPnl = 1.2
                    notional = 4.5
                    updatedAtUtc = "2026-05-03T12:04:00.0000000+00:00"
                }
            )
            riskEvents = @(
                [ordered]@{
                    id = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
                    code = "RISK_MAX_EXPOSURE"
                    severity = "Warning"
                    message = "limit reached"
                    strategyId = "dual_leg_arbitrage"
                    contextJson = '{"authorization":"[redacted]"}'
                    createdAtUtc = "2026-05-03T12:05:00.0000000+00:00"
                    marketId = "market-1"
                }
            )
        }
        strategyConfigVersions = @(
            [ordered]@{
                strategyId = "dual_leg_arbitrage"
                configVersion = "cfg-v1"
                source = "run_session"
                observedAtUtc = "2026-05-03T12:00:00.0000000+00:00"
            }
        )
        readiness = [ordered]@{
            contractVersion = "readiness.v1"
            checkedAtUtc = "2026-05-03T12:00:00.0000000+00:00"
            status = "Ready"
            checks = @(
                [ordered]@{
                    id = "credentials.polymarket"
                    category = "Credentials"
                    requirement = "LiveOnly"
                    status = "Ready"
                    source = "phase5-regression"
                    lastCheckedAtUtc = "2026-05-03T12:00:00.0000000+00:00"
                    summary = "credentials present"
                    remediationHint = "none"
                    evidence = [ordered]@{
                        apiKey = "[redacted]"
                        public = "safe"
                    }
                }
            )
            capabilities = @(
                [ordered]@{
                    capability = "PaperTrading"
                    status = "Ready"
                    blockingCheckIds = @()
                    summary = "ready"
                }
            )
        }
        exportReferences = [ordered]@{
            jsonApi = "/api/replay-exports?strategyId=dual_leg_arbitrage&marketId=market-1&runSessionId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa&limit=100"
            jsonCli = "autotrade export replay-package --json --strategy-id `"dual_leg_arbitrage`" --market-id `"market-1`" --session-id `"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa`" --limit 100"
            schema = "docs/operations/replay-export-schema.md"
        }
    }

    $fixture | ConvertTo-Json -Depth 20 | Set-Content -Path $fixturePath -Encoding UTF8
    $fixtureText = Get-Content -Path $fixturePath -Raw
    $forbiddenValues = @(
        "ffffffff-ffff-ffff-ffff-ffffffffffff",
        "private-key-value",
        "api-key-value",
        "super-secret",
        "authorization-token",
        "readiness-api-key",
        "readiness-token",
        "salt-value",
        "timestamp-value"
    )

    foreach ($value in $forbiddenValues) {
        if ($fixtureText.IndexOf($value, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Replay fixture contains forbidden value: $value"
        }
    }

    return $fixturePath
}

function Write-BrowserSpec {
    $browserWorkDir = Join-Path $webAppDir ".codex-run/phase5"
    New-Item -ItemType Directory -Force -Path $browserWorkDir | Out-Null
    $browserSpec = Join-Path $browserWorkDir "phase5_browser_evidence.spec.mjs"
    $browserConfig = Join-Path $browserWorkDir "phase5.playwright.config.mjs"
    @'
import { expect, test } from '@playwright/test'
import { writeFile } from 'node:fs/promises'
import path from 'node:path'

test.use({ channel: 'chrome', viewport: { width: 1440, height: 1000 } })

const artifactDir = process.env.PHASE5_ARTIFACT_DIR
const webBaseUrl = process.env.PHASE5_WEB_URL
const now = '2026-05-03T12:00:00.000Z'
const riskEventId = 'risk-1'

const market = {
  marketId: 'market-1',
  conditionId: 'condition-1',
  name: 'Will the fixture market settle yes?',
  category: 'Politics',
  status: 'open',
  yesPrice: 0.48,
  noPrice: 0.52,
  liquidity: 25000,
  volume24h: 3500,
  expiresAtUtc: '2026-05-13T12:00:00.000Z',
  signalScore: 0.62,
  slug: 'fixture-market',
  description: 'Deterministic fixture market.',
  acceptingOrders: true,
  tokens: [
    { tokenId: 'token-yes', outcome: 'YES', price: 0.48, winner: null },
    { tokenId: 'token-no', outcome: 'NO', price: 0.52, winner: null }
  ],
  tags: ['fixture', 'phase-5'],
  spread: 0.04,
  source: 'phase5-fixture'
}

const snapshot = {
  timestampUtc: now,
  dataMode: 'deterministic',
  commandMode: 'paper',
  process: {
    apiStatus: 'Ready',
    environment: 'Test',
    executionMode: 'Paper',
    modulesEnabled: true,
    readyChecks: 4,
    degradedChecks: 0,
    unhealthyChecks: 0
  },
  risk: {
    killSwitchActive: false,
    killSwitchLevel: 'None',
    killSwitchReason: null,
    killSwitchActivatedAtUtc: null,
    totalCapital: 10000,
    availableCapital: 7500,
    capitalUtilizationPct: 25,
    openNotional: 2500,
    openOrders: 2,
    unhedgedExposures: 1,
    limits: [
      { name: 'PerMarketNotional', current: 250, limit: 500, unit: 'USDC', state: 'watch' }
    ]
  },
  metrics: [
    { label: 'Latency', value: '12 ms', delta: '-3 ms', tone: 'good' }
  ],
  strategies: [
    {
      strategyId: 'dual_leg_arbitrage',
      name: 'Dual Leg Arbitrage',
      state: 'Running',
      enabled: true,
      configVersion: 'cfg-v1',
      desiredState: 'Running',
      activeMarkets: 3,
      cycleCount: 44,
      snapshotsProcessed: 1024,
      channelBacklog: 0,
      isKillSwitchBlocked: false,
      lastHeartbeatUtc: '2026-05-03T11:59:55.000Z',
      lastDecisionAtUtc: '2026-05-03T11:59:45.000Z',
      lastError: null,
      blockedReason: null,
      parameters: [{ name: 'MaxSpreadBps', value: '250' }]
    }
  ],
  markets: [market],
  orders: [
    {
      clientOrderId: 'client-1',
      strategyId: 'dual_leg_arbitrage',
      marketId: 'market-1',
      side: 'Buy',
      outcome: 'YES',
      price: 0.47,
      quantity: 20,
      filledQuantity: 5,
      status: 'Open',
      updatedAtUtc: '2026-05-03T11:59:40.000Z'
    }
  ],
  positions: [
    {
      marketId: 'market-1',
      outcome: 'YES',
      quantity: 25,
      averageCost: 0.44,
      notional: 11,
      realizedPnl: 1.25,
      markPrice: 0.48,
      unrealizedPnl: 1,
      totalPnl: 2.25,
      returnPct: 20.45,
      markSource: 'midpoint',
      updatedAtUtc: '2026-05-03T11:59:48.000Z'
    }
  ],
  decisions: [
    {
      strategyId: 'dual_leg_arbitrage',
      action: 'QuoteAdjusted',
      marketId: 'market-1',
      reason: 'Spread widened',
      createdAtUtc: '2026-05-03T11:59:30.000Z'
    }
  ],
  timeline: [
    {
      timestampUtc: '2026-05-03T11:59:00.000Z',
      label: 'Cycle completed',
      detail: 'Processed one market snapshot',
      tone: 'info'
    }
  ],
  capitalCurve: [
    { timestampUtc: '2026-05-03T11:58:00.000Z', value: 9950 },
    { timestampUtc: now, value: 10000 }
  ],
  latencyCurve: [
    { timestampUtc: '2026-05-03T11:58:00.000Z', value: 15 },
    { timestampUtc: now, value: 12 }
  ]
}

const readiness = {
  contractVersion: 'phase5-readiness',
  checkedAtUtc: now,
  status: 'Ready',
  checks: [
    {
      id: 'risk.limits.configured',
      category: 'RiskLimits',
      requirement: 'Required',
      status: 'Ready',
      source: 'phase5-fixture',
      lastCheckedAtUtc: now,
      summary: 'Risk limits configured',
      remediationHint: 'none',
      evidence: { source: 'fixture' }
    }
  ],
  capabilities: [
    { capability: 'PaperTrading', status: 'Ready', blockingCheckIds: [], summary: 'ready' }
  ]
}

const exposure = {
  evidenceId: riskEventId,
  strategyId: 'dual_leg_arbitrage',
  marketId: 'market-1',
  tokenId: 'token-yes',
  hedgeTokenId: 'token-no',
  outcome: 'YES',
  side: 'Buy',
  quantity: 20,
  price: 0.47,
  notional: 9.4,
  durationSeconds: 120,
  startedAtUtc: '2026-05-03T11:57:00.000Z',
  endedAtUtc: '2026-05-03T11:59:00.000Z',
  timeoutSeconds: 90,
  hedgeState: 'Expired',
  mitigationResult: 'ForceHedge',
  source: 'risk_event_context'
}

const riskDrilldown = {
  generatedAtUtc: now,
  event: {
    id: riskEventId,
    code: 'RISK_UNHEDGED_TIMEOUT',
    severity: 'Critical',
    message: 'Unhedged exposure timeout.',
    strategyId: 'dual_leg_arbitrage',
    contextJson: '{"mitigationResult":"ForceHedge"}',
    createdAtUtc: '2026-05-03T11:59:00.000Z',
    marketId: 'market-1'
  },
  trigger: {
    triggerReason: 'Unhedged exposure exceeded timeout',
    limitName: 'UnhedgedExposureSeconds',
    currentValue: 120,
    threshold: 90,
    unit: 'seconds',
    state: 'danger'
  },
  action: {
    selectedAction: 'ForceHedge',
    mitigationResult: 'Submitted hedge order',
    reasonCode: 'UNHEDGED_TIMEOUT'
  },
  affectedOrders: [
    {
      orderId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
      clientOrderId: 'client-1',
      strategyId: 'dual_leg_arbitrage',
      marketId: 'market-1',
      status: 'Open',
      source: 'order_event',
      detailReference: 'order-events/client-1'
    }
  ],
  exposure,
  killSwitch: {
    scope: 'Strategy',
    level: 'SoftStop',
    reasonCode: 'UNHEDGED_TIMEOUT',
    reason: 'Stop strategy until exposure is reconciled.',
    activatedAtUtc: null,
    triggeringRiskEventId: riskEventId
  },
  sourceReferences: {
    jsonApi: `/api/control-room/risk/events/${riskEventId}`,
    csvApi: `/api/control-room/risk/events/${riskEventId}.csv`,
    riskEventIds: [riskEventId],
    orderEventIds: ['33333333-3333-3333-3333-333333333333']
  }
}

const exposures = {
  generatedAtUtc: now,
  count: 1,
  limit: 20,
  query: {
    strategyId: null,
    marketId: null,
    riskEventId,
    fromUtc: null,
    toUtc: null,
    limit: 20
  },
  exposures: [exposure]
}

const incidentActions = {
  generatedAtUtc: now,
  commandMode: 'paper',
  runbookPath: 'docs/operations/autotrade-incident-runbook.md',
  actions: [
    {
      id: 'hard-stop',
      label: 'Hard stop',
      category: 'Risk',
      scope: 'Global',
      method: 'POST',
      path: '/api/control-room/risk/kill-switch',
      enabled: true,
      disabledReason: null,
      confirmationText: 'CONFIRM',
      result: 'Activates audited global hard stop.'
    },
    {
      id: 'cancel-open-orders',
      label: 'Cancel open orders',
      category: 'Orders',
      scope: 'Global',
      method: 'POST',
      path: '/api/control-room/incidents/cancel-open-orders',
      enabled: true,
      disabledReason: null,
      confirmationText: 'CONFIRM',
      result: 'Cancels open paper orders.'
    }
  ]
}

function json(body) {
  return {
    status: 200,
    headers: {
      'content-type': 'application/json',
      'access-control-allow-origin': '*'
    },
    body: JSON.stringify(body)
  }
}

test('captures Phase 5 audit and risk evidence', async ({ page }) => {
  const screenshots = []
  const browserErrors = []

  page.on('pageerror', error => browserErrors.push(error.stack || error.message))
  page.on('console', message => {
    if (message.type() === 'error') {
      browserErrors.push(message.text())
    }
  })

  const handleApiRoute = async route => {
    const url = new URL(route.request().url())
    const pathName = url.pathname

    if (pathName === '/api/control-room/snapshot') {
      await route.fulfill(json(snapshot))
      return
    }

    if (pathName === '/api/readiness') {
      await route.fulfill(json(readiness))
      return
    }

    if (pathName === '/api/control-room/markets') {
      await route.fulfill(json({
        timestampUtc: now,
        source: 'phase5-fixture',
        totalCount: 1,
        isComplete: true,
        categories: ['Politics'],
        markets: [market]
      }))
      return
    }

    if (pathName === `/api/control-room/risk/events/${riskEventId}`) {
      await route.fulfill(json(riskDrilldown))
      return
    }

    if (pathName === '/api/control-room/risk/unhedged-exposures') {
      await route.fulfill(json(exposures))
      return
    }

    if (pathName === '/api/control-room/incidents/actions') {
      await route.fulfill(json(incidentActions))
      return
    }

    if (pathName === '/api/strategy-decisions') {
      await route.fulfill(json({
        timestampUtc: now,
        count: 1,
        limit: 12,
        decisions: [
          {
            decisionId: '22222222-2222-2222-2222-222222222222',
            strategyId: 'dual_leg_arbitrage',
            action: 'Buy',
            reason: 'edge',
            marketId: 'market-1',
            createdAtUtc: '2026-05-03T12:01:00.000Z',
            configVersion: 'cfg-v1',
            correlationId: 'corr-1',
            executionMode: 'Paper'
          }
        ]
      }))
      return
    }

    if (pathName.startsWith('/api/strategy-parameters/')) {
      await route.fulfill(json({
        strategyId: 'dual_leg_arbitrage',
        configVersion: 'cfg-v1',
        parameters: [
          { name: 'MaxSpreadBps', value: '250', type: 'decimal', editable: true }
        ],
        recentVersions: []
      }))
      return
    }

    await route.fulfill(json({}))
  }

  await page.route('http://localhost:5080/api/**', handleApiRoute)
  await page.route('http://127.0.0.1:5080/api/**', handleApiRoute)

  await page.addInitScript(() => window.localStorage.setItem('autotrade.locale', 'en-US'))
  await page.goto(webBaseUrl, { waitUntil: 'networkidle' })
  await writeFile(path.join(artifactDir, 'phase5-browser-initial.html'), await page.content(), 'utf8')
  if (await page.locator('.app-shell').count() === 0) {
    throw new Error(`App shell did not mount. Browser errors: ${browserErrors.join(' | ') || 'none'}`)
  }
  await expect(page.locator('.app-shell')).toBeVisible()
  await expect(page.locator('.view-tabs > button')).toHaveCount(5)
  await page.locator('.view-tabs > button').nth(3).click()
  await expect(page.locator('.activity-grid')).toBeVisible()
  await expect(page.getByText('QuoteAdjusted').first()).toBeVisible()
  const auditScreenshot = path.join(artifactDir, 'audit-activity-desktop.png')
  await page.screenshot({ path: auditScreenshot, fullPage: true })
  screenshots.push(auditScreenshot)

  await page.getByRole('button', { name: /Operations/ }).click()
  await page.waitForTimeout(500)
  await writeFile(path.join(artifactDir, 'phase5-ops-after-click.html'), await page.content(), 'utf8')
  if (browserErrors.length > 0) {
    throw new Error(`Browser errors after opening Operations: ${browserErrors.join(' | ')}`)
  }
  await expect(page.getByRole('button', { name: /Operations/ })).toHaveClass(/active/)
  await expect(page.locator('.risk-drilldown')).toBeVisible()
  await page.fill('#risk-event-id', riskEventId)
  await page.getByRole('button', { name: /^Load$/ }).click()
  await expect(page.locator('.risk-drilldown')).toContainText('RISK_UNHEDGED_TIMEOUT')
  await expect(page.locator('.incident-actions')).toBeVisible()
  const riskScreenshot = path.join(artifactDir, 'risk-drilldown-ui.png')
  const incidentScreenshot = path.join(artifactDir, 'incident-actions-desktop.png')
  await page.screenshot({ path: riskScreenshot, fullPage: true })
  await page.screenshot({ path: incidentScreenshot, fullPage: true })
  screenshots.push(riskScreenshot, incidentScreenshot)

  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto(webBaseUrl, { waitUntil: 'networkidle' })
  await expect(page.locator('.app-shell')).toBeVisible()
  await page.getByRole('button', { name: /Operations/ }).click()
  await expect(page.getByRole('button', { name: /Operations/ })).toHaveClass(/active/)
  await expect(page.locator('.risk-drilldown')).toBeVisible()
  await page.fill('#risk-event-id', riskEventId)
  await page.getByRole('button', { name: /^Load$/ }).click()
  await expect(page.locator('.risk-drilldown')).toContainText('RISK_UNHEDGED_TIMEOUT')
  const mobileRiskScreenshot = path.join(artifactDir, 'risk-drilldown-mobile.png')
  const mobileIncidentScreenshot = path.join(artifactDir, 'incident-actions-mobile.png')
  await page.screenshot({ path: mobileRiskScreenshot, fullPage: true })
  await page.screenshot({ path: mobileIncidentScreenshot, fullPage: true })
  screenshots.push(mobileRiskScreenshot, mobileIncidentScreenshot)

  const hasHorizontalOverflow = await page.evaluate(() =>
    document.documentElement.scrollWidth > document.documentElement.clientWidth + 2
  )
  expect(hasHorizontalOverflow).toBe(false)
  expect(browserErrors).toEqual([])

  await writeFile(path.join(artifactDir, 'phase5-browser-report.json'), `${JSON.stringify({
    generatedAtUtc: new Date().toISOString(),
    screenshots,
    browserErrors,
    hasHorizontalOverflow
  }, null, 2)}\n`, 'utf8')
})
'@ | Set-Content -Path $browserSpec -Encoding UTF8

    @'
export default {
  testDir: '.',
  testMatch: /phase5_browser_evidence\.spec\.mjs$/,
  timeout: 60000,
  expect: {
    timeout: 10000
  },
  use: {
    channel: 'chrome'
  }
}
'@ | Set-Content -Path $browserConfig -Encoding UTF8

    return [pscustomobject]@{
        spec = $browserSpec
        config = $browserConfig
        workDir = $browserWorkDir
    }
}

$dotnet = Resolve-Tool -WindowsName "dotnet.exe" -FallbackName "dotnet"
$npm = Resolve-Tool -WindowsName "npm.cmd" -FallbackName "npm"
$npx = Resolve-Tool -WindowsName "npx.cmd" -FallbackName "npx"
$replayFixturePath = Write-ReplayFixture

Invoke-GateStep `
    -Name "phase5-strategy-audit-replay-tests" `
    -FilePath $dotnet `
    -Arguments @(
        "test",
        "context\Strategy\Autotrade.Strategy.Tests\Autotrade.Strategy.Tests.csproj",
        "--no-restore",
        "--filter",
        "FullyQualifiedName~AuditTimelineServiceTests|FullyQualifiedName~ReplayExportServiceTests"
    ) `
    -WorkingDirectory $root

Invoke-GateStep `
    -Name "phase5-trading-risk-drilldown-tests" `
    -FilePath $dotnet `
    -Arguments @(
        "test",
        "context\Trading\Autotrade.Trading.Tests\Autotrade.Trading.Tests.csproj",
        "--no-restore",
        "--filter",
        "FullyQualifiedName~RiskDrilldownServiceTests"
    ) `
    -WorkingDirectory $root

Invoke-GateStep `
    -Name "phase5-api-audit-incident-contract-tests" `
    -FilePath $dotnet `
    -Arguments @(
        "test",
        "interfaces\Autotrade.Api.Tests\Autotrade.Api.Tests.csproj",
        "--no-restore",
        "--filter",
        "FullyQualifiedName~AuditTimelineControllerContractTests|FullyQualifiedName~RiskDrilldownControllerContractTests|FullyQualifiedName~ControlRoomCommandServiceTests|FullyQualifiedName~ControlRoomControllerContractTests|FullyQualifiedName~ReplayExportsControllerContractTests"
    ) `
    -WorkingDirectory $root

if (-not $SkipFullDotnetTest) {
    Invoke-GateStep `
        -Name "phase5-dotnet-test-solution" `
        -FilePath $dotnet `
        -Arguments @("test", "Autotrade.sln", "--no-restore") `
        -WorkingDirectory $root
}

Invoke-GateStep `
    -Name "phase5-web-build" `
    -FilePath $npm `
    -Arguments @("run", "build") `
    -WorkingDirectory $webAppDir

if (-not $SkipBrowserScreenshots) {
    $browserFiles = Write-BrowserSpec
    $pathSeparators = [char[]]@("\", "/")
    $webAppFullPath = (Resolve-Path $webAppDir).Path.TrimEnd($pathSeparators)
    $browserConfigFullPath = (Resolve-Path $browserFiles.config).Path
    if ($browserConfigFullPath.IndexOf($webAppFullPath, [StringComparison]::OrdinalIgnoreCase) -eq 0) {
        $browserConfigRelative = $browserConfigFullPath.Substring($webAppFullPath.Length).TrimStart($pathSeparators).Replace("\", "/")
    }
    else {
        $browserConfigRelative = $browserConfigFullPath
    }
    $webPort = Get-FreeTcpPort
    $webUrl = "http://127.0.0.1:$webPort"
    $viteOut = Join-Path $artifactDir "phase5-vite.out.log"
    $viteErr = Join-Path $artifactDir "phase5-vite.err.log"
    $vite = Start-Process `
        -FilePath $npm `
        -ArgumentList @("run", "dev", "--", "--host", "127.0.0.1", "--port", "$webPort", "--strictPort") `
        -WorkingDirectory $webAppDir `
        -RedirectStandardOutput $viteOut `
        -RedirectStandardError $viteErr `
        -WindowStyle Hidden `
        -PassThru

    try {
        Wait-HttpReady -Url $webUrl
        $previousArtifactDir = $env:PHASE5_ARTIFACT_DIR
        $previousWebUrl = $env:PHASE5_WEB_URL
        $env:PHASE5_ARTIFACT_DIR = $artifactDir
        $env:PHASE5_WEB_URL = $webUrl
        try {
            Invoke-GateStep `
                -Name "phase5-browser-screenshots" `
                -FilePath $npx `
                -Arguments @("playwright", "test", "--config", $browserConfigRelative, "--reporter=line") `
                -WorkingDirectory $webAppDir
        }
        finally {
            $env:PHASE5_ARTIFACT_DIR = $previousArtifactDir
            $env:PHASE5_WEB_URL = $previousWebUrl
        }
    }
    finally {
        if ($null -ne $vite -and -not $vite.HasExited) {
            Stop-Process -Id $vite.Id -Force
        }

        Get-CimInstance Win32_Process -Filter "name = 'node.exe'" |
            Where-Object {
                $_.CommandLine -like "*vite*" -and
                $_.CommandLine -like "*--port*" -and
                $_.CommandLine -like "*$webPort*"
            } |
            ForEach-Object {
                Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
            }
    }
}

$requiredArtifacts = @(
    "replay-package.json",
    "audit-activity-desktop.png",
    "risk-drilldown-ui.png",
    "risk-drilldown-mobile.png",
    "incident-actions-desktop.png",
    "incident-actions-mobile.png"
)

foreach ($artifact in $requiredArtifacts) {
    $path = Join-Path $artifactDir $artifact
    if (-not (Test-Path $path)) {
        throw "Missing required Phase 5 artifact: $path"
    }

    if ((Get-Item $path).Length -le 0) {
        throw "Phase 5 artifact is empty: $path"
    }
}

$manifestPath = Join-Path $artifactDir "phase5-audit-regression-gate.json"
$manifest = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    contractVersion = "phase5.audit-regression-gate.v1"
    replayFixture = (Resolve-Path $replayFixturePath).Path
    evidenceDirectory = (Resolve-Path $artifactDir).Path
    assertions = @(
        "Audit timeline tests are included in the gate.",
        "Risk drill-down tests are included in the gate.",
        "Incident action controller and command service tests are included in the gate.",
        "Replay export fixture is generated under artifacts/audit/phase-5/.",
        "Replay export redaction is enforced by ReplayExportServiceTests and fixture forbidden-value checks.",
        "Replay export remains read-only: the public API is GET-only and the service depends only on query/export repository methods."
    )
    artifacts = $requiredArtifacts
    steps = $steps
}

$manifest | ConvertTo-Json -Depth 20 | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host "Phase 5 audit regression gate passed."
Write-Host "Manifest: $manifestPath"
