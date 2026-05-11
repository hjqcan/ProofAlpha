import { apiRequest } from '@/api/http'
import type {
  ControlRoomCommandResponse,
  ControlRoomMarketDetailResponse,
  ControlRoomMarketsResponse,
  ControlRoomOrderBook,
  ControlRoomSnapshot,
  CancelOpenOrdersRequest,
  IncidentActionCatalog,
  IncidentPackage,
  IncidentPackageQuery,
  PaperPromotionChecklist,
  PaperRunReport,
  PaperRunSessionRecord,
  ReadinessReport,
  ReplayExportPackage,
  ReplayExportQueryParams,
  RiskEventDrilldown,
  StrategyDecisionListResponse,
  StrategyParameterMutationResponse,
  StrategyParameterSnapshot,
  UnhedgedExposureDrilldownResponse,
  StrategyState
} from '@/types/controlRoom'

export function getControlRoomSnapshot(signal?: AbortSignal): Promise<ControlRoomSnapshot> {
  return apiRequest<ControlRoomSnapshot>('/api/control-room/snapshot', undefined, signal)
}

export function getReadinessReport(signal?: AbortSignal): Promise<ReadinessReport> {
  return apiRequest<ReadinessReport>('/api/readiness', undefined, signal)
}

export function getActiveRunSession(signal?: AbortSignal): Promise<PaperRunSessionRecord | null> {
  return apiRequest<PaperRunSessionRecord | null>('/api/run-reports/active', undefined, signal)
}

export function getRunReport(
  sessionId: string,
  limit = 1000,
  signal?: AbortSignal
): Promise<PaperRunReport> {
  return apiRequest<PaperRunReport>(
    `/api/run-reports/${encodeURIComponent(sessionId)}?limit=${limit}`,
    undefined,
    signal
  )
}

export function getPromotionChecklist(
  sessionId: string,
  limit = 1000,
  signal?: AbortSignal
): Promise<PaperPromotionChecklist> {
  return apiRequest<PaperPromotionChecklist>(
    `/api/run-reports/${encodeURIComponent(sessionId)}/promotion-checklist?limit=${limit}`,
    undefined,
    signal
  )
}

export function getReplayExportPackage(
  query: ReplayExportQueryParams = {},
  signal?: AbortSignal
): Promise<ReplayExportPackage> {
  return apiRequest<ReplayExportPackage>(buildReplayExportPath(query), undefined, signal)
}

export function buildReplayExportPath(query: ReplayExportQueryParams = {}) {
  const params = new URLSearchParams()
  Object.entries(query).forEach(([key, value]) => {
    if (value !== undefined && value !== null && String(value).trim() !== '') {
      params.set(key, String(value))
    }
  })

  const suffix = params.size > 0 ? `?${params.toString()}` : ''
  return `/api/replay-exports${suffix}`
}

export interface MarketQuery {
  search?: string
  category?: string
  status?: string
  sort?: string
  minLiquidity?: number
  minVolume24h?: number
  maxDaysToExpiry?: number
  acceptingOrders?: boolean
  minSignalScore?: number
  limit?: number
  offset?: number
}

export interface ControlCommandIntent {
  actor?: string
  reasonCode?: string
  reason?: string
  confirmationText?: string
}

export interface StrategyDecisionQuery {
  strategyId?: string
  marketId?: string
  action?: string
  correlationId?: string
  limit?: number
}

export interface StrategyParameterMutationIntent {
  actor?: string
  reason?: string
  invalidateLiveArming?: boolean
  liveDisarmConfirmationText?: string
}

export interface RiskDrilldownQueryParams {
  strategyId?: string
  marketId?: string
  riskEventId?: string
  fromUtc?: string
  toUtc?: string
  limit?: number
}

export type IncidentPackageQueryParams = IncidentPackageQuery

export function getMarkets(query: MarketQuery = {}, signal?: AbortSignal): Promise<ControlRoomMarketsResponse> {
  const params = new URLSearchParams()
  Object.entries(query).forEach(([key, value]) => {
    if (value !== undefined && value !== null && String(value).trim() !== '') {
      params.set(key, String(value))
    }
  })

  const suffix = params.size > 0 ? `?${params.toString()}` : ''
  return apiRequest<ControlRoomMarketsResponse>(`/api/control-room/markets${suffix}`, undefined, signal)
}

export function getMarketDetail(
  marketId: string,
  levels = 12,
  signal?: AbortSignal
): Promise<ControlRoomMarketDetailResponse> {
  return apiRequest<ControlRoomMarketDetailResponse>(
    `/api/control-room/markets/${encodeURIComponent(marketId)}?levels=${levels}`,
    undefined,
    signal
  )
}

export function getOrderBook(
  marketId: string,
  tokenId?: string,
  outcome?: string,
  levels = 12,
  signal?: AbortSignal
): Promise<ControlRoomOrderBook> {
  const params = new URLSearchParams({ levels: String(levels) })
  if (tokenId) {
    params.set('tokenId', tokenId)
  }
  if (outcome) {
    params.set('outcome', outcome)
  }

  return apiRequest<ControlRoomOrderBook>(
    `/api/control-room/markets/${encodeURIComponent(marketId)}/order-book?${params.toString()}`,
    undefined,
    signal
  )
}

export function getStrategyDecisions(
  query: StrategyDecisionQuery = {},
  signal?: AbortSignal
): Promise<StrategyDecisionListResponse> {
  const params = new URLSearchParams()
  Object.entries(query).forEach(([key, value]) => {
    if (value !== undefined && value !== null && String(value).trim() !== '') {
      params.set(key, String(value))
    }
  })

  const suffix = params.size > 0 ? `?${params.toString()}` : ''
  return apiRequest<StrategyDecisionListResponse>(`/api/strategy-decisions${suffix}`, undefined, signal)
}

export function getStrategyParameters(
  strategyId: string,
  limit = 10,
  signal?: AbortSignal
): Promise<StrategyParameterSnapshot> {
  return apiRequest<StrategyParameterSnapshot>(
    `/api/strategy-parameters/${encodeURIComponent(strategyId)}?limit=${limit}`,
    undefined,
    signal
  )
}

export function getRiskEventDrilldown(
  riskEventId: string,
  signal?: AbortSignal
): Promise<RiskEventDrilldown> {
  return apiRequest<RiskEventDrilldown>(
    `/api/control-room/risk/events/${encodeURIComponent(riskEventId)}`,
    undefined,
    signal
  )
}

export function getUnhedgedExposures(
  query: RiskDrilldownQueryParams = {},
  signal?: AbortSignal
): Promise<UnhedgedExposureDrilldownResponse> {
  const params = new URLSearchParams()
  Object.entries(query).forEach(([key, value]) => {
    if (value !== undefined && value !== null && String(value).trim() !== '') {
      params.set(key, String(value))
    }
  })

  const suffix = params.size > 0 ? `?${params.toString()}` : ''
  return apiRequest<UnhedgedExposureDrilldownResponse>(
    `/api/control-room/risk/unhedged-exposures${suffix}`,
    undefined,
    signal
  )
}

export function getIncidentActions(signal?: AbortSignal): Promise<IncidentActionCatalog> {
  return apiRequest<IncidentActionCatalog>('/api/control-room/incidents/actions', undefined, signal)
}

export function cancelOpenOrders(
  request: CancelOpenOrdersRequest,
  signal?: AbortSignal
): Promise<ControlRoomCommandResponse> {
  return apiRequest<ControlRoomCommandResponse>(
    '/api/control-room/incidents/cancel-open-orders',
    {
      method: 'POST',
      body: JSON.stringify(request)
    },
    signal
  )
}

export function getIncidentPackage(
  query: IncidentPackageQueryParams = {},
  signal?: AbortSignal
): Promise<IncidentPackage> {
  return apiRequest<IncidentPackage>(buildIncidentPackagePath(query), undefined, signal)
}

export function buildIncidentPackagePath(query: IncidentPackageQueryParams = {}) {
  const params = new URLSearchParams()
  Object.entries(query).forEach(([key, value]) => {
    if (value !== undefined && value !== null && String(value).trim() !== '') {
      params.set(key, String(value))
    }
  })

  const suffix = params.size > 0 ? `?${params.toString()}` : ''
  return `/api/control-room/incidents/package${suffix}`
}

export function updateStrategyParameters(
  strategyId: string,
  changes: Record<string, string>,
  intent: StrategyParameterMutationIntent = {},
  signal?: AbortSignal
): Promise<StrategyParameterMutationResponse> {
  return apiRequest<StrategyParameterMutationResponse>(
    `/api/strategy-parameters/${encodeURIComponent(strategyId)}/versions`,
    {
      method: 'POST',
      body: JSON.stringify({
        changes,
        actor: intent.actor,
        reason: intent.reason,
        invalidateLiveArming: intent.invalidateLiveArming ?? false,
        liveDisarmConfirmationText: intent.liveDisarmConfirmationText
      })
    },
    signal
  )
}

export function rollbackStrategyParameters(
  strategyId: string,
  versionId: string,
  intent: StrategyParameterMutationIntent = {},
  signal?: AbortSignal
): Promise<StrategyParameterMutationResponse> {
  return apiRequest<StrategyParameterMutationResponse>(
    `/api/strategy-parameters/${encodeURIComponent(strategyId)}/versions/${encodeURIComponent(versionId)}/rollback`,
    {
      method: 'POST',
      body: JSON.stringify({
        actor: intent.actor,
        reason: intent.reason,
        invalidateLiveArming: intent.invalidateLiveArming ?? false,
        liveDisarmConfirmationText: intent.liveDisarmConfirmationText
      })
    },
    signal
  )
}

export function setStrategyState(
  strategyId: string,
  targetState: Extract<StrategyState, 'Running' | 'Paused' | 'Stopped'>,
  intent: ControlCommandIntent = {},
  signal?: AbortSignal
): Promise<ControlRoomCommandResponse> {
  return apiRequest<ControlRoomCommandResponse>(
    `/api/control-room/strategies/${encodeURIComponent(strategyId)}/state`,
    {
      method: 'POST',
      body: JSON.stringify({ targetState, ...intent })
    },
    signal
  )
}

export function setKillSwitch(
  active: boolean,
  intent: ControlCommandIntent = {},
  signal?: AbortSignal
): Promise<ControlRoomCommandResponse> {
  return apiRequest<ControlRoomCommandResponse>(
    '/api/control-room/risk/kill-switch',
    {
      method: 'POST',
      body: JSON.stringify({
        active,
        level: active ? 'HardStop' : 'None',
        reasonCode: intent.reasonCode ?? (active ? 'UI_CONTROL' : 'UI_RESET'),
        reason: intent.reason ?? (active ? 'Control room hard stop' : 'Control room kill switch reset'),
        actor: intent.actor,
        confirmationText: intent.confirmationText
      })
    },
    signal
  )
}
