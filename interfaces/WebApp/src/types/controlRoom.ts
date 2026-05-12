export type StrategyState = 'Created' | 'Running' | 'Paused' | 'Stopped' | 'Faulted'
export type ReadinessCapability = 'PaperTrading' | 'LiveTrading'
export type ReadinessCheckCategory =
  | 'Runtime'
  | 'Database'
  | 'Migrations'
  | 'Api'
  | 'MarketData'
  | 'WebSocket'
  | 'BackgroundJobs'
  | 'AccountSync'
  | 'Compliance'
  | 'ExecutionMode'
  | 'RiskLimits'
  | 'Credentials'
export type ReadinessCheckRequirement = 'Required' | 'Optional' | 'LiveOnly'
export type ReadinessCheckStatus = 'Ready' | 'Degraded' | 'Unhealthy' | 'Blocked' | 'Skipped'
export type ReadinessOverallStatus = 'Ready' | 'Degraded' | 'Blocked'

export interface ReadinessReport {
  contractVersion: string
  checkedAtUtc: string
  status: ReadinessOverallStatus
  checks: ReadinessCheckResult[]
  capabilities: ReadinessCapabilityResult[]
}

export interface ReadinessCheckResult {
  id: string
  category: ReadinessCheckCategory
  requirement: ReadinessCheckRequirement
  status: ReadinessCheckStatus
  source: string
  lastCheckedAtUtc: string
  summary: string
  remediationHint: string
  evidence: Record<string, string>
}

export interface ReadinessCapabilityResult {
  capability: ReadinessCapability
  status: ReadinessOverallStatus
  blockingCheckIds: string[]
  summary: string
}

export interface ControlRoomSnapshot {
  timestampUtc: string
  dataMode: string
  commandMode: string
  process: ControlRoomProcess
  risk: ControlRoomRisk
  metrics: ControlRoomMetric[]
  strategies: ControlRoomStrategy[]
  markets: ControlRoomMarket[]
  orders: ControlRoomOrder[]
  positions: ControlRoomPosition[]
  decisions: ControlRoomDecision[]
  timeline: ControlRoomTimelineItem[]
  capitalCurve: ControlRoomSeriesPoint[]
  latencyCurve: ControlRoomSeriesPoint[]
}

export interface ControlRoomProcess {
  apiStatus: string
  environment: string
  executionMode: string
  modulesEnabled: boolean
  readyChecks: number
  degradedChecks: number
  unhealthyChecks: number
}

export interface ControlRoomRisk {
  killSwitchActive: boolean
  killSwitchLevel: string
  killSwitchReason: string | null
  killSwitchActivatedAtUtc: string | null
  totalCapital: number
  availableCapital: number
  capitalUtilizationPct: number
  openNotional: number
  openOrders: number
  unhedgedExposures: number
  limits: ControlRoomRiskLimit[]
}

export interface ControlRoomRiskLimit {
  name: string
  current: number
  limit: number
  unit: string
  state: 'good' | 'watch' | 'danger' | 'unknown'
}

export interface ControlRoomMetric {
  label: string
  value: string
  delta: string
  tone: 'good' | 'watch' | 'neutral' | 'muted'
}

export interface ControlRoomStrategy {
  strategyId: string
  name: string
  state: StrategyState
  enabled: boolean
  configVersion: string
  desiredState: string
  activeMarkets: number
  cycleCount: number
  snapshotsProcessed: number
  channelBacklog: number
  isKillSwitchBlocked: boolean
  lastHeartbeatUtc: string | null
  lastDecisionAtUtc: string | null
  lastError: string | null
  blockedReason: ControlRoomStrategyBlockedReason | null
  parameters: ControlRoomParameter[]
}

export type StrategyBlockedReasonKind =
  | 'None'
  | 'KillSwitch'
  | 'DisabledConfig'
  | 'RiskLimit'
  | 'Readiness'
  | 'StaleData'
  | 'MissingMarket'
  | 'StrategyFault'

export interface ControlRoomStrategyBlockedReason {
  kind: StrategyBlockedReasonKind
  code: string
  message: string
}

export interface ControlRoomParameter {
  name: string
  value: string
}

export interface ControlRoomMarket {
  marketId: string
  conditionId: string
  name: string
  category: string
  status: string
  yesPrice: number | null
  noPrice: number | null
  liquidity: number
  volume24h: number
  expiresAtUtc: string | null
  signalScore: number
  slug: string | null
  description: string | null
  acceptingOrders: boolean
  tokens: ControlRoomMarketToken[]
  tags: string[]
  spread: number | null
  source: string
  rankScore?: number
  rankReason?: string | null
  unsuitableReasons?: string[]
}

export interface ControlRoomMarketToken {
  tokenId: string
  outcome: string
  price: number | null
  winner: boolean | null
}

export interface ControlRoomMarketsResponse {
  timestampUtc: string
  source: string
  totalCount: number
  isComplete: boolean
  categories: string[]
  markets: ControlRoomMarket[]
}

export interface ControlRoomMarketDetailResponse {
  timestampUtc: string
  source: string
  market: ControlRoomMarket
  orderBook: ControlRoomOrderBook | null
  orders: ControlRoomOrder[]
  positions: ControlRoomPosition[]
  decisions: ControlRoomDecision[]
  microstructure: ControlRoomMetric[]
}

export interface ControlRoomOrderBook {
  marketId: string
  tokenId: string
  outcome: string
  lastUpdatedUtc: string
  bestBidPrice: number | null
  bestBidSize: number | null
  bestAskPrice: number | null
  bestAskSize: number | null
  spread: number | null
  midpoint: number | null
  totalBidSize: number
  totalAskSize: number
  imbalancePct: number
  maxLevelNotional: number
  source: string
  freshness: ControlRoomOrderBookFreshness
  bids: ControlRoomOrderBookLevel[]
  asks: ControlRoomOrderBookLevel[]
}

export type ControlRoomOrderBookFreshnessStatus = 'Fresh' | 'Delayed' | 'Stale' | string

export interface ControlRoomOrderBookFreshness {
  status: ControlRoomOrderBookFreshnessStatus
  ageSeconds: number
  freshSeconds: number
  staleSeconds: number
  message: string
}

export interface ControlRoomOrderBookLevel {
  level: number
  price: number
  size: number
  notional: number
  depthPct: number
}

export interface ControlRoomOrder {
  clientOrderId: string
  strategyId: string
  marketId: string
  side: string
  outcome: string
  price: number
  quantity: number
  filledQuantity: number
  status: string
  updatedAtUtc: string
}

export interface ControlRoomPosition {
  marketId: string
  outcome: string
  quantity: number
  averageCost: number
  notional: number
  realizedPnl: number
  markPrice: number | null
  unrealizedPnl: number | null
  totalPnl: number | null
  returnPct: number | null
  markSource: string
  updatedAtUtc: string
}

export interface ControlRoomDecision {
  strategyId: string
  action: string
  marketId: string
  reason: string
  createdAtUtc: string
}

export interface StrategyDecisionListResponse {
  timestampUtc: string
  count: number
  limit: number
  decisions: StrategyDecisionSummary[]
}

export interface StrategyDecisionSummary {
  decisionId: string
  strategyId: string
  action: string
  reason: string
  marketId: string | null
  createdAtUtc: string
  configVersion: string
  correlationId: string | null
  executionMode: string | null
}

export interface StrategyParameterSnapshot {
  strategyId: string
  configVersion: string
  parameters: StrategyParameterValue[]
  recentVersions: StrategyParameterVersionRecord[]
}

export interface StrategyParameterValue {
  name: string
  value: string
  type: string
  editable: boolean
}

export interface StrategyParameterVersionRecord {
  versionId: string
  strategyId: string
  configVersion: string
  previousConfigVersion: string | null
  changeType: string
  source: string
  actor: string | null
  reason: string | null
  createdAtUtc: string
  diff: StrategyParameterDiff[]
  rollbackSourceVersionId: string | null
}

export interface StrategyParameterDiff {
  name: string
  previousValue: string
  nextValue: string
}

export interface StrategyParameterMutationResponse {
  accepted: boolean
  status: string
  message: string
  version: StrategyParameterVersionRecord | null
  snapshot: StrategyParameterSnapshot
}

export interface ControlRoomTimelineItem {
  timestampUtc: string
  label: string
  detail: string
  tone: string
}

export interface ControlRoomSeriesPoint {
  timestampUtc: string
  value: number
}

export interface ControlRoomCommandResponse {
  status: string
  commandMode: string
  message: string
  snapshot: ControlRoomSnapshot
}

export type ArcPerformanceOutcomeStatus =
  | 'ExecutedWin'
  | 'ExecutedLoss'
  | 'ExecutedFlat'
  | 'RejectedRisk'
  | 'RejectedCompliance'
  | 'Expired'
  | 'SkippedNoAccess'
  | 'FailedExecution'
  | 'CancelledOperator'

export type ArcPerformanceRecordStatus =
  | 'Pending'
  | 'SkippedDisabled'
  | 'Submitted'
  | 'Confirmed'
  | 'Failed'
  | 'Duplicate'

export type ArcEntitlementPermission =
  | 'ViewSignals'
  | 'ViewReasoning'
  | 'ExportSignal'
  | 'RequestPaperAutoTrade'
  | 'RequestLiveAutoTrade'
  | 'PublishSignal'
  | 'RecordSettlement'

export type ArcStrategyAccessStatusCode =
  | 'Disabled'
  | 'Active'
  | 'Expired'
  | 'MissingWallet'
  | 'InvalidWallet'
  | 'MissingStrategy'
  | 'NotFound'

export interface ArcSubscriptionPlan {
  planId: number
  strategyKey: string
  planName: string
  tier: string
  priceUsdc: number
  durationSeconds: number
  permissions: ArcEntitlementPermission[]
  maxMarkets: number | null
  autoTradingAllowed: boolean
  liveTradingAllowed: boolean
  createdAtUtc: string
}

export interface ArcStrategyAccessStatus {
  walletAddress: string
  strategyKey: string
  statusCode: ArcStrategyAccessStatusCode
  hasAccess: boolean
  reason: string
  permissions: ArcEntitlementPermission[]
  checkedAtUtc: string
  tier: string | null
  expiresAtUtc: string | null
  sourceTransactionHash: string | null
  syncedAtUtc: string | null
  canViewSignals: boolean
}

export interface ArcAccessDecision {
  allowed: boolean
  reasonCode: string
  reason: string
  requiredPermission: ArcEntitlementPermission
  strategyKey: string
  walletAddress: string | null
  resourceKind: string
  resourceId: string
  tier: string | null
  expiresAtUtc: string | null
  evidenceTransactionHash: string | null
}

export interface ArcOpportunitySummary {
  opportunityId: string
  marketId: string
  outcome: string
  edge: number
  status: string
  validUntilUtc: string
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ArcOpportunityEvidenceItem {
  id: string
  researchRunId: string
  sourceKind: string
  sourceName: string
  url: string
  title: string
  summary: string
  publishedAtUtc: string | null
  observedAtUtc: string
  contentHash: string
  sourceQuality: number
}

export interface ArcOpportunityDetail {
  opportunityId: string
  researchRunId: string
  marketId: string
  outcome: string
  fairProbability: number
  confidence: number
  edge: number
  status: string
  validUntilUtc: string
  compiledPolicyJson: string
  reason: string | null
  scoreJson: string | null
  evidence: ArcOpportunityEvidenceItem[]
  signalDecision: ArcAccessDecision
  reasoningDecision: ArcAccessDecision
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ArcSignalSummary {
  signalId: string
  sourceKind: string
  sourceId: string
  strategyId: string
  marketId: string
  venue: string
  expectedEdgeBps: number
  maxNotionalUsdc: number
  validUntilUtc: string
  status: string
  createdAtUtc: string
  publishedAtUtc: string | null
  provenanceHash: string | null
  evidenceUri: string | null
}

export interface ArcSignalDetail extends ArcSignalSummary {
  agentId: string
  reasoningHash: string | null
  riskEnvelopeHash: string | null
  signalHash: string
  sourcePolicyHash: string | null
  transactionHash: string | null
  explorerUrl: string | null
  errorCode: string | null
  actor: string
  reason: string
  signalDecision: ArcAccessDecision
  reasoningDecision: ArcAccessDecision
}

export interface ArcAgentReputation {
  scope: string
  strategyId: string | null
  totalSignals: number
  terminalSignals: number
  pendingSignals: number
  executedSignals: number
  expiredSignals: number
  rejectedSignals: number
  skippedSignals: number
  failedSignals: number
  cancelledSignals: number
  winCount: number
  lossCount: number
  flatCount: number
  averageRealizedPnlBps: number | null
  averageSlippageBps: number | null
  riskRejectionRate: number
  confidenceCoverage: number
  calculatedAtUtc: string
}

export interface ArcPerformanceOutcomeRecord {
  outcomeId: string
  signalId: string
  executionId: string
  strategyId: string
  marketId: string
  status: ArcPerformanceOutcomeStatus
  realizedPnlBps: number | null
  slippageBps: number | null
  fillRate: number | null
  reasonCode: string | null
  outcomeHash: string
  transactionHash: string | null
  explorerUrl: string | null
  recordStatus: ArcPerformanceRecordStatus
  errorCode: string | null
  createdAtUtc: string
  recordedAtUtc: string
}

export interface ArcPaperAutoTradeResponse {
  status: string
  message: string
  accessDecision: ArcAccessDecision
  command: ControlRoomCommandResponse | null
}

export interface ArcRevenueSettlementSummary {
  generatedAtUtc: string
  strategyKey: string
  source: 'simulated' | 'testnet'
  subscriptionRevenueUsdc: number
  builderAttributedFlowUsdc: number
  simulatedBuilderShareUsdc: number
  simulatedTreasuryShareUsdc: number
  settlementTransactionHash: string | null
}

export interface ArcSubscriberPortalSummary {
  plan: ArcSubscriptionPlan | null
  access: ArcStrategyAccessStatus | null
  latestSignal: ArcSignalDetail | null
  performance: ArcAgentReputation | null
  revenue: ArcRevenueSettlementSummary
}

export type ArcProvenanceSourceModule = 'OpportunityDiscovery' | 'SelfImprove' | 'ManualFixture'

export type ArcProvenanceValidationStatus =
  | 'Approved'
  | 'Published'
  | 'StaticValidated'
  | 'UnitTested'
  | 'ReplayValidated'
  | 'ShadowRunning'
  | 'PaperRunning'
  | 'LiveCanary'
  | 'Promoted'

export interface ArcProvenanceEvidenceReference {
  evidenceId: string
  title: string
  summary: string
  contentHash: string
  sourceUri: string | null
  observedAtUtc: string | null
}

export interface ArcSubscriberProvenanceExplanation {
  provenanceHash: string
  sourceModule: ArcProvenanceSourceModule
  sourceId: string
  agentId: string
  marketId: string
  strategyId: string
  validationStatus: ArcProvenanceValidationStatus
  evidence: ArcProvenanceEvidenceReference[]
  evidenceSummaryHash: string
  llmOutputHash: string
  compiledPolicyHash: string
  generatedPackageHash: string | null
  riskEnvelopeHash: string
  evidenceUri: string | null
  privacyNote: string
  createdAtUtc: string
}

export interface CancelOpenOrdersRequest {
  actor?: string
  reasonCode?: string
  reason?: string
  strategyId?: string
  marketId?: string
  confirmationText?: string
}

export interface IncidentPackageQuery {
  riskEventId?: string | null
  strategyId?: string | null
  marketId?: string | null
  orderId?: string | null
  correlationId?: string | null
}

export interface IncidentActionCatalog {
  generatedAtUtc: string
  commandMode: string
  runbookPath: string
  actions: IncidentActionDescriptor[]
}

export interface IncidentActionDescriptor {
  id: string
  label: string
  category: string
  scope: string
  method: string
  path: string
  enabled: boolean
  disabledReason: string | null
  confirmationText: string | null
  result: string
}

export interface IncidentPackage {
  generatedAtUtc: string
  contractVersion: string
  query: IncidentPackageQuery
  snapshot: ControlRoomSnapshot
  actions: IncidentActionCatalog
  runbookReferences: string[]
  exportReferences: string[]
}

export interface ReplayExportQuery {
  strategyId: string | null
  marketId: string | null
  orderId: string | null
  clientOrderId: string | null
  runSessionId: string | null
  riskEventId: string | null
  correlationId: string | null
  fromUtc: string | null
  toUtc: string | null
  limit: number
}

export interface ReplayExportQueryParams {
  strategyId?: string | null
  marketId?: string | null
  orderId?: string | null
  clientOrderId?: string | null
  runSessionId?: string | null
  riskEventId?: string | null
  correlationId?: string | null
  fromUtc?: string | null
  toUtc?: string | null
  limit?: number | null
}

export interface ReplayExportPackage {
  generatedAtUtc: string
  contractVersion: string
  query: ReplayExportQuery
  redaction: ReplayRedactionSummary
  completenessNotes: string[]
  runSession: PaperRunSessionRecord | null
  timeline: AuditTimeline
  evidence: ReplayEvidenceBundle
  strategyConfigVersions: ReplayStrategyConfigVersion[]
  readiness: ReplayReadinessReport | null
  exportReferences: ReplayExportReferences
}

export interface ReplayRedactionSummary {
  appliedRules: string[]
  excludedFields: string[]
}

export interface AuditTimeline {
  generatedAtUtc: string
  count: number
  limit: number
  query: AuditTimelineQuery
  items: AuditTimelineItem[]
}

export interface AuditTimelineQuery {
  strategyId: string | null
  marketId: string | null
  orderId: string | null
  clientOrderId: string | null
  runSessionId: string | null
  riskEventId: string | null
  correlationId: string | null
  fromUtc: string | null
  toUtc: string | null
  limit: number
}

export interface AuditTimelineItem {
  id: string
  timestampUtc: string
  type: string
  source: string
  severity: string
  message: string
  evidenceRef: string
  strategyId: string | null
  marketId: string | null
  orderId: string | null
  clientOrderId: string | null
  runSessionId: string | null
  riskEventId: string | null
  correlationId: string | null
  detailJson: string | null
}

export interface ReplayEvidenceBundle {
  decisions: ReplayDecisionRecord[]
  orderEvents: ReplayOrderEventRecord[]
  orders: ReplayOrderRecord[]
  trades: ReplayTradeRecord[]
  positions: ReplayPositionRecord[]
  riskEvents: ReplayRiskEventRecord[]
}

export interface ReplayDecisionRecord {
  decisionId: string
  strategyId: string
  action: string
  reason: string
  marketId: string | null
  contextJson: string | null
  timestampUtc: string
  configVersion: string
  correlationId: string | null
  executionMode: string | null
  runSessionId: string | null
}

export interface ReplayOrderEventRecord {
  id: string
  orderId: string
  clientOrderId: string
  strategyId: string
  marketId: string
  eventType: string
  status: string
  message: string
  contextJson: string | null
  correlationId: string | null
  createdAtUtc: string
  runSessionId: string | null
}

export interface ReplayOrderRecord {
  id: string
  marketId: string
  tokenId: string | null
  strategyId: string | null
  clientOrderId: string | null
  exchangeOrderId: string | null
  correlationId: string | null
  outcome: string
  side: string
  orderType: string
  timeInForce: string
  goodTilDateUtc: string | null
  negRisk: boolean
  price: number
  quantity: number
  filledQuantity: number
  status: string
  rejectionReason: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ReplayTradeRecord {
  id: string
  orderId: string
  clientOrderId: string
  strategyId: string
  marketId: string
  tokenId: string
  outcome: string
  side: string
  price: number
  quantity: number
  exchangeTradeId: string
  fee: number
  notional: number
  correlationId: string | null
  createdAtUtc: string
}

export interface ReplayPositionRecord {
  id: string
  marketId: string
  outcome: string
  quantity: number
  averageCost: number
  realizedPnl: number
  notional: number
  updatedAtUtc: string
}

export interface ReplayRiskEventRecord {
  id: string
  code: string
  severity: string
  message: string
  strategyId: string | null
  contextJson: string | null
  createdAtUtc: string
  marketId: string | null
}

export interface ReplayStrategyConfigVersion {
  strategyId: string
  configVersion: string
  source: string
  observedAtUtc: string | null
}

export type ReplayReadinessReport = ReadinessReport

export interface ReplayExportReferences {
  jsonApi: string
  jsonCli: string
  schema: string
}

export interface PaperRunReport {
  generatedAtUtc: string
  reportStatus: string
  completenessNotes: string[]
  session: PaperRunSessionRecord
  summary: PaperRunReportSummary
  strategyBreakdown: PaperRunStrategyBreakdown[]
  marketBreakdown: PaperRunMarketBreakdown[]
  riskEvents: RiskEventRecord[]
  notableIncidents: PaperRunIncident[]
  evidenceLinks: PaperRunEvidenceLinks
  exportReferences: PaperRunExportReferences
  attribution: PaperRunAttribution
}

export interface PaperRunSessionRecord {
  sessionId: string
  executionMode: string
  configVersion: string
  strategies: string[]
  riskProfileJson: string
  operatorSource: string
  startedAtUtc: string
  stoppedAtUtc: string | null
  stopReason: string | null
  isActive: boolean
  recovered: boolean
}

export interface PaperRunReportSummary {
  decisionCount: number
  orderEventCount: number
  orderCount: number
  tradeCount: number
  positionCount: number
  riskEventCount: number
  filledOrderEventCount: number
  rejectedOrderEventCount: number
  totalBuyNotional: number
  totalSellNotional: number
  totalFees: number
  grossPnl: number
  netPnl: number
}

export interface PaperRunStrategyBreakdown {
  strategyId: string
  decisionCount: number
  orderEventCount: number
  orderCount: number
  tradeCount: number
  riskEventCount: number
  totalBuyNotional: number
  totalSellNotional: number
  totalFees: number
  netPnl: number
  realizedPnl: number
  unrealizedPnl: number | null
  estimatedSlippage: number
  averageDecisionToFillLatencyMs: number | null
  staleDataEventCount: number
  unhedgedExposureNotional: number
  unhedgedExposureSeconds: number
}

export interface PaperRunMarketBreakdown {
  marketId: string
  decisionCount: number
  orderEventCount: number
  orderCount: number
  tradeCount: number
  positionCount: number
  totalBuyNotional: number
  totalSellNotional: number
  netPnl: number
  realizedPnl: number
  unrealizedPnl: number | null
  estimatedSlippage: number
  averageDecisionToFillLatencyMs: number | null
  staleDataEventCount: number
  unhedgedExposureNotional: number
  unhedgedExposureSeconds: number
}

export interface PaperRunAttribution {
  pnl: PaperRunPnlAttribution
  slippage: PaperRunSlippageAttribution
  latency: PaperRunLatencyAttribution
  staleData: PaperRunStaleDataAttribution
  unhedgedExposure: PaperRunUnhedgedExposureAttribution
  strategyTotalsReconcile: boolean
  marketTotalsReconcile: boolean
  reconciliationNotes: string[]
}

export interface PaperRunPnlAttribution {
  realizedPnl: number
  unrealizedPnl: number | null
  fees: number
  grossPnl: number
  netPnl: number
  realizedPnlSource: string
  unrealizedPnlSource: string
  feeSource: string
  markSource: string
  notes: string[]
}

export interface PaperRunSlippageAttribution {
  estimatedSlippage: number
  adverseSlippage: number
  favorablePriceImprovement: number
  source: string
  tradeCountWithEstimate: number
  tradeCountWithoutEstimate: number
  evidenceIds: string[]
  notes: string[]
}

export interface PaperRunLatencyAttribution {
  averageDecisionToFillLatencyMs: number | null
  p95DecisionToFillLatencyMs: number | null
  averageAcceptedToFillLatencyMs: number | null
  fillEventCountWithDecisionLatency: number
  fillEventCountWithAcceptedLatency: number
  evidenceIds: string[]
  notes: string[]
}

export interface PaperRunStaleDataAttribution {
  eventCount: number
  estimatedPnlContribution: number | null
  source: string
  evidenceIds: string[]
  notes: string[]
}

export interface PaperRunUnhedgedExposureAttribution {
  eventCount: number
  totalNotional: number
  totalDurationSeconds: number
  averageDurationSeconds: number | null
  exposures: PaperRunUnhedgedExposureRecord[]
  notes: string[]
}

export interface PaperRunUnhedgedExposureRecord {
  evidenceId: string
  strategyId: string | null
  marketId: string | null
  notional: number
  durationSeconds: number
  mitigationOutcome: string
  startedAtUtc: string | null
  endedAtUtc: string | null
}

export interface RiskEventRecord {
  id: string
  code: string
  severity: string
  message: string
  strategyId: string | null
  contextJson: string | null
  createdAtUtc: string
  marketId: string | null
}

export interface RiskEventDrilldown {
  generatedAtUtc: string
  event: RiskEventRecord
  trigger: RiskTriggerDrilldown
  action: RiskActionDrilldown
  affectedOrders: RiskAffectedOrder[]
  exposure: UnhedgedExposureDrilldown | null
  killSwitch: RiskKillSwitchLink | null
  sourceReferences: RiskDrilldownSourceReferences
}

export interface RiskTriggerDrilldown {
  triggerReason: string
  limitName: string | null
  currentValue: number | null
  threshold: number | null
  unit: string | null
  state: string
}

export interface RiskActionDrilldown {
  selectedAction: string
  mitigationResult: string | null
  reasonCode: string | null
}

export interface RiskAffectedOrder {
  orderId: string | null
  clientOrderId: string | null
  strategyId: string | null
  marketId: string | null
  status: string | null
  source: string
  detailReference: string
}

export interface UnhedgedExposureDrilldown {
  evidenceId: string | null
  strategyId: string
  marketId: string
  tokenId: string
  hedgeTokenId: string
  outcome: string
  side: string
  quantity: number
  price: number
  notional: number
  durationSeconds: number
  startedAtUtc: string
  endedAtUtc: string | null
  timeoutSeconds: number | null
  hedgeState: string
  mitigationResult: string
  source: string
}

export interface RiskKillSwitchLink {
  scope: string
  level: string
  reasonCode: string
  reason: string
  activatedAtUtc: string | null
  triggeringRiskEventId: string | null
}

export interface RiskDrilldownSourceReferences {
  jsonApi: string
  csvApi: string
  riskEventIds: string[]
  orderEventIds: string[]
}

export interface UnhedgedExposureDrilldownResponse {
  generatedAtUtc: string
  count: number
  limit: number
  query: RiskDrilldownQuery
  exposures: UnhedgedExposureDrilldown[]
}

export interface RiskDrilldownQuery {
  strategyId: string | null
  marketId: string | null
  riskEventId: string | null
  fromUtc: string | null
  toUtc: string | null
  limit: number
}

export interface PaperRunIncident {
  timestampUtc: string
  source: string
  severity: string
  code: string
  message: string
  strategyId: string | null
  marketId: string | null
  evidenceId: string
}

export interface PaperRunEvidenceLinks {
  decisionIds: string[]
  orderEventIds: string[]
  orderIds: string[]
  tradeIds: string[]
  positionIds: string[]
  riskEventIds: string[]
}

export interface PaperRunExportReferences {
  jsonApi: string
  jsonCli: string
  csvCli: string
  csvTables: string[]
}

export interface PaperPromotionChecklist {
  sessionId: string
  generatedAtUtc: string
  overallStatus: 'Passed' | 'Failed' | string
  canConsiderLive: boolean
  liveArmingUnchanged: boolean
  criteria: PaperPromotionCriterion[]
  residualRisks: string[]
}

export interface PaperPromotionCriterion {
  id: string
  name: string
  status: 'Passed' | 'Failed' | string
  reason: string
  evidenceIds: string[]
  residualRisks: string[]
}
