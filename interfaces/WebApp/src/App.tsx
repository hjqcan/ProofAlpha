import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode, RefObject } from 'react'
import { IntlProvider, useIntl, type IntlShape } from 'react-intl'
import {
  Activity,
  BookOpen,
  ChevronRight,
  CirclePause,
  ClipboardCheck,
  DatabaseZap,
  FileText,
  Gauge,
  Languages,
  Layers,
  OctagonX,
  Play,
  RefreshCw,
  Search,
  ShieldCheck,
  Square,
  TrendingUp,
  Zap
} from 'lucide-react'
import {
  getControlRoomSnapshot,
  getArcAgentReputation,
  getArcOpportunities,
  getArcOpportunityDetail,
  getArcPerformanceOutcome,
  getArcSignals,
  getArcSignalDetail,
  getArcStrategyAccessStatus,
  getArcStrategyReputation,
  getArcSubscriptionPlans,
  getArcProvenance,
  getMarketDetail,
  getMarkets,
  getOrderBook,
  getActiveRunSession,
  getIncidentActions,
  getPromotionChecklist,
  getReadinessReport,
  getRiskEventDrilldown,
  getRunReport,
  getStrategyDecisions,
  getStrategyParameters,
  getUnhedgedExposures,
  cancelOpenOrders,
  buildIncidentPackagePath,
  requestArcPaperAutoTrade,
  setKillSwitch,
  setStrategyState,
  rollbackStrategyParameters,
  updateStrategyParameters
} from '@/api/controlRoom'
import type { ControlCommandIntent, StrategyParameterMutationIntent } from '@/api/controlRoom'
import { ApiError, apiBaseUrl } from '@/api/http'
import { messages } from '@/i18n/messages'
import type { Locale } from '@/i18n/messages'
import type {
  ControlRoomDecision,
  ControlRoomMarket,
  ControlRoomMarketDetailResponse,
  ControlRoomMarketsResponse,
  ControlRoomMetric,
  ControlRoomOrder,
  ControlRoomOrderBook,
  ControlRoomPosition,
  ControlRoomRisk,
  ControlRoomSnapshot,
  ControlRoomStrategy,
  ControlRoomMarketToken,
  ControlRoomCommandResponse,
  ArcAccessDecision,
  ArcAgentReputation,
  ArcEntitlementPermission,
  ArcOpportunityDetail,
  ArcOpportunitySummary,
  ArcPaperAutoTradeResponse,
  ArcPerformanceOutcomeRecord,
  ArcRevenueSettlementSummary,
  ArcSignalDetail,
  ArcSignalSummary,
  ArcStrategyAccessStatus,
  ArcSubscriptionPlan,
  ArcSubscriberProvenanceExplanation,
  IncidentActionCatalog,
  IncidentActionDescriptor,
  PaperPromotionChecklist,
  PaperRunMarketBreakdown,
  PaperRunReport,
  PaperRunSessionRecord,
  PaperRunStrategyBreakdown,
  ReadinessCapability,
  ReadinessCheckResult,
  ReadinessCheckStatus,
  ReadinessOverallStatus,
  ReadinessReport,
  RiskEventDrilldown,
  StrategyDecisionListResponse,
  StrategyDecisionSummary,
  StrategyParameterSnapshot,
  UnhedgedExposureDrilldownResponse
} from '@/types/controlRoom'

type TargetStrategyState = 'Running' | 'Paused' | 'Stopped'
type ActiveView = 'markets' | 'trade' | 'ops' | 'activity' | 'performance' | 'reports'
type CommandSafetyLevel = 'offline' | 'readonly' | 'degraded' | 'paper' | 'live'
type CommandResultTone = 'good' | 'watch' | 'danger'
type IncidentCancelScope = 'all' | 'strategy'

interface CommandSafety {
  level: CommandSafetyLevel
  label: string
  reason: string
  commandsEnabled: boolean
}

interface CommandResult {
  tone: CommandResultTone
  text: string
}

const supportedLocales: Locale[] = ['zh-CN', 'en-US']
const supportedViews: ActiveView[] = ['markets', 'trade', 'ops', 'activity', 'performance', 'reports']
const MARKET_PAGE_SIZE = 48
const STRATEGY_DECISION_LIMIT = 12
const readinessStatusSummary: ReadinessCheckStatus[] = ['Ready', 'Degraded', 'Unhealthy', 'Blocked', 'Skipped']
const readinessStatusOrder: Record<ReadinessCheckStatus, number> = {
  Blocked: 0,
  Unhealthy: 1,
  Degraded: 2,
  Skipped: 3,
  Ready: 4
}
const readinessRequirementOrder = {
  Required: 0,
  LiveOnly: 1,
  Optional: 2
} as const

const strategyHypotheses: Record<string, string> = {
  dual_leg_arbitrage: 'Exploit temporary mispricing between complementary outcome legs when combined entry cost leaves enough edge after fees, hedge latency, and slippage.',
  endgame_sweep: 'Target late-stage markets where high win probability, short time to expiry, and enough liquidity create a bounded-risk sweep opportunity.',
  liquidity_pulse: 'Enter when order-book pressure is one-sided in a liquid, tight-spread market, then exit on mean reversion or predefined risk stops.',
  liquidity_maker: 'Quote passively inside a tradable spread when top-of-book depth supports inventory control and expected maker edge exceeds holding risk.',
  micro_volatility_scalper: 'Use short-window price dips against recent microstructure samples, with strict hold-time, spread, and slippage controls.'
}

function readInitialLocale(): Locale {
  const saved = window.localStorage.getItem('autotrade.locale')
  return supportedLocales.includes(saved as Locale) ? (saved as Locale) : 'zh-CN'
}

function readInitialView(): ActiveView {
  const hash = window.location.hash.replace(/^#/, '')
  return supportedViews.includes(hash as ActiveView) ? (hash as ActiveView) : 'markets'
}

function readInitialStrategyId() {
  const strategyId = new URLSearchParams(window.location.search).get('strategy')?.trim()
  return strategyId ? strategyId : null
}

function readInitialRunSessionId() {
  const sessionId = new URLSearchParams(window.location.search).get('runSessionId')?.trim()
  return sessionId ? sessionId : ''
}

function readInitialProvenanceHash() {
  const provenanceHash = new URLSearchParams(window.location.search).get('provenance')?.trim()
  return provenanceHash ? provenanceHash : ''
}

function readInitialSubscriberWallet() {
  const wallet = new URLSearchParams(window.location.search).get('wallet')?.trim()
  if (wallet) {
    return wallet
  }

  return window.localStorage.getItem('autotrade.arcSubscriberWallet')?.trim() ?? ''
}

function writeWorkspaceLocation(view: ActiveView, strategyId: string | null) {
  const params = new URLSearchParams(window.location.search)
  if (strategyId) {
    params.set('strategy', strategyId)
  } else {
    params.delete('strategy')
  }

  const query = params.toString()
  const nextPath = `${window.location.pathname}${query ? `?${query}` : ''}#${view}`
  window.history.replaceState(null, '', nextPath)
}

export default function App() {
  const [locale, setLocaleState] = useState<Locale>(readInitialLocale)

  const setLocale = useCallback((nextLocale: Locale) => {
    window.localStorage.setItem('autotrade.locale', nextLocale)
    setLocaleState(nextLocale)
  }, [])

  return (
    <IntlProvider locale={locale} messages={messages[locale]}>
      <ControlRoom locale={locale} onLocaleChange={setLocale} />
    </IntlProvider>
  )
}

function ControlRoom({
  locale,
  onLocaleChange
}: {
  locale: Locale
  onLocaleChange: (locale: Locale) => void
}) {
  const intl = useIntl()
  const [snapshot, setSnapshot] = useState<ControlRoomSnapshot | null>(null)
  const [readiness, setReadiness] = useState<ReadinessReport | null>(null)
  const [marketResponse, setMarketResponse] = useState<ControlRoomMarketsResponse | null>(null)
  const [selectedMarketId, setSelectedMarketId] = useState<string | null>(null)
  const [marketDetail, setMarketDetail] = useState<ControlRoomMarketDetailResponse | null>(null)
  const [selectedToken, setSelectedToken] = useState<ControlRoomMarketToken | null>(null)
  const [selectedStrategyId, setSelectedStrategyIdState] = useState<string | null>(readInitialStrategyId)
  const [strategyDecisionResponse, setStrategyDecisionResponse] = useState<StrategyDecisionListResponse | null>(null)
  const [strategyParameterSnapshot, setStrategyParameterSnapshot] = useState<StrategyParameterSnapshot | null>(null)
  const [reportSessionId, setReportSessionId] = useState(readInitialRunSessionId)
  const [loadedReportSessionId, setLoadedReportSessionId] = useState(readInitialRunSessionId)
  const [activeReportSession, setActiveReportSession] = useState<PaperRunSessionRecord | null>(null)
  const [runReport, setRunReport] = useState<PaperRunReport | null>(null)
  const [promotionChecklist, setPromotionChecklist] = useState<PaperPromotionChecklist | null>(null)
  const [riskEventIdInput, setRiskEventIdInput] = useState('')
  const [loadedRiskEventId, setLoadedRiskEventId] = useState('')
  const [riskDrilldown, setRiskDrilldown] = useState<RiskEventDrilldown | null>(null)
  const [exposureDrilldown, setExposureDrilldown] = useState<UnhedgedExposureDrilldownResponse | null>(null)
  const [incidentActions, setIncidentActions] = useState<IncidentActionCatalog | null>(null)
  const [agentReputation, setAgentReputation] = useState<ArcAgentReputation | null>(null)
  const [strategyReputation, setStrategyReputation] = useState<ArcAgentReputation | null>(null)
  const [provenanceHashInput, setProvenanceHashInput] = useState(readInitialProvenanceHash)
  const [loadedProvenanceHash, setLoadedProvenanceHash] = useState(readInitialProvenanceHash)
  const [provenanceExplanation, setProvenanceExplanation] = useState<ArcSubscriberProvenanceExplanation | null>(null)
  const [incidentCancelScope, setIncidentCancelScope] = useState<IncidentCancelScope>('all')
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('all')
  const [marketStatus, setMarketStatus] = useState('all')
  const [sort, setSort] = useState('rank')
  const [minLiquidity, setMinLiquidity] = useState('')
  const [minVolume24h, setMinVolume24h] = useState('')
  const [maxDaysToExpiry, setMaxDaysToExpiry] = useState('')
  const [acceptingOrders, setAcceptingOrders] = useState('all')
  const [minSignalScore, setMinSignalScore] = useState('')
  const [activeView, setActiveViewState] = useState<ActiveView>(readInitialView)
  const [error, setError] = useState<string | null>(null)
  const [readinessError, setReadinessError] = useState<string | null>(null)
  const [reportError, setReportError] = useState<string | null>(null)
  const [riskDrilldownError, setRiskDrilldownError] = useState<string | null>(null)
  const [incidentActionError, setIncidentActionError] = useState<string | null>(null)
  const [performanceError, setPerformanceError] = useState<string | null>(null)
  const [provenanceError, setProvenanceError] = useState<string | null>(null)
  const [orderBookError, setOrderBookError] = useState<string | null>(null)
  const [strategyDecisionError, setStrategyDecisionError] = useState<string | null>(null)
  const [strategyParameterError, setStrategyParameterError] = useState<string | null>(null)
  const [commandResult, setCommandResult] = useState<CommandResult | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  const [isDetailLoading, setIsDetailLoading] = useState(false)
  const [isLoadingMoreMarkets, setIsLoadingMoreMarkets] = useState(false)
  const [isOrderBookRefreshing, setIsOrderBookRefreshing] = useState(false)
  const [isStrategyDecisionLoading, setIsStrategyDecisionLoading] = useState(false)
  const [isStrategyParameterLoading, setIsStrategyParameterLoading] = useState(false)
  const [isReportLoading, setIsReportLoading] = useState(false)
  const [isRiskDrilldownLoading, setIsRiskDrilldownLoading] = useState(false)
  const [isIncidentActionLoading, setIsIncidentActionLoading] = useState(false)
  const [isPerformanceLoading, setIsPerformanceLoading] = useState(false)
  const [isProvenanceLoading, setIsProvenanceLoading] = useState(false)
  const [busyCommand, setBusyCommand] = useState<string | null>(null)
  const loadedMarketCountRef = useRef(0)
  const loadMoreMarkerRef = useRef<HTMLDivElement | null>(null)

  const marketQuery = useMemo(
    () => ({
      search,
      category,
      status: marketStatus,
      sort,
      minLiquidity: parseOptionalNumber(minLiquidity),
      minVolume24h: parseOptionalNumber(minVolume24h),
      maxDaysToExpiry: parseOptionalNumber(maxDaysToExpiry),
      acceptingOrders: acceptingOrders === 'all' ? undefined : acceptingOrders === 'true',
      minSignalScore: parseOptionalNumber(minSignalScore)
    }),
    [acceptingOrders, category, marketStatus, maxDaysToExpiry, minLiquidity, minSignalScore, minVolume24h, search, sort]
  )

  const setActiveView = useCallback((view: ActiveView) => {
    setActiveViewState(view)
    writeWorkspaceLocation(view, selectedStrategyId)
  }, [selectedStrategyId])

  const selectStrategy = useCallback((strategyId: string) => {
    setSelectedStrategyIdState(strategyId)
    setActiveViewState('ops')
    writeWorkspaceLocation('ops', strategyId)
  }, [])

  useEffect(() => {
    loadedMarketCountRef.current = marketResponse?.markets.length ?? 0
  }, [marketResponse?.markets.length])

  const loadDashboard = useCallback(
    async (signal?: AbortSignal) => {
      setIsLoading(true)
      try {
        const marketLimit = Math.max(MARKET_PAGE_SIZE, loadedMarketCountRef.current)
        const readinessRequest = getReadinessReport(signal)
          .then((nextReadiness) => {
            setReadinessError(null)
            return nextReadiness
          })
          .catch((readinessRequestError) => {
            if (readinessRequestError instanceof DOMException && readinessRequestError.name === 'AbortError') {
              throw readinessRequestError
            }

            setReadinessError(readinessRequestError instanceof Error
              ? readinessRequestError.message
              : 'Readiness diagnostics failed.')
            return null
          })
        const [nextSnapshot, nextMarkets, nextReadiness] = await Promise.all([
          getControlRoomSnapshot(signal),
          getMarkets({ ...marketQuery, limit: marketLimit, offset: 0 }, signal),
          readinessRequest
        ])

        setSnapshot(nextSnapshot)
        if (nextReadiness) {
          setReadiness(nextReadiness)
        }
        setMarketResponse(nextMarkets)
        setSelectedMarketId((current) => {
          if (current && nextMarkets.markets.some((market) => market.marketId === current)) {
            return current
          }

          return nextMarkets.markets[0]?.marketId ?? null
        })
        setError(null)
      } catch (requestError) {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return
        }

        setError(requestError instanceof Error ? requestError.message : 'Failed to load dashboard.')
      } finally {
        setIsLoading(false)
      }
    },
    [marketQuery]
  )

  const loadReport = useCallback(
    async (sessionId: string, signal?: AbortSignal) => {
      const normalizedSessionId = sessionId.trim()
      if (!normalizedSessionId) {
        setReportError('Run session id is required.')
        setRunReport(null)
        setPromotionChecklist(null)
        return
      }

      setIsReportLoading(true)
      try {
        const [nextReport, nextChecklist] = await Promise.all([
          getRunReport(normalizedSessionId, 1000, signal),
          getPromotionChecklist(normalizedSessionId, 1000, signal)
        ])

        setRunReport(nextReport)
        setPromotionChecklist(nextChecklist)
        setReportError(null)
      } catch (requestError) {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return
        }

        setRunReport(null)
        setPromotionChecklist(null)
        setReportError(requestError instanceof Error ? requestError.message : 'Run report request failed.')
      } finally {
        if (!signal?.aborted) {
          setIsReportLoading(false)
        }
      }
    },
    []
  )

  const loadActiveReportSession = useCallback(
    async (signal?: AbortSignal) => {
      try {
        const activeSession = await getActiveRunSession(signal)
        setActiveReportSession(activeSession)

        if (activeSession && !reportSessionId.trim()) {
          setReportSessionId(activeSession.sessionId)
          setLoadedReportSessionId(activeSession.sessionId)
        }
      } catch (requestError) {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return
        }

        setActiveReportSession(null)
      }
    },
    [reportSessionId]
  )

  const loadRiskDrilldown = useCallback(
    async (riskEventId: string, signal?: AbortSignal) => {
      const normalizedRiskEventId = riskEventId.trim()
      setIsRiskDrilldownLoading(true)
      try {
        const [nextEvent, nextExposures] = await Promise.all([
          normalizedRiskEventId ? getRiskEventDrilldown(normalizedRiskEventId, signal) : Promise.resolve(null),
          getUnhedgedExposures(
            {
              riskEventId: normalizedRiskEventId || undefined,
              limit: 20
            },
            signal
          )
        ])

        setRiskDrilldown(nextEvent)
        setExposureDrilldown(nextExposures)
        setRiskDrilldownError(null)
      } catch (requestError) {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return
        }

        setRiskDrilldown(null)
        setRiskDrilldownError(requestError instanceof Error ? requestError.message : 'Risk drill-down request failed.')
      } finally {
        if (!signal?.aborted) {
          setIsRiskDrilldownLoading(false)
        }
      }
    },
    []
  )

  const loadIncidentActions = useCallback(
    async (signal?: AbortSignal) => {
      setIsIncidentActionLoading(true)
      try {
        const nextActions = await getIncidentActions(signal)
        setIncidentActions(nextActions)
        setIncidentActionError(null)
      } catch (requestError) {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return
        }

        setIncidentActions(null)
        setIncidentActionError(requestError instanceof Error ? requestError.message : 'Incident action request failed.')
      } finally {
        if (!signal?.aborted) {
          setIsIncidentActionLoading(false)
        }
      }
    },
    []
  )

  const loadPerformance = useCallback(
    async (signal?: AbortSignal) => {
      setIsPerformanceLoading(true)
      try {
        const strategyRequest = selectedStrategyId
          ? getArcStrategyReputation(selectedStrategyId, signal)
          : Promise.resolve(null)
        const [nextAgentReputation, nextStrategyReputation] = await Promise.all([
          getArcAgentReputation(signal),
          strategyRequest
        ])

        setAgentReputation(nextAgentReputation)
        setStrategyReputation(nextStrategyReputation)
        setPerformanceError(null)
      } catch (requestError) {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return
        }

        setPerformanceError(requestError instanceof Error ? requestError.message : 'Arc performance request failed.')
      } finally {
        if (!signal?.aborted) {
          setIsPerformanceLoading(false)
        }
      }
    },
    [selectedStrategyId]
  )

  const loadProvenance = useCallback(
    async (provenanceHash: string, signal?: AbortSignal) => {
      const normalizedHash = provenanceHash.trim()
      if (!normalizedHash) {
        setProvenanceExplanation(null)
        setProvenanceError(null)
        return
      }

      setIsProvenanceLoading(true)
      try {
        const nextProvenance = await getArcProvenance(normalizedHash, signal)
        setProvenanceExplanation(nextProvenance)
        setProvenanceError(null)
      } catch (requestError) {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return
        }

        setProvenanceExplanation(null)
        setProvenanceError(requestError instanceof Error ? requestError.message : 'Arc provenance request failed.')
      } finally {
        if (!signal?.aborted) {
          setIsProvenanceLoading(false)
        }
      }
    },
    []
  )

  const loadMoreMarkets = useCallback(
    async () => {
      if (!marketResponse || isLoadingMoreMarkets || (marketResponse.isComplete && marketResponse.markets.length >= marketResponse.totalCount)) {
        return
      }

      setIsLoadingMoreMarkets(true)
      try {
        const nextMarkets = await getMarkets({
          ...marketQuery,
          limit: MARKET_PAGE_SIZE,
          offset: marketResponse.markets.length
        })

        setMarketResponse((current) => {
          if (!current) {
            return nextMarkets
          }

          const knownMarketIds = new Set(current.markets.map((market) => market.marketId))
          const appendedMarkets = nextMarkets.markets.filter((market) => !knownMarketIds.has(market.marketId))
          const totalCount = nextMarkets.isComplete
            ? nextMarkets.totalCount
            : Math.max(nextMarkets.totalCount, current.markets.length + appendedMarkets.length + 1)

          return {
            ...nextMarkets,
            categories: nextMarkets.categories.length > 0 ? nextMarkets.categories : current.categories,
            totalCount,
            markets: [...current.markets, ...appendedMarkets]
          }
        })
        setError(null)
      } catch (requestError) {
        setError(requestError instanceof Error ? requestError.message : 'Failed to load more markets.')
      } finally {
        setIsLoadingMoreMarkets(false)
      }
    },
    [isLoadingMoreMarkets, marketQuery, marketResponse]
  )

  const hasMoreMarkets = marketResponse ? !marketResponse.isComplete || marketResponse.markets.length < marketResponse.totalCount : false

  const loadMarketDetail = useCallback(
    async (marketId: string, signal?: AbortSignal) => {
      setIsDetailLoading(true)
      try {
        const detail = await getMarketDetail(marketId, 14, signal)
        setMarketDetail(detail)
        setSelectedToken(
          detail.market.tokens.find((token) => token.outcome === detail.orderBook?.outcome) ??
          detail.market.tokens[0] ??
          null
        )
        setOrderBookError(null)
        setError(null)
      } catch (requestError) {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return
        }

        setError(requestError instanceof Error ? requestError.message : 'Failed to load market detail.')
      } finally {
        setIsDetailLoading(false)
      }
    },
    []
  )

  useEffect(() => {
    const controller = new AbortController()
    void loadDashboard(controller.signal)
    const timer = window.setInterval(() => {
      void loadDashboard()
    }, 15_000)

    return () => {
      controller.abort()
      window.clearInterval(timer)
    }
  }, [loadDashboard])

  useEffect(() => {
    if (activeView !== 'reports' || loadedReportSessionId.trim()) {
      return
    }

    const controller = new AbortController()
    void loadActiveReportSession(controller.signal)
    return () => controller.abort()
  }, [activeView, loadActiveReportSession, loadedReportSessionId])

  useEffect(() => {
    if (activeView !== 'reports' || !loadedReportSessionId.trim()) {
      return
    }

    const controller = new AbortController()
    void loadReport(loadedReportSessionId, controller.signal)
    return () => controller.abort()
  }, [activeView, loadReport, loadedReportSessionId])

  useEffect(() => {
    if (activeView !== 'ops') {
      return
    }

    const controller = new AbortController()
    void loadRiskDrilldown(loadedRiskEventId, controller.signal)
    return () => controller.abort()
  }, [activeView, loadRiskDrilldown, loadedRiskEventId])

  useEffect(() => {
    if (activeView !== 'ops') {
      return
    }

    const controller = new AbortController()
    void loadIncidentActions(controller.signal)
    return () => controller.abort()
  }, [activeView, loadIncidentActions, snapshot?.timestampUtc])

  useEffect(() => {
    if (activeView !== 'performance') {
      return
    }

    const controller = new AbortController()
    void loadPerformance(controller.signal)
    return () => controller.abort()
  }, [activeView, loadPerformance])

  useEffect(() => {
    if (activeView !== 'performance') {
      return
    }

    if (!loadedProvenanceHash.trim()) {
      setProvenanceExplanation(null)
      setProvenanceError(null)
      return
    }

    const controller = new AbortController()
    void loadProvenance(loadedProvenanceHash, controller.signal)
    return () => controller.abort()
  }, [activeView, loadProvenance, loadedProvenanceHash])

  useEffect(() => {
    if (activeView !== 'markets' || !hasMoreMarkets || isLoadingMoreMarkets || typeof IntersectionObserver === 'undefined') {
      return
    }

    const marker = loadMoreMarkerRef.current
    if (!marker) {
      return
    }

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          void loadMoreMarkets()
        }
      },
      { rootMargin: '560px 0px 560px 0px' }
    )

    observer.observe(marker)
    return () => observer.disconnect()
  }, [activeView, hasMoreMarkets, isLoadingMoreMarkets, loadMoreMarkets])

  useEffect(() => {
    if (!selectedMarketId) {
      setMarketDetail(null)
      setSelectedToken(null)
      setOrderBookError(null)
      return
    }

    const controller = new AbortController()
    void loadMarketDetail(selectedMarketId, controller.signal)

    return () => controller.abort()
  }, [loadMarketDetail, selectedMarketId])

  useEffect(() => {
    const strategies = snapshot?.strategies ?? []
    if (strategies.length === 0) {
      setSelectedStrategyIdState(null)
      setStrategyDecisionResponse(null)
      setStrategyDecisionError(null)
      return
    }

    const strategyIdFromUrl = readInitialStrategyId()
    setSelectedStrategyIdState((current) => {
      if (strategyIdFromUrl && strategies.some((strategy) => strategy.strategyId === strategyIdFromUrl)) {
        return strategyIdFromUrl
      }

      if (current && strategies.some((strategy) => strategy.strategyId === current)) {
        return current
      }

      return strategies[0].strategyId
    })
  }, [snapshot?.strategies])

  useEffect(() => {
    if (!selectedStrategyId) {
      setStrategyDecisionResponse(null)
      setStrategyDecisionError(null)
      return
    }

    const controller = new AbortController()
    setIsStrategyDecisionLoading(true)
    void getStrategyDecisions(
      { strategyId: selectedStrategyId, limit: STRATEGY_DECISION_LIMIT },
      controller.signal
    )
      .then((response) => {
        setStrategyDecisionResponse(response)
        setStrategyDecisionError(null)
      })
      .catch((requestError) => {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return
        }

        setStrategyDecisionResponse(null)
        setStrategyDecisionError(requestError instanceof Error
          ? requestError.message
          : 'Strategy decisions request failed.')
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsStrategyDecisionLoading(false)
        }
      })

    return () => controller.abort()
  }, [selectedStrategyId, snapshot?.timestampUtc])

  useEffect(() => {
    if (!selectedStrategyId) {
      setStrategyParameterSnapshot(null)
      setStrategyParameterError(null)
      return
    }

    const controller = new AbortController()
    setIsStrategyParameterLoading(true)
    void getStrategyParameters(selectedStrategyId, 8, controller.signal)
      .then((response) => {
        setStrategyParameterSnapshot(response)
        setStrategyParameterError(null)
      })
      .catch((requestError) => {
        if (requestError instanceof DOMException && requestError.name === 'AbortError') {
          return
        }

        setStrategyParameterSnapshot(null)
        setStrategyParameterError(requestError instanceof Error
          ? requestError.message
          : 'Strategy parameters request failed.')
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsStrategyParameterLoading(false)
        }
      })

    return () => controller.abort()
  }, [selectedStrategyId])

  const runStrategyCommand = useCallback(
    async (strategyId: string, targetState: TargetStrategyState) => {
      const commandKey = `${strategyId}:${targetState}`
      const intent = buildStrategyCommandIntent(snapshot, targetState)
      if (intent === null) {
        setCommandResult({ tone: 'watch', text: 'Command cancelled' })
        return
      }

      setBusyCommand(commandKey)
      try {
        const response = await setStrategyState(strategyId, targetState, intent)
        applyCommandResponse(response, setSnapshot, setCommandResult)
        setError(null)
      } catch (commandError) {
        setCommandResult(null)
        setError(sanitizeCommandMessage(commandError instanceof Error ? commandError.message : 'Strategy command failed.'))
      } finally {
        setBusyCommand(null)
      }
    },
    [snapshot]
  )

  const runStrategyParameterUpdate = useCallback(
    async (strategyId: string, changes: Record<string, string>) => {
      const commandKey = `${strategyId}:parameters:update`
      const intent = buildParameterMutationIntent(snapshot, 'Control room strategy parameter update')
      if (intent === null) {
        setCommandResult({ tone: 'watch', text: 'Command cancelled' })
        return
      }

      setBusyCommand(commandKey)
      try {
        const response = await updateStrategyParameters(strategyId, changes, intent)
        setStrategyParameterSnapshot(response.snapshot)
        setCommandResult({
          tone: response.accepted ? 'good' : 'danger',
          text: `${response.status}: ${sanitizeCommandMessage(response.message)}`
        })
        setError(null)
      } catch (commandError) {
        setCommandResult(null)
        setError(sanitizeCommandMessage(commandError instanceof Error
          ? commandError.message
          : 'Strategy parameter update failed.'))
      } finally {
        setBusyCommand(null)
      }
    },
    [snapshot]
  )

  const runStrategyParameterRollback = useCallback(
    async (strategyId: string, versionId: string) => {
      const commandKey = `${strategyId}:parameters:rollback:${versionId}`
      const intent = buildParameterMutationIntent(snapshot, 'Control room strategy parameter rollback')
      if (intent === null) {
        setCommandResult({ tone: 'watch', text: 'Command cancelled' })
        return
      }

      setBusyCommand(commandKey)
      try {
        const response = await rollbackStrategyParameters(strategyId, versionId, intent)
        setStrategyParameterSnapshot(response.snapshot)
        setCommandResult({
          tone: response.accepted ? 'good' : 'danger',
          text: `${response.status}: ${sanitizeCommandMessage(response.message)}`
        })
        setError(null)
      } catch (commandError) {
        setCommandResult(null)
        setError(sanitizeCommandMessage(commandError instanceof Error
          ? commandError.message
          : 'Strategy parameter rollback failed.'))
      } finally {
        setBusyCommand(null)
      }
    },
    [snapshot]
  )

  const runKillSwitchCommand = useCallback(async (active: boolean) => {
    const commandKey = active ? 'kill-switch:on' : 'kill-switch:off'
    const intent = buildKillSwitchIntent(active)
    if (intent === null) {
      setCommandResult({ tone: 'watch', text: 'Command cancelled' })
      return
    }

    setBusyCommand(commandKey)
    try {
      const response = await setKillSwitch(active, intent)
      applyCommandResponse(response, setSnapshot, setCommandResult)
      setError(null)
    } catch (commandError) {
      setCommandResult(null)
      setError(sanitizeCommandMessage(commandError instanceof Error ? commandError.message : 'Kill switch command failed.'))
    } finally {
      setBusyCommand(null)
    }
  }, [])

  const runCancelOpenOrdersCommand = useCallback(async () => {
    const strategyId = incidentCancelScope === 'strategy' ? selectedStrategyId : null
    if (incidentCancelScope === 'strategy' && !strategyId) {
      setCommandResult({ tone: 'watch', text: 'Select a strategy before cancelling scoped open orders.' })
      return
    }

    const confirmationText = requestTypedConfirmation()
    if (confirmationText === null) {
      setCommandResult({ tone: 'watch', text: 'Command cancelled' })
      return
    }

    const commandKey = 'incident:cancel-open-orders'
    setBusyCommand(commandKey)
    try {
      const response = await cancelOpenOrders({
        actor: 'control-room-ui',
        reasonCode: 'UI_INCIDENT_CANCEL_OPEN_ORDERS',
        reason: strategyId
          ? `Control room incident response cancel open orders for ${strategyId}`
          : 'Control room incident response cancel open orders',
        strategyId: strategyId ?? undefined,
        confirmationText
      })
      applyCommandResponse(response, setSnapshot, setCommandResult)
      setError(null)
      void loadIncidentActions()
    } catch (commandError) {
      setCommandResult(null)
      setError(sanitizeCommandMessage(commandError instanceof Error
        ? commandError.message
        : 'Cancel open orders command failed.'))
    } finally {
      setBusyCommand(null)
    }
  }, [incidentCancelScope, loadIncidentActions, selectedStrategyId])

  const refreshOrderBook = useCallback(
    async (token: ControlRoomMarketToken, showBusy = false) => {
      const marketId = marketDetail?.market.marketId
      if (!marketId) {
        return
      }

      if (showBusy) {
        setBusyCommand(`book:${token.tokenId}`)
      } else {
        setIsOrderBookRefreshing(true)
      }

      try {
        const orderBook = await getOrderBook(marketId, token.tokenId, token.outcome, 14)
        setMarketDetail((current) => current?.market.marketId === marketId
          ? { ...current, orderBook }
          : current)
        setOrderBookError(null)
        setError(null)
      } catch (requestError) {
        setOrderBookError(requestError instanceof Error ? requestError.message : 'Order book request failed.')
      } finally {
        if (showBusy) {
          setBusyCommand(null)
        } else {
          setIsOrderBookRefreshing(false)
        }
      }
    },
    [marketDetail?.market.marketId]
  )

  const selectToken = useCallback(
    async (token: ControlRoomMarketToken) => {
      if (!marketDetail) {
        return
      }

      setSelectedToken(token)
      await refreshOrderBook(token, true)
    },
    [marketDetail, refreshOrderBook]
  )

  useEffect(() => {
    if (activeView !== 'trade' || !selectedToken || !marketDetail) {
      return
    }

    const timer = window.setInterval(() => {
      void refreshOrderBook(selectedToken, false)
    }, 10_000)

    return () => window.clearInterval(timer)
  }, [activeView, marketDetail?.market.marketId, refreshOrderBook, selectedToken])

  const statusLabel = useMemo(() => {
    if (error) {
      return intl.formatMessage({ id: 'status.offline' })
    }

    if (isLoading && !snapshot) {
      return intl.formatMessage({ id: 'status.connecting' })
    }

    return snapshot?.process.apiStatus ?? intl.formatMessage({ id: 'status.unknown' })
  }, [error, intl, isLoading, snapshot])

  const selectedMarket = useMemo(() => {
    return marketResponse?.markets.find((market) => market.marketId === selectedMarketId) ?? null
  }, [marketResponse, selectedMarketId])
  const selectedStrategy = useMemo(() => {
    return snapshot?.strategies.find((strategy) => strategy.strategyId === selectedStrategyId) ?? null
  }, [selectedStrategyId, snapshot?.strategies])
  const knownMarkets = useMemo(() => {
    const byId = new Map<string, ControlRoomMarket>()
    snapshot?.markets.forEach((market) => byId.set(market.marketId, market))
    marketResponse?.markets.forEach((market) => byId.set(market.marketId, market))
    if (marketDetail?.market) {
      byId.set(marketDetail.market.marketId, marketDetail.market)
    }

    return Array.from(byId.values())
  }, [marketDetail?.market, marketResponse?.markets, snapshot?.markets])
  const commandSafety = useMemo(
    () => resolveCommandSafety(snapshot, Boolean(error)),
    [error, snapshot]
  )

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand-block">
          <p className="eyebrow">{intl.formatMessage({ id: 'app.eyebrow' })}</p>
          <h1>{intl.formatMessage({ id: 'app.title' })}</h1>
          <span>{intl.formatMessage({ id: 'app.subtitle' })}</span>
        </div>
        <div className="topbar-actions">
          <span className={`status-pill ${error ? 'down' : ''}`}>{statusLabel}</span>
          <button
            className="icon-button"
            type="button"
            title={intl.formatMessage({ id: 'action.swagger' })}
            onClick={() => window.open(`${apiBaseUrl}/swagger`, '_blank', 'noopener,noreferrer')}
          >
            <BookOpen size={17} aria-hidden="true" />
            <span>{intl.formatMessage({ id: 'action.swagger' })}</span>
          </button>
          <button
            className="icon-button"
            type="button"
            title={intl.formatMessage({ id: 'action.language' })}
            onClick={() => onLocaleChange(locale === 'zh-CN' ? 'en-US' : 'zh-CN')}
          >
            <Languages size={17} aria-hidden="true" />
            <span>{locale === 'zh-CN' ? '中文' : 'EN'}</span>
          </button>
          <button className="command-button" type="button" onClick={() => void loadDashboard()} disabled={isLoading}>
            <RefreshCw size={16} aria-hidden="true" />
            {intl.formatMessage({ id: 'action.refresh' })}
          </button>
        </div>
      </header>

      {error && <div className="error-strip">{error}</div>}
      {commandResult && <div className={`command-result-strip ${commandResult.tone}`}>{commandResult.text}</div>}

      <main>
        <MetricsStrip metrics={snapshot?.metrics ?? []} />
        <CommandBand
          snapshot={snapshot}
          busyCommand={busyCommand}
          commandSafety={commandSafety}
          onKillSwitch={runKillSwitchCommand}
        />

        <ViewTabs activeView={activeView} onChange={setActiveView} />

        {activeView === 'markets' && (
          <section className="view-grid discovery-grid">
            <MarketDiscoveryPanel
              markets={marketResponse?.markets ?? []}
              categories={marketResponse?.categories ?? []}
              source={marketResponse?.source ?? '-'}
              totalCount={marketResponse?.totalCount ?? 0}
              selectedMarketId={selectedMarketId}
              hasMore={hasMoreMarkets}
              isComplete={marketResponse?.isComplete ?? false}
              isLoadingMore={isLoadingMoreMarkets}
              loadMoreRef={loadMoreMarkerRef}
              search={search}
              category={category}
              marketStatus={marketStatus}
              sort={sort}
              minLiquidity={minLiquidity}
              minVolume24h={minVolume24h}
              maxDaysToExpiry={maxDaysToExpiry}
              acceptingOrders={acceptingOrders}
              minSignalScore={minSignalScore}
              onSearchChange={setSearch}
              onCategoryChange={setCategory}
              onMarketStatusChange={setMarketStatus}
              onSortChange={setSort}
              onMinLiquidityChange={setMinLiquidity}
              onMinVolume24hChange={setMinVolume24h}
              onMaxDaysToExpiryChange={setMaxDaysToExpiry}
              onAcceptingOrdersChange={setAcceptingOrders}
              onMinSignalScoreChange={setMinSignalScore}
              onLoadMore={() => void loadMoreMarkets()}
              onSelect={(marketId) => {
                setSelectedMarketId(marketId)
                setActiveView('trade')
              }}
            />
          </section>
        )}

        {activeView === 'trade' && (
          <section className="view-grid trade-grid">
            <MarketDetailPanel
              market={marketDetail?.market ?? selectedMarket}
              detail={marketDetail}
              selectedToken={selectedToken}
              isLoading={isDetailLoading}
              isOrderBookRefreshing={isOrderBookRefreshing}
              orderBookError={orderBookError}
              busyCommand={busyCommand}
              onSelectToken={selectToken}
              onRefreshOrderBook={() => {
                if (selectedToken) {
                  void refreshOrderBook(selectedToken, true)
                }
              }}
            />
            <aside className="trade-side">
              <RiskStack
                risk={snapshot?.risk ?? null}
                busyCommand={busyCommand}
                commandSafety={commandSafety}
                onKillSwitch={runKillSwitchCommand}
              />
              <OrdersTable orders={marketDetail?.orders ?? []} compact />
            </aside>
          </section>
        )}

        {activeView === 'ops' && (
          <section className="view-grid ops-grid">
            <ReadinessPanel report={readiness} error={readinessError} />
            <RiskStack
              risk={snapshot?.risk ?? null}
              busyCommand={busyCommand}
              commandSafety={commandSafety}
              onKillSwitch={runKillSwitchCommand}
            />
            <StrategyWorkspace
              strategies={snapshot?.strategies ?? []}
              selectedStrategy={selectedStrategy}
              selectedStrategyId={selectedStrategyId}
              decisions={strategyDecisionResponse?.decisions ?? []}
              snapshotDecisions={snapshot?.decisions ?? []}
              parameterSnapshot={strategyParameterSnapshot}
              markets={knownMarkets}
              orders={snapshot?.orders ?? []}
              busyCommand={busyCommand}
              commandSafety={commandSafety}
              isDecisionLoading={isStrategyDecisionLoading}
              decisionError={strategyDecisionError}
              isParameterLoading={isStrategyParameterLoading}
              parameterError={strategyParameterError}
              onSelect={selectStrategy}
              onSetState={runStrategyCommand}
              onUpdateParameters={runStrategyParameterUpdate}
              onRollbackParameters={runStrategyParameterRollback}
            />
            <IncidentResponsePanel
              catalog={incidentActions}
              snapshot={snapshot}
              selectedStrategyId={selectedStrategyId}
              cancelScope={incidentCancelScope}
              busyCommand={busyCommand}
              commandSafety={commandSafety}
              isLoading={isIncidentActionLoading}
              error={incidentActionError}
              riskEventId={loadedRiskEventId}
              onCancelScopeChange={setIncidentCancelScope}
              onKillSwitch={runKillSwitchCommand}
              onSetState={runStrategyCommand}
              onCancelOpenOrders={runCancelOpenOrdersCommand}
              onRefresh={() => void loadIncidentActions()}
            />
            <RiskDrilldownPanel
              riskEventId={riskEventIdInput}
              loadedRiskEventId={loadedRiskEventId}
              drilldown={riskDrilldown}
              exposures={exposureDrilldown}
              isLoading={isRiskDrilldownLoading}
              error={riskDrilldownError}
              onRiskEventIdChange={setRiskEventIdInput}
              onLoad={() => {
                const nextRiskEventId = riskEventIdInput.trim()
                if (nextRiskEventId === loadedRiskEventId) {
                  void loadRiskDrilldown(nextRiskEventId)
                  return
                }

                setLoadedRiskEventId(nextRiskEventId)
              }}
              onClear={() => {
                setRiskEventIdInput('')
                if (loadedRiskEventId === '') {
                  void loadRiskDrilldown('')
                  return
                }

                setLoadedRiskEventId('')
              }}
            />
          </section>
        )}

        {activeView === 'activity' && (
          <section className="view-grid activity-grid">
            <OrdersTable orders={snapshot?.orders ?? []} />
            <PositionsTable positions={snapshot?.positions ?? []} />
            <DecisionList decisions={snapshot?.decisions ?? []} />
          </section>
        )}

        {activeView === 'performance' && (
          <section className="view-grid performance-grid">
            <PerformanceLedgerWorkspace
              agentReputation={agentReputation}
              strategyReputation={strategyReputation}
              selectedStrategyId={selectedStrategyId}
              provenanceHash={provenanceHashInput}
              provenance={provenanceExplanation}
              isLoading={isPerformanceLoading}
              isProvenanceLoading={isProvenanceLoading}
              error={performanceError}
              provenanceError={provenanceError}
              onProvenanceHashChange={setProvenanceHashInput}
              onLoadProvenance={() => {
                const nextProvenanceHash = provenanceHashInput.trim()
                setLoadedProvenanceHash(nextProvenanceHash)
                if (nextProvenanceHash === loadedProvenanceHash) {
                  void loadProvenance(nextProvenanceHash)
                }
              }}
              onRefresh={() => void loadPerformance()}
            />
          </section>
        )}

        {activeView === 'reports' && (
          <section className="view-grid report-grid">
            <RunReportWorkspace
              sessionId={reportSessionId}
              activeSession={activeReportSession}
              report={runReport}
              checklist={promotionChecklist}
              isLoading={isReportLoading}
              error={reportError}
              onSessionIdChange={setReportSessionId}
              onLoad={() => {
                const nextSessionId = reportSessionId.trim()
                setLoadedReportSessionId(nextSessionId)
                if (nextSessionId === loadedReportSessionId) {
                  void loadReport(nextSessionId)
                }
              }}
            />
          </section>
        )}
      </main>
    </div>
  )
}

const ViewTabs = memo(function ViewTabs({
  activeView,
  onChange
}: {
  activeView: ActiveView
  onChange: (view: ActiveView) => void
}) {
  const intl = useIntl()
  const tabs: Array<{ view: ActiveView; icon: ReactNode; label: string; detail: string }> = [
    {
      view: 'markets',
      icon: <Search size={17} aria-hidden="true" />,
      label: intl.formatMessage({ id: 'view.markets' }),
      detail: intl.formatMessage({ id: 'view.markets.detail' })
    },
    {
      view: 'trade',
      icon: <Layers size={17} aria-hidden="true" />,
      label: intl.formatMessage({ id: 'view.trade' }),
      detail: intl.formatMessage({ id: 'view.trade.detail' })
    },
    {
      view: 'ops',
      icon: <ShieldCheck size={17} aria-hidden="true" />,
      label: intl.formatMessage({ id: 'view.ops' }),
      detail: intl.formatMessage({ id: 'view.ops.detail' })
    },
    {
      view: 'activity',
      icon: <Activity size={17} aria-hidden="true" />,
      label: intl.formatMessage({ id: 'view.activity' }),
      detail: intl.formatMessage({ id: 'view.activity.detail' })
    },
    {
      view: 'performance',
      icon: <Gauge size={17} aria-hidden="true" />,
      label: 'Arc proof',
      detail: 'Reputation ledger'
    },
    {
      view: 'reports',
      icon: <FileText size={17} aria-hidden="true" />,
      label: intl.formatMessage({ id: 'view.reports' }),
      detail: intl.formatMessage({ id: 'view.reports.detail' })
    }
  ]

  return (
    <nav className="view-tabs" aria-label="Primary workspace">
      {tabs.map((tab) => (
        <button
          className={tab.view === activeView ? 'active' : ''}
          key={tab.view}
          type="button"
          onClick={() => onChange(tab.view)}
        >
          {tab.icon}
          <span>
            <strong>{tab.label}</strong>
            <small>{tab.detail}</small>
          </span>
        </button>
      ))}
    </nav>
  )
})

const MetricsStrip = memo(function MetricsStrip({ metrics }: { metrics: ControlRoomMetric[] }) {
  const intl = useIntl()

  if (metrics.length === 0) {
    return <section className="metrics-strip skeleton" aria-label="System metrics" />
  }

  return (
    <section className="metrics-strip" aria-label="System metrics">
      {metrics.map((metric) => (
        <article className={`metric-cell ${metric.tone}`} key={metric.label}>
          <span>{translateMetricLabel(metric.label, (descriptor) => intl.formatMessage(descriptor))}</span>
          <strong>{metric.value}</strong>
          <small>{metric.delta}</small>
        </article>
      ))}
    </section>
  )
})

const CommandBand = memo(function CommandBand({
  snapshot,
  busyCommand,
  commandSafety,
  onKillSwitch
}: {
  snapshot: ControlRoomSnapshot | null
  busyCommand: string | null
  commandSafety: CommandSafety
  onKillSwitch: (active: boolean) => Promise<void>
}) {
  const intl = useIntl()
  const risk = snapshot?.risk
  const killSwitchActive = risk?.killSwitchActive ?? false
  const hardStopDisabledReason = resolveKillSwitchDisabledReason(true, risk ?? null, busyCommand, commandSafety)
  const resetDisabledReason = resolveKillSwitchDisabledReason(false, risk ?? null, busyCommand, commandSafety)

  return (
    <section className="command-band" aria-label="Operating state">
      <Readout label={intl.formatMessage({ id: 'readout.mode' })} value={snapshot?.process.executionMode ?? '-'} />
      <Readout label={intl.formatMessage({ id: 'readout.data' })} value={snapshot?.dataMode ?? '-'} />
      <Readout label={intl.formatMessage({ id: 'readout.command' })} value={snapshot?.commandMode ?? '-'} />
      <Readout label={intl.formatMessage({ id: 'readout.api' })} value={apiBaseUrl} variant="api" />
      <Readout label={intl.formatMessage({ id: 'readout.updated' })} value={snapshot ? formatTime(snapshot.timestampUtc) : '-'} />
      <div className={`kill-switch-panel ${killSwitchActive ? 'armed' : ''}`}>
        <div>
          <span>{intl.formatMessage({ id: 'risk.killSwitch' })}</span>
          <strong>{killSwitchActive ? risk?.killSwitchLevel : intl.formatMessage({ id: 'risk.inactive' })}</strong>
          <small className={`mode-chip ${commandSafety.level}`}>{commandSafety.label}</small>
        </div>
        <div className="button-row">
          <button
            className="danger-button"
            type="button"
            title={hardStopDisabledReason ?? intl.formatMessage({ id: 'action.hardStop' })}
            onClick={() => void onKillSwitch(true)}
            disabled={Boolean(hardStopDisabledReason)}
          >
            <OctagonX size={16} aria-hidden="true" />
            {intl.formatMessage({ id: 'action.hardStop' })}
          </button>
          <button
            className="ghost-button"
            type="button"
            title={resetDisabledReason ?? intl.formatMessage({ id: 'action.reset' })}
            onClick={() => void onKillSwitch(false)}
            disabled={Boolean(resetDisabledReason)}
          >
            <ShieldCheck size={16} aria-hidden="true" />
            {intl.formatMessage({ id: 'action.reset' })}
          </button>
          {(hardStopDisabledReason || resetDisabledReason) && (
            <span className="control-reason">{hardStopDisabledReason ?? resetDisabledReason}</span>
          )}
        </div>
      </div>
    </section>
  )
})

function Readout({ label, value, variant }: { label: string; value: string; variant?: 'api' }) {
  return (
    <div className={`process-readout ${variant ? `${variant}-readout` : ''}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

const ReadinessPanel = memo(function ReadinessPanel({
  report,
  error
}: {
  report: ReadinessReport | null
  error: string | null
}) {
  const sortedChecks = useMemo(
    () => [...(report?.checks ?? [])].sort(compareReadinessChecks),
    [report]
  )

  if (!report) {
    return (
      <section className="surface readiness-panel" aria-label="First-run readiness">
        <div className="section-head compact">
          <div>
            <p className="eyebrow">First run</p>
            <h2>Readiness</h2>
          </div>
          <ShieldCheck size={20} aria-hidden="true" />
        </div>
        <div className="empty-panel">{error ?? 'Waiting for readiness diagnostics.'}</div>
      </section>
    )
  }

  return (
    <section className="surface readiness-panel" aria-label="First-run readiness">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">First run</p>
          <h2>Readiness</h2>
        </div>
        <span className={`readiness-overall ${readinessOverallTone(report.status)}`}>
          {formatReadinessStatus(report.status)}
        </span>
      </div>

      {error && <div className="readiness-warning">{error}</div>}

      <div className="readiness-summary">
        <div className={`readiness-state ${readinessOverallTone(report.status)}`}>
          <span>Overall</span>
          <strong>{formatReadinessStatus(report.status)}</strong>
          <small>{formatDate(report.checkedAtUtc)}</small>
          <small>{report.contractVersion}</small>
        </div>

        <div className="readiness-counts" aria-label="Readiness status counts">
          {readinessStatusSummary.map((status) => (
            <MetricPill
              key={status}
              label={formatReadinessStatus(status)}
              value={String(report.checks.filter((check) => check.status === status).length)}
              tone={status === 'Ready' ? 'good' : status === 'Degraded' ? 'watch' : undefined}
            />
          ))}
        </div>
      </div>

      <div className="readiness-capabilities" aria-label="Readiness capabilities">
        {report.capabilities.map((capability) => (
          <div
            className={`readiness-capability ${readinessOverallTone(capability.status)}`}
            key={capability.capability}
          >
            <div>
              <span>{formatReadinessCapability(capability.capability)}</span>
              <strong>{formatReadinessStatus(capability.status)}</strong>
            </div>
            <small>{capability.summary}</small>
            {capability.blockingCheckIds.length > 0 && (
              <code>{capability.blockingCheckIds.join(', ')}</code>
            )}
          </div>
        ))}
      </div>

      <div className="readiness-check-list" aria-label="Readiness checks">
        {sortedChecks.map((check) => (
          <article className={`readiness-check ${readinessCheckTone(check.status)}`} key={check.id}>
            <div className="readiness-check-status">
              <span className={`readiness-badge ${readinessCheckTone(check.status)}`}>
                {formatReadinessStatus(check.status)}
              </span>
              <small>{check.requirement}</small>
            </div>
            <div className="readiness-check-copy">
              <strong>{check.id}</strong>
              <span>{check.summary}</span>
              <small>{check.remediationHint}</small>
            </div>
            <div className="readiness-check-meta">
              <span>{check.source}</span>
              <small>{formatTime(check.lastCheckedAtUtc)}</small>
            </div>
          </article>
        ))}
      </div>
    </section>
  )
})

const MarketDiscoveryPanel = memo(function MarketDiscoveryPanel({
  markets,
  categories,
  source,
  totalCount,
  selectedMarketId,
  hasMore,
  isComplete,
  isLoadingMore,
  loadMoreRef,
  search,
  category,
  marketStatus,
  sort,
  minLiquidity,
  minVolume24h,
  maxDaysToExpiry,
  acceptingOrders,
  minSignalScore,
  onSearchChange,
  onCategoryChange,
  onMarketStatusChange,
  onSortChange,
  onMinLiquidityChange,
  onMinVolume24hChange,
  onMaxDaysToExpiryChange,
  onAcceptingOrdersChange,
  onMinSignalScoreChange,
  onLoadMore,
  onSelect
}: {
  markets: ControlRoomMarket[]
  categories: string[]
  source: string
  totalCount: number
  selectedMarketId: string | null
  hasMore: boolean
  isComplete: boolean
  isLoadingMore: boolean
  loadMoreRef: RefObject<HTMLDivElement | null>
  search: string
  category: string
  marketStatus: string
  sort: string
  minLiquidity: string
  minVolume24h: string
  maxDaysToExpiry: string
  acceptingOrders: string
  minSignalScore: string
  onSearchChange: (value: string) => void
  onCategoryChange: (value: string) => void
  onMarketStatusChange: (value: string) => void
  onSortChange: (value: string) => void
  onMinLiquidityChange: (value: string) => void
  onMinVolume24hChange: (value: string) => void
  onMaxDaysToExpiryChange: (value: string) => void
  onAcceptingOrdersChange: (value: string) => void
  onMinSignalScoreChange: (value: string) => void
  onLoadMore: () => void
  onSelect: (marketId: string) => void
}) {
  const intl = useIntl()
  const sortLabel = resolveMarketSortLabel(sort, intl)
  const featuredMarkets = markets.slice(0, 3)
  const directoryMarkets = markets.length > featuredMarkets.length
    ? markets.slice(featuredMarkets.length)
    : markets

  return (
    <section className="surface market-discovery" aria-label={intl.formatMessage({ id: 'nav.markets' })}>
      <div className="discovery-hero">
        <div className="discovery-title">
          <p className="eyebrow">{intl.formatMessage({ id: 'market.source' })}: {source}</p>
          <h2>{intl.formatMessage({ id: 'view.markets' })}</h2>
          <span>
            {intl.formatMessage(
              { id: isComplete ? 'discovery.showing' : 'discovery.showingMore' },
              { visible: markets.length, total: totalCount }
            )}
          </span>
        </div>
        <div className="discovery-controls">
          <label className="search-box discovery-search">
            <Search size={16} aria-hidden="true" />
            <input
              value={search}
              placeholder={intl.formatMessage({ id: 'filter.search' })}
              onChange={(event) => onSearchChange(event.target.value)}
            />
          </label>
          <select className="discovery-select" value={sort} onChange={(event) => onSortChange(event.target.value)}>
            <option value="rank">{intl.formatMessage({ id: 'filter.sort.rank' })}</option>
            <option value="volume">{intl.formatMessage({ id: 'filter.sort.volume' })}</option>
            <option value="signal">{intl.formatMessage({ id: 'filter.sort.signal' })}</option>
            <option value="liquidity">{intl.formatMessage({ id: 'filter.sort.liquidity' })}</option>
            <option value="expiry">{intl.formatMessage({ id: 'filter.sort.expiry' })}</option>
          </select>
          <div className="discovery-filter-grid">
            <select
              className="discovery-select"
              value={marketStatus}
              aria-label={intl.formatMessage({ id: 'filter.status' })}
              onChange={(event) => onMarketStatusChange(event.target.value)}
            >
              <option value="all">{intl.formatMessage({ id: 'filter.status.all' })}</option>
              <option value="Active">{intl.formatMessage({ id: 'filter.status.active' })}</option>
              <option value="Paused">{intl.formatMessage({ id: 'filter.status.paused' })}</option>
              <option value="Closed">{intl.formatMessage({ id: 'filter.status.closed' })}</option>
            </select>
            <select
              className="discovery-select"
              value={acceptingOrders}
              aria-label={intl.formatMessage({ id: 'filter.acceptingOrders' })}
              onChange={(event) => onAcceptingOrdersChange(event.target.value)}
            >
              <option value="all">{intl.formatMessage({ id: 'filter.accepting.all' })}</option>
              <option value="true">{intl.formatMessage({ id: 'filter.accepting.yes' })}</option>
              <option value="false">{intl.formatMessage({ id: 'filter.accepting.no' })}</option>
            </select>
            <NumericDiscoveryFilter
              label={intl.formatMessage({ id: 'filter.minLiquidity' })}
              value={minLiquidity}
              placeholder="1000"
              onChange={onMinLiquidityChange}
            />
            <NumericDiscoveryFilter
              label={intl.formatMessage({ id: 'filter.minVolume24h' })}
              value={minVolume24h}
              placeholder="500"
              onChange={onMinVolume24hChange}
            />
            <NumericDiscoveryFilter
              label={intl.formatMessage({ id: 'filter.maxDaysToExpiry' })}
              value={maxDaysToExpiry}
              placeholder="30"
              onChange={onMaxDaysToExpiryChange}
            />
            <NumericDiscoveryFilter
              label={intl.formatMessage({ id: 'filter.minSignalScore' })}
              value={minSignalScore}
              placeholder="0.30"
              step="0.01"
              onChange={onMinSignalScoreChange}
            />
          </div>
        </div>
      </div>

      <div className="topic-rail" aria-label={intl.formatMessage({ id: 'discovery.topics' })}>
        <button className={`topic-chip ${category === 'all' ? 'active' : ''}`} type="button" onClick={() => onCategoryChange('all')}>
          {intl.formatMessage({ id: 'filter.allCategories' })}
        </button>
        {categories.map((categoryName) => (
          <button
            className={`topic-chip ${categoryName === category ? 'active' : ''}`}
            key={categoryName}
            type="button"
            onClick={() => onCategoryChange(categoryName)}
          >
            {categoryName}
          </button>
        ))}
      </div>

      {markets.length === 0 ? (
        <div className="empty-panel">{intl.formatMessage({ id: 'empty.markets' })}</div>
      ) : (
        <>
          <div className="discovery-section">
            <div className="discovery-section-head">
              <h3>{intl.formatMessage({ id: 'discovery.featured' })}</h3>
              <span>{intl.formatMessage({ id: 'discovery.rankedBy' }, { sort: sortLabel })}</span>
            </div>
            <div className="featured-market-grid">
              {featuredMarkets.map((market) => (
                <MarketCard
                  featured
                  key={market.marketId}
                  market={market}
                  selected={market.marketId === selectedMarketId}
                  onSelect={onSelect}
                />
              ))}
            </div>
          </div>

          <div className="discovery-section">
            <div className="discovery-section-head">
              <h3>{intl.formatMessage({ id: 'discovery.allMarkets' })}</h3>
              <span>{source}</span>
            </div>
            <div className="market-card-grid">
              {directoryMarkets.map((market) => (
                <MarketCard
                  key={market.marketId}
                  market={market}
                  selected={market.marketId === selectedMarketId}
                  onSelect={onSelect}
                />
              ))}
            </div>
          </div>
        </>
      )}

      <div className="discovery-sentinel" ref={loadMoreRef}>
        {markets.length > 0 && hasMore && (
          <button className="ghost-button" type="button" disabled={isLoadingMore} onClick={onLoadMore}>
            <RefreshCw size={15} aria-hidden="true" />
            {intl.formatMessage({ id: isLoadingMore ? 'discovery.loadingMore' : 'discovery.loadMore' })}
          </button>
        )}
        {markets.length > 0 && !hasMore && (
          <span>{intl.formatMessage({ id: 'discovery.end' })}</span>
        )}
      </div>
    </section>
  )
})

const NumericDiscoveryFilter = memo(function NumericDiscoveryFilter({
  label,
  value,
  placeholder,
  step = '1',
  onChange
}: {
  label: string
  value: string
  placeholder: string
  step?: string
  onChange: (value: string) => void
}) {
  return (
    <label className="numeric-filter">
      <span>{label}</span>
      <input
        type="number"
        min="0"
        step={step}
        value={value}
        placeholder={placeholder}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  )
})

const MarketCard = memo(function MarketCard({
  market,
  featured = false,
  selected,
  onSelect
}: {
  market: ControlRoomMarket
  featured?: boolean
  selected: boolean
  onSelect: (marketId: string) => void
}) {
  const intl = useIntl()
  const primaryToken = market.tokens[0]
  const secondaryToken = market.tokens[1]
  const visibleTokens = [primaryToken, secondaryToken].filter((token): token is ControlRoomMarketToken => Boolean(token))
  const unsuitableReasons = resolveMarketUnsuitableReasons(market)
    .map((reason) => translateMarketDiscoveryCopy(reason, intl))
  const rankReason = translateMarketDiscoveryCopy(resolveMarketRankReason(market), intl)
  const rankScore = resolveMarketRankScore(market)
  const statusTone = resolveMarketStatusTone(market)

  return (
    <button
      className={`market-card ${featured ? 'featured' : ''} ${selected ? 'selected' : ''}`}
      type="button"
      onClick={() => onSelect(market.marketId)}
    >
      <div className="market-card-topline">
        <span className="category-chip">{market.category}</span>
        <div className="market-state-row">
          <span className={`status-chip ${statusTone}`}>
            {translateMarketStatus(market.status, (descriptor) => intl.formatMessage(descriptor))}
          </span>
          <span className={`status-chip ${market.acceptingOrders ? 'open' : 'paused'}`}>
            {market.acceptingOrders
              ? intl.formatMessage({ id: 'market.accepting' })
              : intl.formatMessage({ id: 'market.notAccepting' })}
          </span>
        </div>
      </div>
      <strong className="market-card-title">{market.name}</strong>
      <p>{market.description ?? market.slug ?? market.conditionId}</p>

      <div className="market-rank-band">
        <span>{intl.formatMessage({ id: 'market.rankScore' })} {formatPercent(rankScore)}</span>
        <small>{rankReason}</small>
      </div>

      <div className="outcome-grid">
        {visibleTokens.map((token, index) => (
          <span className="outcome-quote" key={token.tokenId}>
            <small>{token.outcome}</small>
            <strong>{formatProbability(token.price ?? (index === 0 ? market.yesPrice : market.noPrice))}</strong>
          </span>
        ))}
        {market.tokens.length > 2 && (
          <span className="outcome-quote muted">
            <small>{intl.formatMessage({ id: 'market.moreOutcomes' })}</small>
            <strong>{market.tokens.length}</strong>
          </span>
        )}
      </div>

      <div className="market-card-metrics">
        <MetricPair label={intl.formatMessage({ id: 'market.volume' })} value={formatCompact(market.volume24h)} />
        <MetricPair label={intl.formatMessage({ id: 'market.liquidity' })} value={formatCompact(market.liquidity)} />
        <MetricPair label={intl.formatMessage({ id: 'market.signal' })} value={formatPercent(market.signalScore)} />
        <MetricPair label={intl.formatMessage({ id: 'market.expires' })} value={market.expiresAtUtc ? formatDate(market.expiresAtUtc) : '-'} />
      </div>

      <div className={`market-reason-stack ${unsuitableReasons.length > 0 ? 'watch' : 'good'}`}>
        <span>
          {intl.formatMessage({
            id: unsuitableReasons.length > 0 ? 'market.unsuitable' : 'market.suitable'
          })}
        </span>
        <div>
          {(unsuitableReasons.length > 0 ? unsuitableReasons.slice(0, 3) : [rankReason]).map((reason) => (
            <small key={reason}>{reason}</small>
          ))}
        </div>
      </div>

      <div className="market-card-footer">
        <span>{market.slug ?? market.conditionId}</span>
        <ChevronRight size={17} aria-hidden="true" />
      </div>
    </button>
  )
})

const MarketDetailPanel = memo(function MarketDetailPanel({
  market,
  detail,
  selectedToken,
  isLoading,
  isOrderBookRefreshing,
  orderBookError,
  busyCommand,
  onSelectToken,
  onRefreshOrderBook
}: {
  market: ControlRoomMarket | null
  detail: ControlRoomMarketDetailResponse | null
  selectedToken: ControlRoomMarketToken | null
  isLoading: boolean
  isOrderBookRefreshing: boolean
  orderBookError: string | null
  busyCommand: string | null
  onSelectToken: (token: ControlRoomMarketToken) => Promise<void>
  onRefreshOrderBook: () => void
}) {
  const intl = useIntl()

  if (!market) {
    return (
      <section className="surface market-detail empty-detail">
        <div className="empty-panel">{intl.formatMessage({ id: 'empty.detail' })}</div>
      </section>
    )
  }

  const primaryToken = market.tokens[0]
  const secondaryToken = market.tokens[1]
  const displayedOrderBook = isOrderBookForSelectedToken(detail?.orderBook, selectedToken) ? detail?.orderBook ?? null : null
  const selectedBookBusy = Boolean(selectedToken && busyCommand === `book:${selectedToken.tokenId}`)
  const orderBookFreshness = displayedOrderBook ? resolveOrderBookFreshness(displayedOrderBook, intl) : null
  const bookState = orderBookError
    ? intl.formatMessage({ id: 'book.state.error' })
    : selectedBookBusy || isOrderBookRefreshing
      ? intl.formatMessage({ id: 'action.syncing' })
      : orderBookFreshness?.label ?? intl.formatMessage({ id: detail ? 'book.state.empty' : 'book.state.loading' })
  const relatedOrders = detail?.orders.length ?? 0
  const relatedPositions = detail?.positions.length ?? 0
  const relatedDecisions = detail?.decisions.length ?? 0
  const microstructureMetrics = buildOrderBookMicrostructureMetrics(displayedOrderBook, market, detail?.microstructure ?? [])

  return (
    <section className={`surface market-detail ${isLoading ? 'loading' : ''}`} aria-label={intl.formatMessage({ id: 'nav.detail' })}>
      <div className="market-hero">
        <div>
          <p className="eyebrow">{market.category} / {market.source}</p>
          <h2>{market.name}</h2>
          <p>{market.description ?? market.slug ?? market.conditionId}</p>
        </div>
        <div className="price-stack">
          <MetricPill
            label={primaryToken?.outcome ?? intl.formatMessage({ id: 'market.yes' })}
            value={formatProbability(primaryToken?.price ?? market.yesPrice)}
            tone="good"
          />
          <MetricPill
            label={secondaryToken?.outcome ?? intl.formatMessage({ id: 'market.no' })}
            value={formatProbability(secondaryToken?.price ?? market.noPrice)}
            tone="watch"
          />
        </div>
      </div>

      <div className="fact-grid">
        <Fact icon={<DatabaseZap size={17} />} label={intl.formatMessage({ id: 'market.liquidity' })} value={formatCurrency(market.liquidity)} />
        <Fact icon={<TrendingUp size={17} />} label={intl.formatMessage({ id: 'market.volume' })} value={formatCurrency(market.volume24h)} />
        <Fact icon={<Gauge size={17} />} label={intl.formatMessage({ id: 'market.signal' })} value={formatPercent(market.signalScore)} />
        <Fact icon={<Activity size={17} />} label={intl.formatMessage({ id: 'market.expires' })} value={market.expiresAtUtc ? formatDate(market.expiresAtUtc) : '-'} />
      </div>

      <div className="token-strip" aria-label={intl.formatMessage({ id: 'detail.tokens' })}>
        {market.tokens.map((token) => (
          <button
            className={selectedToken?.tokenId === token.tokenId ? 'selected' : ''}
            key={token.tokenId}
            type="button"
            disabled={busyCommand === `book:${token.tokenId}`}
            onClick={() => void onSelectToken(token)}
          >
            <span>{token.outcome}</span>
            <strong>{formatProbability(token.price)}</strong>
          </button>
        ))}
      </div>

      <div className="trade-activity-strip">
        <MetricPair label={intl.formatMessage({ id: 'book.activity' })} value={bookState} />
        <MetricPair label={intl.formatMessage({ id: 'nav.orders' })} value={relatedOrders} />
        <MetricPair label={intl.formatMessage({ id: 'table.position' })} value={relatedPositions} />
        <MetricPair label={intl.formatMessage({ id: 'nav.decisions' })} value={relatedDecisions} />
      </div>

      <div className="detail-grid">
        <OrderBookPanel
          orderBook={displayedOrderBook}
          error={orderBookError}
          isRefreshing={isOrderBookRefreshing || selectedBookBusy}
          onRefresh={onRefreshOrderBook}
        />
        <Microstructure metrics={microstructureMetrics} />
      </div>
    </section>
  )
})

const OrderBookPanel = memo(function OrderBookPanel({
  orderBook,
  error,
  isRefreshing,
  onRefresh
}: {
  orderBook: ControlRoomOrderBook | null
  error: string | null
  isRefreshing: boolean
  onRefresh: () => void
}) {
  const intl = useIntl()
  const freshness = resolveOrderBookFreshness(orderBook, intl)

  return (
    <section className={`book-panel ${freshness.tone}`}>
      <div className="section-head mini">
        <div>
          <p className="eyebrow">{orderBook?.source ?? '-'}</p>
          <h3>{intl.formatMessage({ id: 'book.title' })}</h3>
        </div>
        <div className="book-head-actions">
          <span className={`freshness-badge ${freshness.tone}`}>{freshness.label}</span>
          <button className="icon-button compact" type="button" disabled={isRefreshing} onClick={onRefresh}>
            <RefreshCw size={15} aria-hidden="true" />
            <span>{intl.formatMessage({ id: isRefreshing ? 'action.syncing' : 'action.refresh' })}</span>
          </button>
          <Layers size={18} aria-hidden="true" />
        </div>
      </div>
      {error ? (
        <div className="book-state-panel error">
          <strong>{intl.formatMessage({ id: 'book.requestFailed' })}</strong>
          <span>{sanitizeCommandMessage(error)}</span>
        </div>
      ) : orderBook ? (
        <>
          <div className={`book-freshness-strip ${freshness.tone}`}>
            <strong>{freshness.label}</strong>
            <span>{freshness.message}</span>
            <small>
              {intl.formatMessage(
                { id: 'book.lastUpdate' },
                {
                  staleSeconds: orderBook.freshness.staleSeconds,
                  updatedAt: formatTime(orderBook.lastUpdatedUtc)
                }
              )}
            </small>
          </div>
          <div className="book-summary">
            <MetricPill label={intl.formatMessage({ id: 'book.spread' })} value={orderBook.spread === null ? '-' : orderBook.spread.toFixed(3)} />
            <MetricPill label={intl.formatMessage({ id: 'book.midpoint' })} value={orderBook.midpoint === null ? '-' : orderBook.midpoint.toFixed(3)} />
            <MetricPill label={intl.formatMessage({ id: 'book.imbalance' })} value={`${orderBook.imbalancePct.toFixed(1)}%`} />
          </div>
          <div className="book-ladder">
            <DepthSide title={intl.formatMessage({ id: 'book.bid' })} levels={orderBook.bids} side="bid" />
            <DepthSide title={intl.formatMessage({ id: 'book.ask' })} levels={orderBook.asks} side="ask" />
          </div>
        </>
      ) : (
        <div className="book-state-panel empty">
          <strong>{intl.formatMessage({ id: 'empty.orderBook' })}</strong>
          <span>{intl.formatMessage({ id: 'book.emptyTokenDepth' })}</span>
        </div>
      )}
    </section>
  )
})

function DepthSide({
  title,
  levels,
  side
}: {
  title: string
  levels: ControlRoomOrderBook['bids']
  side: 'bid' | 'ask'
}) {
  const intl = useIntl()

  return (
    <div className={`depth-side ${side}`}>
      <div className="depth-title">
        <strong>{title}</strong>
        <span>{intl.formatMessage({ id: 'book.size' })}</span>
      </div>
      {levels.length === 0 ? (
        <div className="empty-panel">{intl.formatMessage({ id: 'empty.depth' })}</div>
      ) : (
        levels.map((level) => (
          <div className="depth-row" key={`${side}:${level.level}:${level.price}`}>
            <div className="depth-fill" style={{ width: `${Math.min(100, Math.max(2, level.depthPct))}%` }} />
            <span>{level.price.toFixed(3)}</span>
            <strong>{level.size.toFixed(2)}</strong>
            <small>{formatCurrency(level.notional)}</small>
          </div>
        ))
      )}
    </div>
  )
}

const Microstructure = memo(function Microstructure({ metrics }: { metrics: ControlRoomMetric[] }) {
  const intl = useIntl()

  return (
    <section className="micro-panel">
      <div className="section-head mini">
        <div>
          <p className="eyebrow">{intl.formatMessage({ id: 'detail.microstructure' })}</p>
          <h3>{intl.formatMessage({ id: 'nav.detail' })}</h3>
        </div>
        <Activity size={18} aria-hidden="true" />
      </div>
      <div className="micro-list">
        {metrics.length === 0 ? (
          <div className="empty-panel">{intl.formatMessage({ id: 'empty.microstructure' })}</div>
        ) : (
          metrics.map((metric) => (
            <div className={`micro-row ${metric.tone}`} key={metric.label}>
              <span>{translateMetricLabel(metric.label, (descriptor) => intl.formatMessage(descriptor))}</span>
              <strong>{translateOperationalCopy(metric.value, intl)}</strong>
              <small>{translateOperationalCopy(metric.delta, intl)}</small>
            </div>
          ))
        )}
      </div>
    </section>
  )
})

const RiskStack = memo(function RiskStack({
  risk,
  busyCommand,
  commandSafety,
  onKillSwitch
}: {
  risk: ControlRoomRisk | null
  busyCommand: string | null
  commandSafety: CommandSafety
  onKillSwitch: (active: boolean) => Promise<void>
}) {
  const intl = useIntl()
  if (!risk) {
    return <section className="surface side-panel"><div className="empty-panel">-</div></section>
  }

  const tiles = [
    [intl.formatMessage({ id: 'risk.capital' }), formatCurrency(risk.totalCapital)],
    [intl.formatMessage({ id: 'risk.available' }), formatCurrency(risk.availableCapital)],
    [intl.formatMessage({ id: 'risk.openNotional' }), formatCurrency(risk.openNotional)],
    [intl.formatMessage({ id: 'risk.unhedged' }), String(risk.unhedgedExposures)]
  ]
  const hardStopDisabledReason = resolveKillSwitchDisabledReason(true, risk, busyCommand, commandSafety)
  const resetDisabledReason = resolveKillSwitchDisabledReason(false, risk, busyCommand, commandSafety)

  return (
    <section className="surface side-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">{intl.formatMessage({ id: 'risk.killSwitch' })}</p>
          <h2>{intl.formatMessage({ id: 'nav.risk' })}</h2>
        </div>
        <ShieldCheck size={20} aria-hidden="true" />
      </div>
      <div className={`risk-state ${risk.killSwitchActive ? 'armed' : ''}`}>
        <strong>{risk.killSwitchActive ? risk.killSwitchLevel : intl.formatMessage({ id: 'risk.inactive' })}</strong>
        <span>{risk.killSwitchReason ?? `${risk.capitalUtilizationPct.toFixed(1)}%`}</span>
        <small className={`mode-chip ${commandSafety.level}`}>{commandSafety.label}</small>
        <div className="button-row">
          <button
            className="danger-button"
            type="button"
            title={hardStopDisabledReason ?? intl.formatMessage({ id: 'action.hardStop' })}
            disabled={Boolean(hardStopDisabledReason)}
            onClick={() => void onKillSwitch(true)}
          >
            <OctagonX size={15} aria-hidden="true" />
            {intl.formatMessage({ id: 'action.hardStop' })}
          </button>
          <button
            className="ghost-button"
            type="button"
            title={resetDisabledReason ?? intl.formatMessage({ id: 'action.reset' })}
            disabled={Boolean(resetDisabledReason)}
            onClick={() => void onKillSwitch(false)}
          >
            <ShieldCheck size={15} aria-hidden="true" />
            {intl.formatMessage({ id: 'action.reset' })}
          </button>
          {(hardStopDisabledReason || resetDisabledReason) && (
            <span className="control-reason">{hardStopDisabledReason ?? resetDisabledReason}</span>
          )}
        </div>
      </div>
      <div className="risk-summary">
        {tiles.map(([label, value]) => (
          <div className="risk-tile" key={label}>
            <span>{label}</span>
            <strong>{value}</strong>
          </div>
        ))}
      </div>
      <div className="limit-list">
        {risk.limits.map((limit) => {
          const width = Math.min(100, Math.max(0, (limit.current / Math.max(1, limit.limit)) * 100))
          return (
            <div className="limit-row" key={limit.name}>
              <div className="limit-label">
                <span>{limit.name}</span>
                <span>
                  {formatLimit(limit.current, limit.unit)} / {formatLimit(limit.limit, limit.unit)}
                </span>
              </div>
              <div className="limit-track">
                <div className={`limit-fill ${limit.state}`} style={{ width: `${width}%` }} />
              </div>
            </div>
          )
        })}
      </div>
    </section>
  )
})

const IncidentResponsePanel = memo(function IncidentResponsePanel({
  catalog,
  snapshot,
  selectedStrategyId,
  cancelScope,
  busyCommand,
  isLoading,
  error,
  riskEventId,
  onCancelScopeChange,
  onKillSwitch,
  onSetState,
  onCancelOpenOrders,
  onRefresh
}: {
  catalog: IncidentActionCatalog | null
  snapshot: ControlRoomSnapshot | null
  selectedStrategyId: string | null
  cancelScope: IncidentCancelScope
  busyCommand: string | null
  commandSafety: CommandSafety
  isLoading: boolean
  error: string | null
  riskEventId: string
  onCancelScopeChange: (scope: IncidentCancelScope) => void
  onKillSwitch: (active: boolean) => Promise<void>
  onSetState: (strategyId: string, targetState: TargetStrategyState) => Promise<void>
  onCancelOpenOrders: () => Promise<void>
  onRefresh: () => void
}) {
  const incidentPackageHref = buildApiHref(buildIncidentPackagePath({
    riskEventId: riskEventId.trim() || undefined,
    strategyId: selectedStrategyId ?? undefined
  }))
  const actions = catalog?.actions ?? []
  const openOrders = snapshot?.risk.openOrders ?? snapshot?.orders.length ?? 0
  const generatedAt = catalog ? formatDate(catalog.generatedAtUtc) : '-'

  return (
    <section className="surface incident-actions" aria-label="Incident response actions">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Incident response</p>
          <h2>Actions</h2>
        </div>
        <ClipboardCheck size={20} aria-hidden="true" />
      </div>

      <div className="incident-actions-summary">
        <MetricPair label="Catalog" value={generatedAt} />
        <MetricPair label="Command mode" value={catalog?.commandMode ?? snapshot?.commandMode ?? '-'} />
        <MetricPair label="Open orders" value={openOrders} />
        <MetricPair label="Selected strategy" value={formatNullableText(selectedStrategyId)} />
        <MetricPair label="Kill switch" value={snapshot?.risk.killSwitchActive ? snapshot.risk.killSwitchLevel : 'Inactive'} />
        <MetricPair label="Runbook" value={catalog?.runbookPath ?? 'docs/operations/autotrade-incident-runbook.md'} />
      </div>

      <div className="incident-toolbar">
        <div className="incident-scope-control" aria-label="Cancel order scope">
          <button
            className={cancelScope === 'all' ? 'active' : ''}
            type="button"
            onClick={() => onCancelScopeChange('all')}
          >
            All orders
          </button>
          <button
            className={cancelScope === 'strategy' ? 'active' : ''}
            type="button"
            onClick={() => onCancelScopeChange('strategy')}
          >
            Selected strategy
          </button>
        </div>
        <button className="ghost-button" type="button" disabled={isLoading} onClick={onRefresh}>
          <RefreshCw size={16} aria-hidden="true" />
          {isLoading ? 'Loading' : 'Refresh'}
        </button>
        <a className="export-link" href={incidentPackageHref} target="_blank" rel="noreferrer">
          <FileText size={15} aria-hidden="true" />
          Incident package
        </a>
      </div>

      {error && <div className="error-strip incident-actions-error">{error}</div>}

      <div className="incident-action-list">
        {actions.length === 0 ? (
          <div className="empty-panel tight">No incident action catalog loaded.</div>
        ) : (
          actions.map((action) => {
            const disabledReason = resolveIncidentActionDisabledReason(
              action,
              selectedStrategyId,
              cancelScope,
              busyCommand)
            const pending = isIncidentActionPending(action, selectedStrategyId, busyCommand)
            const handler = createIncidentActionHandler(
              action,
              selectedStrategyId,
              onKillSwitch,
              onSetState,
              onCancelOpenOrders)

            return (
              <IncidentActionRow
                key={action.id}
                action={action}
                disabledReason={disabledReason}
                pending={pending}
                packageHref={action.id === 'export-incident-package' ? incidentPackageHref : null}
                onRun={handler}
              />
            )
          })
        )}
      </div>
    </section>
  )
})

function IncidentActionRow({
  action,
  disabledReason,
  pending,
  packageHref,
  onRun
}: {
  action: IncidentActionDescriptor
  disabledReason: string | null
  pending: boolean
  packageHref: string | null
  onRun: (() => Promise<void>) | null
}) {
  const statusLabel = disabledReason ? 'Blocked' : pending ? 'Pending' : 'Ready'

  return (
    <article className={`incident-action-row ${disabledReason ? 'blocked' : 'ready'}`}>
      <div className="incident-action-icon">{getIncidentActionIcon(action.id)}</div>
      <div className="incident-action-main">
        <div className="incident-action-title">
          <h3>{action.label}</h3>
          <span>{action.category} / {action.scope}</span>
        </div>
        <p>{action.result}</p>
        <div className="incident-action-meta">
          <span>{action.method}</span>
          <span>{action.path}</span>
          {action.confirmationText && <span>Requires {action.confirmationText}</span>}
        </div>
        {disabledReason && <small className="control-reason">{disabledReason}</small>}
      </div>
      <div className="incident-action-control">
        <span className={`incident-action-status ${disabledReason ? 'blocked' : 'ready'}`}>{statusLabel}</span>
        {packageHref ? (
          <a
            className="command-button"
            href={packageHref}
            target="_blank"
            rel="noreferrer"
            aria-disabled={Boolean(disabledReason)}
            onClick={(event) => {
              if (disabledReason) {
                event.preventDefault()
              }
            }}
          >
            <FileText size={15} aria-hidden="true" />
            Export
          </a>
        ) : (
          <button
            className={action.id === 'hard-stop' || action.id === 'cancel-open-orders' ? 'danger-button' : 'command-button'}
            type="button"
            title={disabledReason ?? action.label}
            disabled={Boolean(disabledReason)}
            onClick={() => {
              if (onRun) {
                void onRun()
              }
            }}
          >
            {pending ? <RefreshCw size={15} aria-hidden="true" /> : <ChevronRight size={15} aria-hidden="true" />}
            Run
          </button>
        )}
      </div>
    </article>
  )
}

const RiskDrilldownPanel = memo(function RiskDrilldownPanel({
  riskEventId,
  loadedRiskEventId,
  drilldown,
  exposures,
  isLoading,
  error,
  onRiskEventIdChange,
  onLoad,
  onClear
}: {
  riskEventId: string
  loadedRiskEventId: string
  drilldown: RiskEventDrilldown | null
  exposures: UnhedgedExposureDrilldownResponse | null
  isLoading: boolean
  error: string | null
  onRiskEventIdChange: (riskEventId: string) => void
  onLoad: () => void
  onClear: () => void
}) {
  const eventJsonHref = drilldown ? buildApiHref(drilldown.sourceReferences.jsonApi) : null
  const eventCsvHref = drilldown ? buildApiHref(drilldown.sourceReferences.csvApi) : null
  const exposureCsvHref = buildApiHref(buildUnhedgedExposureCsvApi(loadedRiskEventId))
  const exposureRows = exposures?.exposures ?? []

  return (
    <section className="surface risk-drilldown" aria-label="Risk drill-down">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Incident response</p>
          <h2>Risk drill-down</h2>
        </div>
        <Gauge size={20} aria-hidden="true" />
      </div>

      <div className="risk-drilldown-toolbar">
        <label className="search-box risk-event-search" htmlFor="risk-event-id">
          <Search size={16} aria-hidden="true" />
          <input
            id="risk-event-id"
            value={riskEventId}
            placeholder="Risk event id"
            onChange={(event) => onRiskEventIdChange(event.target.value)}
          />
        </label>
        <button className="command-button" type="button" disabled={isLoading} onClick={onLoad}>
          <RefreshCw size={16} aria-hidden="true" />
          {isLoading ? 'Loading' : 'Load'}
        </button>
        <button className="ghost-button" type="button" disabled={isLoading && loadedRiskEventId === ''} onClick={onClear}>
          Clear
        </button>
        <a className="export-link" href={exposureCsvHref} target="_blank" rel="noreferrer">
          <FileText size={15} aria-hidden="true" />
          Exposure CSV
        </a>
      </div>

      {error && <div className="error-strip risk-drilldown-error">{error}</div>}

      <div className="risk-drilldown-grid">
        <section className="risk-detail-block">
          <div className="risk-block-head">
            <div>
              <p className="eyebrow">Trigger</p>
              <h3>{drilldown ? drilldown.event.code : 'No event selected'}</h3>
            </div>
            <span className={`risk-severity ${drilldown?.event.severity.toLowerCase() ?? 'unknown'}`}>
              {drilldown?.event.severity ?? '-'}
            </span>
          </div>

          {drilldown ? (
            <>
              <div className="risk-drilldown-summary">
                <MetricPair label="Event" value={compactId(drilldown.event.id)} />
                <MetricPair label="Created" value={formatDate(drilldown.event.createdAtUtc)} />
                <MetricPair label="Strategy" value={formatNullableText(drilldown.event.strategyId)} />
                <MetricPair label="Market" value={formatNullableText(drilldown.event.marketId)} />
                <MetricPair label="Limit" value={formatNullableText(drilldown.trigger.limitName)} />
                <MetricPair label="Current" value={formatRiskValue(drilldown.trigger.currentValue, drilldown.trigger.unit)} />
                <MetricPair label="Threshold" value={formatRiskValue(drilldown.trigger.threshold, drilldown.trigger.unit)} />
                <MetricPair label="State" value={drilldown.trigger.state} />
                <MetricPair label="Action" value={drilldown.action.selectedAction} />
                <MetricPair label="Result" value={formatNullableText(drilldown.action.mitigationResult)} />
              </div>

              <div className="risk-message">
                <span>Trigger reason</span>
                <p>{drilldown.trigger.triggerReason}</p>
                <small>{drilldown.event.message}</small>
              </div>

              <div className="risk-link-strip">
                {eventJsonHref && (
                  <a href={eventJsonHref} target="_blank" rel="noreferrer">
                    <FileText size={15} aria-hidden="true" />
                    JSON
                  </a>
                )}
                {eventCsvHref && (
                  <a href={eventCsvHref} target="_blank" rel="noreferrer">
                    <FileText size={15} aria-hidden="true" />
                    CSV
                  </a>
                )}
                <span>{drilldown.sourceReferences.riskEventIds.length} risk event</span>
                <span>{drilldown.sourceReferences.orderEventIds.length} order events</span>
              </div>
            </>
          ) : (
            <div className="empty-panel tight">No risk event loaded.</div>
          )}
        </section>

        <section className="risk-detail-block">
          <div className="risk-block-head">
            <div>
              <p className="eyebrow">Mitigation</p>
              <h3>Orders and kill switch</h3>
            </div>
            <ShieldCheck size={18} aria-hidden="true" />
          </div>

          {drilldown ? (
            <>
              <div className={`kill-link ${drilldown.killSwitch ? 'active' : ''}`}>
                <MetricPair label="Scope" value={drilldown.killSwitch?.scope ?? '-'} />
                <MetricPair label="Level" value={drilldown.killSwitch?.level ?? '-'} />
                <MetricPair label="Reason" value={drilldown.killSwitch?.reasonCode ?? '-'} />
                <MetricPair label="Triggered by" value={compactNullableId(drilldown.killSwitch?.triggeringRiskEventId)} />
              </div>

              <div className="table-wrap risk-order-wrap">
                <table className="risk-order-table">
                  <thead>
                    <tr>
                      <th>Order</th>
                      <th>Client</th>
                      <th>Status</th>
                      <th>Market</th>
                      <th>Source</th>
                    </tr>
                  </thead>
                  <tbody>
                    {drilldown.affectedOrders.length === 0 ? (
                      <tr>
                        <td colSpan={5}>No affected orders.</td>
                      </tr>
                    ) : (
                      drilldown.affectedOrders.map((order) => (
                        <tr key={`${order.source}:${order.detailReference}:${order.clientOrderId ?? order.orderId ?? '-'}`}>
                          <td>{compactNullableId(order.orderId)}</td>
                          <td>{formatNullableText(order.clientOrderId)}</td>
                          <td>{order.status ?? '-'}</td>
                          <td>{compactNullableId(order.marketId)}</td>
                          <td>{order.source}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </>
          ) : (
            <div className="empty-panel tight">No mitigation event loaded.</div>
          )}
        </section>
      </div>

      <section className="risk-detail-block exposure-block">
        <div className="risk-block-head">
          <div>
            <p className="eyebrow">Exposure detail</p>
            <h3>{exposures ? `${exposures.count} unhedged records` : 'Unhedged exposure'}</h3>
          </div>
          <span>{exposures ? formatDate(exposures.generatedAtUtc) : '-'}</span>
        </div>

        {drilldown?.exposure && (
          <div className="risk-drilldown-summary featured-exposure">
            <MetricPair label="Evidence" value={compactNullableId(drilldown.exposure.evidenceId)} />
            <MetricPair label="Market" value={compactId(drilldown.exposure.marketId)} />
            <MetricPair label="Strategy" value={drilldown.exposure.strategyId} />
            <MetricPair label="Outcome" value={`${drilldown.exposure.outcome} / ${drilldown.exposure.side}`} />
            <MetricPair label="Notional" value={formatCurrency(drilldown.exposure.notional)} />
            <MetricPair label="Duration" value={formatDurationSeconds(drilldown.exposure.durationSeconds)} />
            <MetricPair label="Hedge state" value={drilldown.exposure.hedgeState} />
            <MetricPair label="Mitigation" value={drilldown.exposure.mitigationResult} />
          </div>
        )}

        <div className="table-wrap">
          <table className="exposure-table">
            <thead>
              <tr>
                <th>Evidence</th>
                <th>Market</th>
                <th>Outcome</th>
                <th>Strategy</th>
                <th>Notional</th>
                <th>Duration</th>
                <th>Hedge state</th>
                <th>Mitigation</th>
              </tr>
            </thead>
            <tbody>
              {exposureRows.length === 0 ? (
                <tr>
                  <td colSpan={8}>No unhedged exposure records.</td>
                </tr>
              ) : (
                exposureRows.map((exposure) => (
                  <tr key={`${exposure.source}:${exposure.evidenceId ?? exposure.marketId}:${exposure.startedAtUtc}`}>
                    <td>{compactNullableId(exposure.evidenceId)}</td>
                    <td>{compactId(exposure.marketId)}</td>
                    <td>{exposure.outcome} / {exposure.side}</td>
                    <td>{exposure.strategyId}</td>
                    <td>{formatCurrency(exposure.notional)}</td>
                    <td>{formatDurationSeconds(exposure.durationSeconds)}</td>
                    <td>{exposure.hedgeState}</td>
                    <td>{exposure.mitigationResult}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </section>
    </section>
  )
})

const StrategyWorkspace = memo(function StrategyWorkspace({
  strategies,
  selectedStrategy,
  selectedStrategyId,
  decisions,
  snapshotDecisions,
  parameterSnapshot,
  markets,
  orders,
  busyCommand,
  commandSafety,
  isDecisionLoading,
  decisionError,
  isParameterLoading,
  parameterError,
  onSelect,
  onSetState,
  onUpdateParameters,
  onRollbackParameters
}: {
  strategies: ControlRoomStrategy[]
  selectedStrategy: ControlRoomStrategy | null
  selectedStrategyId: string | null
  decisions: StrategyDecisionSummary[]
  snapshotDecisions: ControlRoomDecision[]
  parameterSnapshot: StrategyParameterSnapshot | null
  markets: ControlRoomMarket[]
  orders: ControlRoomOrder[]
  busyCommand: string | null
  commandSafety: CommandSafety
  isDecisionLoading: boolean
  decisionError: string | null
  isParameterLoading: boolean
  parameterError: string | null
  onSelect: (strategyId: string) => void
  onSetState: (strategyId: string, targetState: TargetStrategyState) => Promise<void>
  onUpdateParameters: (strategyId: string, changes: Record<string, string>) => Promise<void>
  onRollbackParameters: (strategyId: string, versionId: string) => Promise<void>
}) {
  return (
    <div className="strategy-workspace">
      <StrategyList
        strategies={strategies}
        selectedStrategyId={selectedStrategyId}
        busyCommand={busyCommand}
        commandSafety={commandSafety}
        onSelect={onSelect}
        onSetState={onSetState}
      />
      <StrategyDetailPanel
        strategy={selectedStrategy}
        decisions={decisions}
        snapshotDecisions={snapshotDecisions}
        parameterSnapshot={parameterSnapshot}
        markets={markets}
        orders={orders}
        busyCommand={busyCommand}
        commandSafety={commandSafety}
        isDecisionLoading={isDecisionLoading}
        decisionError={decisionError}
        isParameterLoading={isParameterLoading}
        parameterError={parameterError}
        onSetState={onSetState}
        onUpdateParameters={onUpdateParameters}
        onRollbackParameters={onRollbackParameters}
      />
    </div>
  )
})

const StrategyList = memo(function StrategyList({
  strategies,
  selectedStrategyId,
  busyCommand,
  commandSafety,
  onSelect,
  onSetState
}: {
  strategies: ControlRoomStrategy[]
  selectedStrategyId: string | null
  busyCommand: string | null
  commandSafety: CommandSafety
  onSelect: (strategyId: string) => void
  onSetState: (strategyId: string, targetState: TargetStrategyState) => Promise<void>
}) {
  const intl = useIntl()
  return (
    <section className="surface side-panel strategy-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Lifecycle</p>
          <h2>{intl.formatMessage({ id: 'nav.strategies' })}</h2>
        </div>
        <Zap size={20} aria-hidden="true" />
      </div>
      <div className="strategy-list">
        {strategies.length === 0 ? (
          <div className="empty-panel">No strategies are registered.</div>
        ) : (
          strategies.map((strategy) => (
            <StrategyCard
              key={strategy.strategyId}
              strategy={strategy}
              selected={strategy.strategyId === selectedStrategyId}
              busyCommand={busyCommand}
              commandSafety={commandSafety}
              onSelect={onSelect}
              onSetState={onSetState}
            />
          ))
        )}
      </div>
    </section>
  )
})

function StrategyCard({
  strategy,
  selected,
  busyCommand,
  commandSafety,
  onSelect,
  onSetState
}: {
  strategy: ControlRoomStrategy
  selected: boolean
  busyCommand: string | null
  commandSafety: CommandSafety
  onSelect: (strategyId: string) => void
  onSetState: (strategyId: string, targetState: TargetStrategyState) => Promise<void>
}) {
  const intl = useIntl()
  const strategyBusy = busyCommand?.startsWith(`${strategy.strategyId}:`) === true
  const isPending = (targetState: TargetStrategyState) => busyCommand === `${strategy.strategyId}:${targetState}`
  const disabledReason = (targetState: TargetStrategyState) =>
    resolveStrategyDisabledReason(strategy, targetState, strategyBusy, commandSafety)
  const cardDisabledReason = strategyBusy
    ? 'Command is pending'
    : describeBlockedReason(strategy) ?? (commandSafety.commandsEnabled ? null : commandSafety.reason)

  return (
    <article className={`strategy-card ${cardDisabledReason ? 'limited' : ''} ${selected ? 'selected' : ''}`}>
      <div className="strategy-topline">
        <div>
          <h3>{strategy.name}</h3>
          <small>{strategy.strategyId}</small>
        </div>
        <div className="strategy-card-controls">
          <span className={`state-badge ${strategy.state.toLowerCase()}`}>
            {intl.formatMessage({ id: `state.${strategy.state}` })}
          </span>
          <button
            className="strategy-detail-link"
            type="button"
            title="Inspect strategy"
            onClick={() => onSelect(strategy.strategyId)}
          >
            <ChevronRight size={16} aria-hidden="true" />
            <span>{selected ? 'Selected' : 'Inspect'}</span>
          </button>
        </div>
      </div>
      <div className="strategy-meta">
        <MetricPair label={intl.formatMessage({ id: 'strategy.markets' })} value={strategy.activeMarkets} />
        <MetricPair label={intl.formatMessage({ id: 'strategy.cycles' })} value={strategy.cycleCount} />
        <MetricPair label={intl.formatMessage({ id: 'strategy.backlog' })} value={strategy.channelBacklog} />
      </div>
      <div className="strategy-actions">
        <StrategyButton
          icon={<Play size={15} aria-hidden="true" />}
          label={intl.formatMessage({ id: 'action.run' })}
          primary
          pending={isPending('Running')}
          disabledReason={disabledReason('Running')}
          onClick={() => onSetState(strategy.strategyId, 'Running')}
        />
        <StrategyButton
          icon={<CirclePause size={15} aria-hidden="true" />}
          label={intl.formatMessage({ id: 'action.pause' })}
          pending={isPending('Paused')}
          disabledReason={disabledReason('Paused')}
          onClick={() => onSetState(strategy.strategyId, 'Paused')}
        />
        <StrategyButton
          icon={<Square size={15} aria-hidden="true" />}
          label={intl.formatMessage({ id: 'action.stop' })}
          pending={isPending('Stopped')}
          disabledReason={disabledReason('Stopped')}
          onClick={() => onSetState(strategy.strategyId, 'Stopped')}
        />
        {cardDisabledReason && <span className="control-reason">{cardDisabledReason}</span>}
      </div>
    </article>
  )
}

const StrategyDetailPanel = memo(function StrategyDetailPanel({
  strategy,
  decisions,
  snapshotDecisions,
  parameterSnapshot,
  markets,
  orders,
  busyCommand,
  commandSafety,
  isDecisionLoading,
  decisionError,
  isParameterLoading,
  parameterError,
  onSetState,
  onUpdateParameters,
  onRollbackParameters
}: {
  strategy: ControlRoomStrategy | null
  decisions: StrategyDecisionSummary[]
  snapshotDecisions: ControlRoomDecision[]
  parameterSnapshot: StrategyParameterSnapshot | null
  markets: ControlRoomMarket[]
  orders: ControlRoomOrder[]
  busyCommand: string | null
  commandSafety: CommandSafety
  isDecisionLoading: boolean
  decisionError: string | null
  isParameterLoading: boolean
  parameterError: string | null
  onSetState: (strategyId: string, targetState: TargetStrategyState) => Promise<void>
  onUpdateParameters: (strategyId: string, changes: Record<string, string>) => Promise<void>
  onRollbackParameters: (strategyId: string, versionId: string) => Promise<void>
}) {
  const intl = useIntl()
  const recentDecisions = useMemo(
    () => buildStrategyDecisionDisplays(strategy?.strategyId ?? null, decisions, snapshotDecisions),
    [decisions, snapshotDecisions, strategy?.strategyId]
  )
  const affectedMarkets = useMemo(
    () => buildAffectedMarkets(strategy?.strategyId ?? null, recentDecisions, markets, orders),
    [markets, orders, recentDecisions, strategy?.strategyId]
  )
  const activeParameters = useMemo(() => {
    if (parameterSnapshot && parameterSnapshot.strategyId === strategy?.strategyId) {
      return parameterSnapshot.parameters
    }

    return (strategy?.parameters ?? []).map((parameter) => ({
      name: parameter.name,
      value: parameter.value,
      type: 'display',
      editable: false
    }))
  }, [parameterSnapshot, strategy?.parameters, strategy?.strategyId])
  const editableParameters = useMemo(
    () => activeParameters.filter((parameter) => parameter.editable),
    [activeParameters]
  )
  const [selectedParameterName, setSelectedParameterName] = useState('')
  const [draftParameterValue, setDraftParameterValue] = useState('')

  useEffect(() => {
    const selectedStillExists = editableParameters.some((parameter) => parameter.name === selectedParameterName)
    const nextParameter = selectedStillExists
      ? editableParameters.find((parameter) => parameter.name === selectedParameterName)
      : editableParameters[0]

    setSelectedParameterName(nextParameter?.name ?? '')
    setDraftParameterValue(nextParameter?.value ?? '')
  }, [editableParameters, selectedParameterName])

  if (!strategy) {
    return (
      <section className="surface strategy-detail-panel">
        <div className="section-head compact">
          <div>
            <p className="eyebrow">Strategy detail</p>
            <h2>Inspect</h2>
          </div>
          <Zap size={20} aria-hidden="true" />
        </div>
        <div className="empty-panel">Select a strategy to inspect its state.</div>
      </section>
    )
  }

  const strategyBusy = busyCommand?.startsWith(`${strategy.strategyId}:`) === true
  const disabledReason = (targetState: TargetStrategyState) =>
    resolveStrategyDisabledReason(strategy, targetState, strategyBusy, commandSafety)
  const blockedReason = describeBlockedReason(strategy)
  const actionRows: Array<{ targetState: TargetStrategyState; label: string; icon: ReactNode; primary?: boolean }> = [
    {
      targetState: 'Running',
      label: intl.formatMessage({ id: 'action.run' }),
      icon: <Play size={15} aria-hidden="true" />,
      primary: true
    },
    {
      targetState: 'Paused',
      label: intl.formatMessage({ id: 'action.pause' }),
      icon: <CirclePause size={15} aria-hidden="true" />
    },
    {
      targetState: 'Stopped',
      label: intl.formatMessage({ id: 'action.stop' }),
      icon: <Square size={15} aria-hidden="true" />
    }
  ]

  return (
    <section className="surface strategy-detail-panel" aria-label="Strategy detail">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Strategy detail</p>
          <h2>{strategy.name}</h2>
        </div>
        <span className={`state-badge ${strategy.state.toLowerCase()}`}>
          {intl.formatMessage({ id: `state.${strategy.state}` })}
        </span>
      </div>

      <div className="strategy-detail-body">
        <div className="strategy-hypothesis">
          <span>Hypothesis</span>
          <p>{resolveStrategyHypothesis(strategy)}</p>
        </div>

        <div className="strategy-state-grid">
          <MetricPair label="Current" value={strategy.state} />
          <MetricPair label="Desired" value={strategy.desiredState || '-'} />
          <MetricPair label="Enabled" value={strategy.enabled ? 'Yes' : 'No'} />
          <MetricPair label="Config" value={strategy.configVersion || '-'} />
          <MetricPair label="Heartbeat" value={formatNullableDate(strategy.lastHeartbeatUtc)} />
          <MetricPair label="Decision" value={formatNullableDate(strategy.lastDecisionAtUtc)} />
          <MetricPair label="Snapshots" value={strategy.snapshotsProcessed} />
          <MetricPair label="Backlog" value={strategy.channelBacklog} />
        </div>

        <div className={`blocked-reason-panel ${blockedReason ? 'blocked' : 'clear'}`}>
          <span>Blocked reason</span>
          <strong>{blockedReason ?? 'Clear'}</strong>
          {strategy.lastError && <small>{strategy.lastError}</small>}
        </div>

        <div className="strategy-detail-section">
          <div className="strategy-section-title">
            <span>Actions</span>
            <small>{commandSafety.label}</small>
          </div>
          <div className="strategy-command-grid">
            {actionRows.map((action) => {
              const reason = disabledReason(action.targetState)
              const pending = busyCommand === `${strategy.strategyId}:${action.targetState}`
              return (
                <div className={`strategy-command-row ${reason ? 'disabled' : 'enabled'}`} key={action.targetState}>
                  <StrategyButton
                    icon={action.icon}
                    label={action.label}
                    primary={action.primary}
                    pending={pending}
                    disabledReason={reason}
                    onClick={() => onSetState(strategy.strategyId, action.targetState)}
                  />
                  <span>{reason ?? 'Available'}</span>
                </div>
              )
            })}
          </div>
        </div>

        <div className="strategy-detail-section">
          <div className="strategy-section-title">
            <span>Parameters</span>
            <small>{isParameterLoading ? 'Loading' : activeParameters.length}</small>
          </div>
          {parameterError && <div className="inline-warning">{sanitizeCommandMessage(parameterError)}</div>}
          {activeParameters.length === 0 ? (
            <div className="empty-panel tight">No runtime parameters.</div>
          ) : (
            <div className="parameter-grid">
              {activeParameters.map((parameter) => (
                <div className="parameter-row" key={parameter.name}>
                  <span>{parameter.name}</span>
                  <strong>{parameter.value}</strong>
                </div>
              ))}
            </div>
          )}
          {editableParameters.length > 0 && (
            <div className="parameter-editor">
              <select
                value={selectedParameterName}
                onChange={(event) => {
                  const nextName = event.target.value
                  const nextParameter = editableParameters.find((parameter) => parameter.name === nextName)
                  setSelectedParameterName(nextName)
                  setDraftParameterValue(nextParameter?.value ?? '')
                }}
              >
                {editableParameters.map((parameter) => (
                  <option key={parameter.name} value={parameter.name}>{parameter.name}</option>
                ))}
              </select>
              <input
                value={draftParameterValue}
                onChange={(event) => setDraftParameterValue(event.target.value)}
              />
              <button
                className="command-button"
                type="button"
                disabled={!selectedParameterName
                  || draftParameterValue.trim() === (editableParameters.find((parameter) => parameter.name === selectedParameterName)?.value ?? '')
                  || busyCommand === `${strategy.strategyId}:parameters:update`}
                onClick={() => void onUpdateParameters(strategy.strategyId, { [selectedParameterName]: draftParameterValue })}
              >
                <RefreshCw size={15} aria-hidden="true" />
                Update
              </button>
            </div>
          )}
        </div>

        <div className="strategy-detail-section">
          <div className="strategy-section-title">
            <span>Version diff</span>
            <small>{parameterSnapshot?.configVersion ?? strategy.configVersion}</small>
          </div>
          {!parameterSnapshot || parameterSnapshot.recentVersions.length === 0 ? (
            <div className="empty-panel tight">No accepted parameter versions.</div>
          ) : (
            <div className="parameter-version-list">
              {parameterSnapshot.recentVersions.map((version) => (
                <article className="parameter-version-row" key={version.versionId}>
                  <div className="parameter-version-head">
                    <div>
                      <strong>{version.changeType} / {version.configVersion}</strong>
                      <span>{formatDate(version.createdAtUtc)} / {version.actor ?? version.source}</span>
                    </div>
                    <button
                      className="ghost-button"
                      type="button"
                      disabled={busyCommand === `${strategy.strategyId}:parameters:rollback:${version.versionId}`}
                      onClick={() => void onRollbackParameters(strategy.strategyId, version.versionId)}
                    >
                      <RefreshCw size={14} aria-hidden="true" />
                      Rollback
                    </button>
                  </div>
                  {version.diff.length === 0 ? (
                    <small>No changed parameters.</small>
                  ) : (
                    <div className="parameter-diff-list">
                      {version.diff.map((diff) => (
                        <div className="parameter-diff-row" key={`${version.versionId}:${diff.name}`}>
                          <span>{diff.name}</span>
                          <strong>{diff.previousValue} {'->'} {diff.nextValue}</strong>
                        </div>
                      ))}
                    </div>
                  )}
                </article>
              ))}
            </div>
          )}
        </div>

        <div className="strategy-detail-section">
          <div className="strategy-section-title">
            <span>Recent decisions</span>
            <small>{isDecisionLoading ? 'Loading' : `${recentDecisions.length}`}</small>
          </div>
          {decisionError && <div className="inline-warning">{sanitizeCommandMessage(decisionError)}</div>}
          {recentDecisions.length === 0 ? (
            <div className="empty-panel tight">{intl.formatMessage({ id: 'empty.decisions' })}</div>
          ) : (
            <div className="strategy-decision-list">
              {recentDecisions.map((decision) => (
                <article className="strategy-decision-row" key={decision.id}>
                  <time>{formatTime(decision.createdAtUtc)}</time>
                  <div>
                    <strong>{decision.action} / {decision.marketId ?? '-'}</strong>
                    <p>{decision.reason}</p>
                    <small>{decision.correlationId ?? decision.executionMode ?? decision.id}</small>
                  </div>
                </article>
              ))}
            </div>
          )}
        </div>

        <div className="strategy-detail-section">
          <div className="strategy-section-title">
            <span>Affected markets</span>
            <small>{strategy.activeMarkets} active</small>
          </div>
          {affectedMarkets.length === 0 ? (
            <div className="empty-panel tight">
              No market ids in recent strategy decisions or orders.
            </div>
          ) : (
            <div className="affected-market-list">
              {affectedMarkets.map((affectedMarket) => (
                <article className="affected-market-row" key={affectedMarket.marketId}>
                  <div>
                    <strong>{affectedMarket.market?.name ?? affectedMarket.marketId}</strong>
                    <span>{affectedMarket.marketId}</span>
                  </div>
                  <small>
                    {affectedMarket.decisionCount} decisions / {affectedMarket.orderCount} orders
                  </small>
                </article>
              ))}
            </div>
          )}
        </div>
      </div>
    </section>
  )
})

function canApplyStrategyTarget(strategy: ControlRoomStrategy, targetState: TargetStrategyState) {
  switch (targetState) {
    case 'Running':
      return strategy.enabled && !hasActiveBlockedReason(strategy) && strategy.state !== 'Running'
    case 'Paused':
      return strategy.state === 'Running'
    case 'Stopped':
      return strategy.state === 'Running' || strategy.state === 'Paused' || strategy.state === 'Faulted'
    default:
      return false
  }
}

function hasActiveBlockedReason(strategy: ControlRoomStrategy) {
  if (strategy.blockedReason && strategy.blockedReason.kind !== 'None') {
    return true
  }

  return !strategy.enabled || strategy.isKillSwitchBlocked || Boolean(strategy.lastError)
}

function describeBlockedReason(strategy: ControlRoomStrategy) {
  const reason = strategy.blockedReason
  if (reason && reason.kind !== 'None') {
    return `${formatBlockedReasonKind(reason.kind)}: ${reason.message || reason.code}`
  }

  if (strategy.lastError) {
    return `Strategy fault: ${strategy.lastError}`
  }

  if (!strategy.enabled) {
    return 'Disabled config: strategy is disabled by configuration'
  }

  if (strategy.isKillSwitchBlocked) {
    return 'Kill switch: strategy is blocked by risk controls'
  }

  return null
}

function formatBlockedReasonKind(kind: NonNullable<ControlRoomStrategy['blockedReason']>['kind']) {
  return kind
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/^./, (letter) => letter.toUpperCase())
}

function resolveStrategyHypothesis(strategy: ControlRoomStrategy) {
  return strategyHypotheses[strategy.strategyId] ??
    strategyHypotheses[strategy.name] ??
    'Evaluate configured market snapshots against strategy-specific signal, risk, and execution thresholds before emitting orders.'
}

interface StrategyDecisionDisplay {
  id: string
  action: string
  marketId: string | null
  reason: string
  createdAtUtc: string
  correlationId: string | null
  executionMode: string | null
}

function buildStrategyDecisionDisplays(
  strategyId: string | null,
  decisions: StrategyDecisionSummary[],
  snapshotDecisions: ControlRoomDecision[]
): StrategyDecisionDisplay[] {
  if (!strategyId) {
    return []
  }

  if (decisions.length > 0) {
    return decisions.map((decision) => ({
      id: decision.decisionId,
      action: decision.action,
      marketId: decision.marketId,
      reason: decision.reason,
      createdAtUtc: decision.createdAtUtc,
      correlationId: decision.correlationId,
      executionMode: decision.executionMode
    }))
  }

  return snapshotDecisions
    .filter((decision) => decision.strategyId === strategyId)
    .map((decision, index) => ({
      id: `${decision.strategyId}:${decision.marketId}:${decision.createdAtUtc}:${index}`,
      action: decision.action,
      marketId: decision.marketId,
      reason: decision.reason,
      createdAtUtc: decision.createdAtUtc,
      correlationId: null,
      executionMode: null
    }))
}

interface AffectedMarketDisplay {
  marketId: string
  market: ControlRoomMarket | null
  decisionCount: number
  orderCount: number
}

function buildAffectedMarkets(
  strategyId: string | null,
  decisions: StrategyDecisionDisplay[],
  markets: ControlRoomMarket[],
  orders: ControlRoomOrder[]
): AffectedMarketDisplay[] {
  if (!strategyId) {
    return []
  }

  const byMarketId = new Map<string, AffectedMarketDisplay>()
  const marketLookup = new Map(markets.map((market) => [market.marketId, market]))

  const ensureMarket = (marketId: string) => {
    const current = byMarketId.get(marketId)
    if (current) {
      return current
    }

    const next = {
      marketId,
      market: marketLookup.get(marketId) ?? null,
      decisionCount: 0,
      orderCount: 0
    }
    byMarketId.set(marketId, next)
    return next
  }

  decisions.forEach((decision) => {
    if (decision.marketId) {
      ensureMarket(decision.marketId).decisionCount += 1
    }
  })

  orders
    .filter((order) => order.strategyId === strategyId)
    .forEach((order) => {
      ensureMarket(order.marketId).orderCount += 1
    })

  return Array.from(byMarketId.values())
    .sort((left, right) =>
      (right.decisionCount + right.orderCount) - (left.decisionCount + left.orderCount) ||
      left.marketId.localeCompare(right.marketId))
    .slice(0, 8)
}

function buildStrategyCommandIntent(
  snapshot: ControlRoomSnapshot | null,
  targetState: TargetStrategyState
): ControlCommandIntent | null {
  const requiresConfirmation = snapshot?.commandMode === 'LiveServices' && targetState === 'Running'
  const confirmationText = requiresConfirmation ? requestTypedConfirmation() : undefined
  if (requiresConfirmation && confirmationText === null) {
    return null
  }

  return {
    reasonCode: 'UI_CONTROL',
    reason: `Control room strategy state ${targetState}`,
    confirmationText: confirmationText ?? undefined
  }
}

function buildParameterMutationIntent(
  snapshot: ControlRoomSnapshot | null,
  reason: string
): StrategyParameterMutationIntent | null {
  if (snapshot?.commandMode !== 'LiveServices') {
    return { reason }
  }

  const confirmationText = window.prompt('Live arming will be invalidated. Type DISARM LIVE to continue.')
  if (confirmationText === null) {
    return null
  }

  return {
    reason,
    invalidateLiveArming: true,
    liveDisarmConfirmationText: confirmationText
  }
}

function buildKillSwitchIntent(active: boolean): ControlCommandIntent | null {
  const confirmationText = requestTypedConfirmation()
  if (confirmationText === null) {
    return null
  }

  return {
    reasonCode: active ? 'UI_HARD_STOP' : 'UI_RESET',
    reason: active ? 'Control room hard stop' : 'Control room kill switch reset',
    confirmationText
  }
}

function requestTypedConfirmation() {
  const input = window.prompt('Type CONFIRM to continue')
  return input?.trim().toUpperCase() === 'CONFIRM' ? 'CONFIRM' : null
}

function applyCommandResponse(
  response: ControlRoomCommandResponse,
  setSnapshot: (snapshot: ControlRoomSnapshot) => void,
  setCommandResult: (result: CommandResult) => void
) {
  setSnapshot(response.snapshot)
  const status = response.status.trim()
  setCommandResult({
    tone: status === 'Accepted' ? 'good' : status === 'Rejected' ? 'danger' : 'watch',
    text: `${status}: ${sanitizeCommandMessage(response.message)}`
  })
}

function sanitizeCommandMessage(message: string) {
  return message
    .replace(/((?:private|api)[_-]?key|password|secret)\s*[:=]\s*[^,\s]+/gi, '$1=[redacted]')
    .slice(0, 240)
}

function resolveCommandSafety(
  snapshot: ControlRoomSnapshot | null,
  hasTransportError: boolean
): CommandSafety {
  if (hasTransportError || !snapshot) {
    return {
      level: 'offline',
      label: 'Offline',
      reason: 'API connection is offline',
      commandsEnabled: false
    }
  }

  if (snapshot.process.unhealthyChecks > 0 || snapshot.process.degradedChecks > 0 || snapshot.process.apiStatus === 'Degraded') {
    return {
      level: 'degraded',
      label: 'Degraded',
      reason: 'Health checks are degraded',
      commandsEnabled: false
    }
  }

  const commandMode = snapshot.commandMode
  if (commandMode === 'Paper') {
    return {
      level: 'paper',
      label: 'Paper commands',
      reason: snapshot.process.modulesEnabled ? 'Paper mode is armed' : 'Trading modules are not loaded',
      commandsEnabled: snapshot.process.modulesEnabled
    }
  }

  if (commandMode === 'LiveServices') {
    return {
      level: 'live',
      label: 'Live commands',
      reason: snapshot.process.modulesEnabled ? 'Live mode requires typed confirmation' : 'Trading modules are not loaded',
      commandsEnabled: snapshot.process.modulesEnabled
    }
  }

  return {
    level: 'readonly',
    label: 'Read-only',
    reason: snapshot.process.modulesEnabled ? 'Control room is read-only' : 'Trading modules are not loaded',
    commandsEnabled: false
  }
}

function resolveKillSwitchDisabledReason(
  active: boolean,
  risk: ControlRoomRisk | null,
  busyCommand: string | null,
  commandSafety: CommandSafety
) {
  if (!commandSafety.commandsEnabled) {
    return commandSafety.reason
  }

  const busyKey = active ? 'kill-switch:on' : 'kill-switch:off'
  if (busyCommand === busyKey) {
    return 'Command is pending'
  }

  if (active && risk?.killSwitchActive && risk.killSwitchLevel === 'HardStop') {
    return 'Hard stop is already active'
  }

  if (!active && !risk?.killSwitchActive) {
    return 'Kill switch is inactive'
  }

  return null
}

function resolveStrategyDisabledReason(
  strategy: ControlRoomStrategy,
  targetState: TargetStrategyState,
  strategyBusy: boolean,
  commandSafety: CommandSafety
) {
  if (!commandSafety.commandsEnabled) {
    return commandSafety.reason
  }

  if (strategyBusy) {
    return 'Command is pending'
  }

  if (targetState === 'Running') {
    if (strategy.state === 'Running') {
      return 'Strategy is already running'
    }

    if (!strategy.enabled) {
      return describeBlockedReason(strategy) ?? 'Strategy is disabled by configuration'
    }

    const blockedReason = describeBlockedReason(strategy)
    if (blockedReason) {
      return blockedReason
    }
  }

  if (!canApplyStrategyTarget(strategy, targetState)) {
    if (targetState === 'Paused') {
      return `Pause requires Running; current state is ${strategy.state}`
    }

    if (targetState === 'Stopped') {
      return `Stop requires Running, Paused, or Faulted; current state is ${strategy.state}`
    }

    return `Target state ${targetState} is not available from ${strategy.state}`
  }

  return null
}

function StrategyButton({
  icon,
  label,
  primary,
  pending,
  disabledReason,
  onClick
}: {
  icon: ReactNode
  label: string
  primary?: boolean
  pending?: boolean
  disabledReason: string | null
  onClick: () => Promise<void>
}) {
  return (
    <button
      className={[primary ? 'primary' : '', pending ? 'is-pending' : ''].filter(Boolean).join(' ')}
      type="button"
      title={disabledReason ?? label}
      disabled={Boolean(disabledReason)}
      onClick={() => void onClick()}
    >
      {icon}
      {label}
    </button>
  )
}

function MetricPair({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="metric-pair">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

function IdText({ value }: { value: string | null | undefined }) {
  const normalized = value?.trim()
  if (!normalized) {
    return <span className="id-text">-</span>
  }

  return (
    <span className="id-text" title={normalized}>
      {compactId(normalized)}
    </span>
  )
}

function MetricPill({ label, value, tone }: { label: string; value: string; tone?: 'good' | 'watch' }) {
  return (
    <span className={`metric-pill ${tone ?? ''}`}>
      <small>{label}</small>
      <strong>{value}</strong>
    </span>
  )
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError'
}

function toErrorMessage(error: unknown, fallback: string) {
  return sanitizeCommandMessage(error instanceof Error ? error.message : fallback)
}

function extractArcAccessDecision(error: unknown) {
  if (!(error instanceof ApiError)) {
    return null
  }

  if (isArcAccessDecision(error.payload)) {
    return error.payload
  }

  if (isArcPaperAutoTradeResponse(error.payload)) {
    return error.payload.accessDecision
  }

  return null
}

function extractArcPaperAutoTradeResponse(error: unknown) {
  return error instanceof ApiError && isArcPaperAutoTradeResponse(error.payload)
    ? error.payload
    : null
}

function isArcAccessDecision(payload: unknown): payload is ArcAccessDecision {
  if (!payload || typeof payload !== 'object') {
    return false
  }

  const candidate = payload as Partial<ArcAccessDecision>
  return typeof candidate.allowed === 'boolean'
    && typeof candidate.reasonCode === 'string'
    && typeof candidate.reason === 'string'
    && typeof candidate.requiredPermission === 'string'
    && typeof candidate.strategyKey === 'string'
    && typeof candidate.resourceKind === 'string'
    && typeof candidate.resourceId === 'string'
}

function isArcPaperAutoTradeResponse(payload: unknown): payload is ArcPaperAutoTradeResponse {
  if (!payload || typeof payload !== 'object') {
    return false
  }

  const candidate = payload as Partial<ArcPaperAutoTradeResponse>
  return typeof candidate.status === 'string'
    && typeof candidate.message === 'string'
    && isArcAccessDecision(candidate.accessDecision)
}

function createClientAutoTradeResponse(
  strategyKey: string,
  walletAddress: string,
  status: string,
  message: string
): ArcPaperAutoTradeResponse {
  return {
    status,
    message,
    command: null,
    accessDecision: {
      allowed: false,
      reasonCode: status,
      reason: message,
      requiredPermission: 'RequestPaperAutoTrade',
      strategyKey,
      walletAddress: walletAddress.trim() || null,
      resourceKind: 'subscriber-portal',
      resourceId: strategyKey || 'unknown',
      tier: null,
      expiresAtUtc: null,
      evidenceTransactionHash: null
    }
  }
}

function resolveAccessStatusTone(status: ArcStrategyAccessStatus | null) {
  if (!status) {
    return 'unknown'
  }

  if (status.hasAccess) {
    return 'active'
  }

  return status.statusCode === 'Expired' ? 'expired' : 'blocked'
}

function hasPermission(status: ArcStrategyAccessStatus | null, permission: ArcEntitlementPermission) {
  return Boolean(status?.hasAccess && status.permissions.includes(permission))
}

function resolvePaperAutoTradeDisabledReason(
  status: ArcStrategyAccessStatus | null,
  selectedStrategyId: string,
  isLoading: boolean
) {
  if (isLoading) {
    return 'Request is pending'
  }

  if (!selectedStrategyId.trim()) {
    return 'No strategy selected'
  }

  if (!status) {
    return 'Access status has not been checked'
  }

  if (!status.hasAccess) {
    return status.reason
  }

  if (!status.permissions.includes('RequestPaperAutoTrade')) {
    return 'RequestPaperAutoTrade permission is missing'
  }

  return null
}

function formatPermissionList(permissions: ArcEntitlementPermission[]) {
  return permissions.length === 0 ? '-' : permissions.join(', ')
}

function formatUsdc(value: number | null | undefined) {
  return value === null || value === undefined ? '-' : `$${formatCurrency(value)} USDC`
}

function formatPlanDuration(durationSeconds: number) {
  if (durationSeconds <= 0) {
    return '-'
  }

  const days = durationSeconds / 86400
  return days >= 1 ? `${days.toFixed(days % 1 === 0 ? 0 : 1)} days` : `${(durationSeconds / 3600).toFixed(1)} hours`
}

function compactJson(value: string | null | undefined) {
  if (!value?.trim()) {
    return '-'
  }

  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

function buildRevenueSettlementSummary(
  plan: ArcSubscriptionPlan | null,
  accessStatus: ArcStrategyAccessStatus | null,
  signal: ArcSignalDetail | ArcSignalSummary | null
): ArcRevenueSettlementSummary {
  const subscriptionRevenueUsdc = accessStatus?.hasAccess && plan ? plan.priceUsdc : 0
  const builderAttributedFlowUsdc = signal ? Number(signal.maxNotionalUsdc) : 0
  const simulatedBuilderShareUsdc = builderAttributedFlowUsdc * 0.7
  const simulatedTreasuryShareUsdc = builderAttributedFlowUsdc - simulatedBuilderShareUsdc

  return {
    generatedAtUtc: new Date().toISOString(),
    strategyKey: accessStatus?.strategyKey ?? plan?.strategyKey ?? signal?.strategyId ?? '-',
    source: accessStatus?.sourceTransactionHash ? 'testnet' : 'simulated',
    subscriptionRevenueUsdc,
    builderAttributedFlowUsdc,
    simulatedBuilderShareUsdc,
    simulatedTreasuryShareUsdc,
    settlementTransactionHash: accessStatus?.sourceTransactionHash ?? null
  }
}

function compareReadinessChecks(left: ReadinessCheckResult, right: ReadinessCheckResult) {
  const statusDelta = readinessStatusOrder[left.status] - readinessStatusOrder[right.status]
  if (statusDelta !== 0) {
    return statusDelta
  }

  const requirementDelta = readinessRequirementOrder[left.requirement] - readinessRequirementOrder[right.requirement]
  if (requirementDelta !== 0) {
    return requirementDelta
  }

  return left.id.localeCompare(right.id)
}

function readinessCheckTone(status: ReadinessCheckStatus) {
  return status.toLowerCase()
}

function readinessOverallTone(status: ReadinessOverallStatus) {
  return status.toLowerCase()
}

function formatReadinessStatus(status: ReadinessCheckStatus | ReadinessOverallStatus) {
  return status
}

function formatReadinessCapability(capability: ReadinessCapability) {
  return capability === 'PaperTrading' ? 'Paper trading' : 'Live trading'
}

function Fact({ icon, label, value }: { icon: ReactNode; label: string; value: string }) {
  return (
    <div className="fact">
      <span>{icon}</span>
      <div>
        <small>{label}</small>
        <strong>{value}</strong>
      </div>
    </div>
  )
}

const OrdersTable = memo(function OrdersTable({
  orders,
  compact = false
}: {
  orders: ControlRoomOrder[]
  compact?: boolean
}) {
  const intl = useIntl()
  return (
    <section className={`surface table-panel ${compact ? 'compact-table' : ''}`}>
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Execution</p>
          <h2>{intl.formatMessage({ id: 'nav.orders' })}</h2>
        </div>
      </div>
      {orders.length === 0 ? (
        <div className="empty-panel">{intl.formatMessage({ id: 'empty.orders' })}</div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{intl.formatMessage({ id: 'table.order' })}</th>
                <th>{intl.formatMessage({ id: 'table.strategy' })}</th>
                <th>{intl.formatMessage({ id: 'table.side' })}</th>
                <th>{intl.formatMessage({ id: 'table.price' })}</th>
                <th>{intl.formatMessage({ id: 'table.quantity' })}</th>
                <th>{intl.formatMessage({ id: 'table.status' })}</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((order) => (
                <tr key={order.clientOrderId}>
                  <td><IdText value={order.clientOrderId} /></td>
                  <td><IdText value={order.strategyId} /></td>
                  <td>{order.side}/{order.outcome}</td>
                  <td>{order.price.toFixed(3)}</td>
                  <td>{order.filledQuantity.toFixed(2)}/{order.quantity.toFixed(2)}</td>
                  <td>{order.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
})

const PositionsTable = memo(function PositionsTable({ positions }: { positions: ControlRoomPosition[] }) {
  const intl = useIntl()
  return (
    <section className="surface table-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Portfolio</p>
          <h2>{intl.formatMessage({ id: 'table.position' })}</h2>
        </div>
      </div>
      {positions.length === 0 ? (
        <div className="empty-panel">{intl.formatMessage({ id: 'empty.positions' })}</div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{intl.formatMessage({ id: 'nav.markets' })}</th>
                <th>{intl.formatMessage({ id: 'table.side' })}</th>
                <th>{intl.formatMessage({ id: 'table.quantity' })}</th>
                <th>{intl.formatMessage({ id: 'table.avgCost' })}</th>
                <th>{intl.formatMessage({ id: 'table.markPrice' })}</th>
                <th>{intl.formatMessage({ id: 'table.unrealizedPnl' })}</th>
                <th>{intl.formatMessage({ id: 'table.realizedPnl' })}</th>
                <th>{intl.formatMessage({ id: 'table.totalPnl' })}</th>
                <th>{intl.formatMessage({ id: 'table.return' })}</th>
              </tr>
            </thead>
            <tbody>
              {positions.map((position) => (
                <tr key={`${position.marketId}:${position.outcome}`}>
                  <td><IdText value={position.marketId} /></td>
                  <td>{position.outcome}</td>
                  <td>{position.quantity.toFixed(2)}</td>
                  <td>{position.averageCost.toFixed(3)}</td>
                  <td>{formatProbability(position.markPrice)}</td>
                  <td className={pnlClass(position.unrealizedPnl)}>{formatSignedCurrency(position.unrealizedPnl)}</td>
                  <td className={pnlClass(position.realizedPnl)}>{formatSignedCurrency(position.realizedPnl)}</td>
                  <td className={pnlClass(position.totalPnl)}>{formatSignedCurrency(position.totalPnl)}</td>
                  <td className={pnlClass(position.returnPct)}>{formatSignedPercent(position.returnPct)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
})

const DecisionList = memo(function DecisionList({ decisions }: { decisions: ControlRoomDecision[] }) {
  const intl = useIntl()
  return (
    <section className="surface decision-log">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Audit</p>
          <h2>{intl.formatMessage({ id: 'nav.decisions' })}</h2>
        </div>
      </div>
      {decisions.length === 0 ? (
        <div className="empty-panel">{intl.formatMessage({ id: 'empty.decisions' })}</div>
      ) : (
        <div className="decision-list">
          {decisions.map((decision) => (
            <article className="decision-item" key={`${decision.strategyId}:${decision.createdAtUtc}`}>
              <div className="decision-time">{formatTime(decision.createdAtUtc)}</div>
              <div className="decision-copy">
                <strong>{decision.action} / <IdText value={decision.marketId} /></strong>
                <p>{decision.reason}</p>
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  )
})

const PerformanceLedgerWorkspace = memo(function PerformanceLedgerWorkspace({
  agentReputation,
  strategyReputation,
  selectedStrategyId,
  provenanceHash,
  provenance,
  isLoading,
  isProvenanceLoading,
  error,
  provenanceError,
  onProvenanceHashChange,
  onLoadProvenance,
  onRefresh
}: {
  agentReputation: ArcAgentReputation | null
  strategyReputation: ArcAgentReputation | null
  selectedStrategyId: string | null
  provenanceHash: string
  provenance: ArcSubscriberProvenanceExplanation | null
  isLoading: boolean
  isProvenanceLoading: boolean
  error: string | null
  provenanceError: string | null
  onProvenanceHashChange: (value: string) => void
  onLoadProvenance: () => void
  onRefresh: () => void
}) {
  const [walletAddress, setWalletAddressState] = useState(readInitialSubscriberWallet)
  const [plans, setPlans] = useState<ArcSubscriptionPlan[]>([])
  const [selectedPlanId, setSelectedPlanId] = useState<number | null>(null)
  const [accessStatus, setAccessStatus] = useState<ArcStrategyAccessStatus | null>(null)
  const [opportunities, setOpportunities] = useState<ArcOpportunitySummary[]>([])
  const [selectedOpportunityId, setSelectedOpportunityId] = useState<string | null>(null)
  const [opportunityDetail, setOpportunityDetail] = useState<ArcOpportunityDetail | null>(null)
  const [opportunityDecision, setOpportunityDecision] = useState<ArcAccessDecision | null>(null)
  const [signals, setSignals] = useState<ArcSignalSummary[]>([])
  const [selectedSignalId, setSelectedSignalId] = useState<string | null>(null)
  const [signalDetail, setSignalDetail] = useState<ArcSignalDetail | null>(null)
  const [signalDecision, setSignalDecision] = useState<ArcAccessDecision | null>(null)
  const [performanceOutcome, setPerformanceOutcome] = useState<ArcPerformanceOutcomeRecord | null>(null)
  const [autoTradeResponse, setAutoTradeResponse] = useState<ArcPaperAutoTradeResponse | null>(null)
  const [portalError, setPortalError] = useState<string | null>(null)
  const [accessError, setAccessError] = useState<string | null>(null)
  const [opportunityError, setOpportunityError] = useState<string | null>(null)
  const [signalError, setSignalError] = useState<string | null>(null)
  const [performanceOutcomeError, setPerformanceOutcomeError] = useState<string | null>(null)
  const [isPortalLoading, setIsPortalLoading] = useState(false)
  const [isAccessLoading, setIsAccessLoading] = useState(false)
  const [isOpportunityLoading, setIsOpportunityLoading] = useState(false)
  const [isSignalLoading, setIsSignalLoading] = useState(false)
  const [isPerformanceOutcomeLoading, setIsPerformanceOutcomeLoading] = useState(false)
  const [isAutoTradeLoading, setIsAutoTradeLoading] = useState(false)

  const selectedPlan = useMemo(
    () => plans.find((plan) => plan.planId === selectedPlanId) ?? null,
    [plans, selectedPlanId]
  )
  const subscriberStrategyKey = selectedPlan?.strategyKey ?? selectedStrategyId ?? plans[0]?.strategyKey ?? ''
  const targetAutoTradeStrategyId = selectedStrategyId ?? subscriberStrategyKey
  const revenueSummary = useMemo(
    () => buildRevenueSettlementSummary(selectedPlan, accessStatus, signalDetail ?? signals[0] ?? null),
    [accessStatus, selectedPlan, signalDetail, signals]
  )

  const setWalletAddress = useCallback((value: string) => {
    setWalletAddressState(value)
    window.localStorage.setItem('autotrade.arcSubscriberWallet', value)
  }, [])

  const loadSubscriberCatalog = useCallback(async (signal?: AbortSignal) => {
    setIsPortalLoading(true)
    try {
      const [plansResult, opportunitiesResult, signalsResult] = await Promise.allSettled([
        getArcSubscriptionPlans(signal),
        getArcOpportunities(12, signal),
        getArcSignals(12, signal)
      ])
      const failures: string[] = []

      if (plansResult.status === 'fulfilled') {
        setPlans(plansResult.value)
      } else if (!isAbortError(plansResult.reason)) {
        failures.push(toErrorMessage(plansResult.reason, 'Subscription plans request failed.'))
      }

      if (opportunitiesResult.status === 'fulfilled') {
        setOpportunities(opportunitiesResult.value)
        setSelectedOpportunityId((current) =>
          current && opportunitiesResult.value.some((item) => item.opportunityId === current)
            ? current
            : opportunitiesResult.value[0]?.opportunityId ?? null)
      } else if (!isAbortError(opportunitiesResult.reason)) {
        failures.push(toErrorMessage(opportunitiesResult.reason, 'Opportunity request failed.'))
      }

      if (signalsResult.status === 'fulfilled') {
        setSignals(signalsResult.value)
        setSelectedSignalId((current) =>
          current && signalsResult.value.some((item) => item.signalId === current)
            ? current
            : signalsResult.value[0]?.signalId ?? null)
      } else if (!isAbortError(signalsResult.reason)) {
        failures.push(toErrorMessage(signalsResult.reason, 'Signal request failed.'))
      }

      setPortalError(failures.length > 0 ? failures.join(' ') : null)
    } finally {
      if (!signal?.aborted) {
        setIsPortalLoading(false)
      }
    }
  }, [])

  const loadAccessStatus = useCallback(async (signal?: AbortSignal) => {
    if (!walletAddress.trim() || !subscriberStrategyKey.trim()) {
      setAccessStatus(null)
      setAccessError(null)
      return
    }

    setIsAccessLoading(true)
    try {
      const nextStatus = await getArcStrategyAccessStatus(walletAddress.trim(), subscriberStrategyKey.trim(), signal)
      setAccessStatus(nextStatus)
      setAccessError(null)
    } catch (requestError) {
      if (isAbortError(requestError)) {
        return
      }

      setAccessStatus(null)
      setAccessError(toErrorMessage(requestError, 'Access status request failed.'))
    } finally {
      if (!signal?.aborted) {
        setIsAccessLoading(false)
      }
    }
  }, [subscriberStrategyKey, walletAddress])

  const loadSelectedOpportunity = useCallback(async (opportunityId: string | null, signal?: AbortSignal) => {
    if (!opportunityId) {
      setOpportunityDetail(null)
      setOpportunityDecision(null)
      setOpportunityError(null)
      return
    }

    setIsOpportunityLoading(true)
    try {
      const nextDetail = await getArcOpportunityDetail(opportunityId, walletAddress, signal)
      setOpportunityDetail(nextDetail)
      setOpportunityDecision(null)
      setOpportunityError(null)
    } catch (requestError) {
      if (isAbortError(requestError)) {
        return
      }

      const accessDecision = extractArcAccessDecision(requestError)
      setOpportunityDetail(null)
      setOpportunityDecision(accessDecision)
      setOpportunityError(accessDecision ? null : toErrorMessage(requestError, 'Opportunity detail request failed.'))
    } finally {
      if (!signal?.aborted) {
        setIsOpportunityLoading(false)
      }
    }
  }, [walletAddress])

  const loadSelectedSignal = useCallback(async (signalId: string | null, signal?: AbortSignal) => {
    if (!signalId) {
      setSignalDetail(null)
      setSignalDecision(null)
      setSignalError(null)
      return
    }

    setIsSignalLoading(true)
    try {
      const nextDetail = await getArcSignalDetail(signalId, walletAddress, signal)
      setSignalDetail(nextDetail)
      setSignalDecision(null)
      setSignalError(null)
      if (nextDetail.provenanceHash && !provenanceHash.trim()) {
        onProvenanceHashChange(nextDetail.provenanceHash)
      }
    } catch (requestError) {
      if (isAbortError(requestError)) {
        return
      }

      const accessDecision = extractArcAccessDecision(requestError)
      setSignalDetail(null)
      setSignalDecision(accessDecision)
      setSignalError(accessDecision ? null : toErrorMessage(requestError, 'Signal detail request failed.'))
    } finally {
      if (!signal?.aborted) {
        setIsSignalLoading(false)
      }
    }
  }, [onProvenanceHashChange, provenanceHash, walletAddress])

  const loadPerformanceOutcome = useCallback(async (signalId: string | null, signal?: AbortSignal) => {
    if (!signalId) {
      setPerformanceOutcome(null)
      setPerformanceOutcomeError(null)
      return
    }

    setIsPerformanceOutcomeLoading(true)
    try {
      const nextOutcome = await getArcPerformanceOutcome(signalId, signal)
      setPerformanceOutcome(nextOutcome)
      setPerformanceOutcomeError(null)
    } catch (requestError) {
      if (isAbortError(requestError)) {
        return
      }

      setPerformanceOutcome(null)
      setPerformanceOutcomeError(toErrorMessage(requestError, 'Performance outcome request failed.'))
    } finally {
      if (!signal?.aborted) {
        setIsPerformanceOutcomeLoading(false)
      }
    }
  }, [])

  const requestPaperAutoTrade = useCallback(async () => {
    if (!walletAddress.trim()) {
      setAutoTradeResponse(createClientAutoTradeResponse(
        subscriberStrategyKey,
        walletAddress,
        'MissingWallet',
        'Enter a subscriber wallet before requesting paper automation.'))
      return
    }

    if (!targetAutoTradeStrategyId.trim()) {
      setAutoTradeResponse(createClientAutoTradeResponse(
        subscriberStrategyKey,
        walletAddress,
        'MissingStrategy',
        'Select a strategy before requesting paper automation.'))
      return
    }

    setIsAutoTradeLoading(true)
    try {
      const response = await requestArcPaperAutoTrade(targetAutoTradeStrategyId, walletAddress.trim())
      setAutoTradeResponse(response)
    } catch (requestError) {
      const response = extractArcPaperAutoTradeResponse(requestError)
      setAutoTradeResponse(response ?? createClientAutoTradeResponse(
        subscriberStrategyKey,
        walletAddress,
        'RequestFailed',
        toErrorMessage(requestError, 'Paper auto-trade request failed.')))
    } finally {
      setIsAutoTradeLoading(false)
    }
  }, [subscriberStrategyKey, targetAutoTradeStrategyId, walletAddress])

  useEffect(() => {
    const controller = new AbortController()
    void loadSubscriberCatalog(controller.signal)
    return () => controller.abort()
  }, [loadSubscriberCatalog])

  useEffect(() => {
    if (plans.length === 0) {
      return
    }

    setSelectedPlanId((current) => {
      if (current && plans.some((plan) => plan.planId === current)) {
        return current
      }

      return plans.find((plan) => plan.strategyKey === selectedStrategyId)?.planId ?? plans[0].planId
    })
  }, [plans, selectedStrategyId])

  useEffect(() => {
    const controller = new AbortController()
    void loadAccessStatus(controller.signal)
    return () => controller.abort()
  }, [loadAccessStatus])

  useEffect(() => {
    const controller = new AbortController()
    void loadSelectedOpportunity(selectedOpportunityId, controller.signal)
    return () => controller.abort()
  }, [loadSelectedOpportunity, selectedOpportunityId])

  useEffect(() => {
    const controller = new AbortController()
    void loadSelectedSignal(selectedSignalId, controller.signal)
    return () => controller.abort()
  }, [loadSelectedSignal, selectedSignalId])

  useEffect(() => {
    const controller = new AbortController()
    void loadPerformanceOutcome(selectedSignalId, controller.signal)
    return () => controller.abort()
  }, [loadPerformanceOutcome, selectedSignalId])

  return (
    <section className="surface performance-workspace subscriber-workspace" aria-label="Arc subscriber portal">
      <div className="performance-hero subscriber-hero">
        <div className="performance-title">
          <p className="eyebrow">Arc settlement</p>
          <h2>Subscriber portal</h2>
          <span>Unlock paid signals, inspect proofs, request paper automation, and review settlement evidence.</span>
        </div>
        <div className="performance-actions">
          <MetricPill
            label="Coverage"
            value={agentReputation ? formatRateValue(agentReputation.confidenceCoverage) : '-'}
            tone={agentReputation && agentReputation.confidenceCoverage >= 0.8 ? 'good' : 'watch'}
          />
          <button className="command-button" type="button" disabled={isLoading} onClick={onRefresh}>
            <RefreshCw size={16} aria-hidden="true" />
            {isLoading || isPortalLoading ? 'Loading' : 'Refresh'}
          </button>
        </div>
      </div>

      {error && <div className="error-strip report-error">{error}</div>}
      {portalError && <div className="error-strip report-error">{portalError}</div>}

      <div className="subscriber-grid">
        <SubscriberIdentityPanel
          walletAddress={walletAddress}
          plans={plans}
          selectedPlan={selectedPlan}
          selectedPlanId={selectedPlanId}
          subscriberStrategyKey={subscriberStrategyKey}
          accessStatus={accessStatus}
          isLoading={isAccessLoading || isPortalLoading}
          error={accessError}
          onWalletChange={setWalletAddress}
          onPlanChange={setSelectedPlanId}
          onRefreshAccess={() => void loadAccessStatus()}
        />
        <SubscriptionPanel
          plan={selectedPlan}
          accessStatus={accessStatus}
          isLoading={isAccessLoading || isPortalLoading}
        />
      </div>

      <div className="subscriber-detail-grid">
        <OpportunityGatePanel
          opportunities={opportunities}
          selectedOpportunityId={selectedOpportunityId}
          detail={opportunityDetail}
          accessDecision={opportunityDecision}
          isLoading={isOpportunityLoading || isPortalLoading}
          error={opportunityError}
          onSelect={setSelectedOpportunityId}
          onRefresh={() => void loadSelectedOpportunity(selectedOpportunityId)}
        />
        <SignalProofPanel
          signals={signals}
          selectedSignalId={selectedSignalId}
          detail={signalDetail}
          accessDecision={signalDecision}
          isLoading={isSignalLoading || isPortalLoading}
          error={signalError}
          onSelect={setSelectedSignalId}
          onRefresh={() => void loadSelectedSignal(selectedSignalId)}
          onUseProvenance={(hash) => {
            onProvenanceHashChange(hash)
          }}
        />
      </div>

      <AutoTradePanel
        selectedStrategyId={targetAutoTradeStrategyId}
        accessStatus={accessStatus}
        response={autoTradeResponse}
        isLoading={isAutoTradeLoading}
        onRequest={requestPaperAutoTrade}
      />

      <div className="performance-scope-grid">
        <ReputationPanel
          title="Agent reputation"
          subtitle="All published Arc signals"
          reputation={agentReputation}
          isLoading={isLoading}
          emptyText="No agent outcomes have been recorded."
        />
        <ReputationPanel
          title="Strategy reputation"
          subtitle={selectedStrategyId ?? 'Strategy scope'}
          reputation={strategyReputation}
          isLoading={isLoading}
          emptyText={selectedStrategyId ? 'No strategy outcomes have been recorded.' : 'Strategy scope not selected.'}
        />
      </div>

      <LastOutcomePanel
        outcome={performanceOutcome}
        isLoading={isPerformanceOutcomeLoading}
        error={performanceOutcomeError}
        onRefresh={() => void loadPerformanceOutcome(selectedSignalId)}
      />

      <RevenueSettlementPanel summary={revenueSummary} />

      <ProvenancePanel
        provenanceHash={provenanceHash}
        provenance={provenance}
        isLoading={isProvenanceLoading}
        error={provenanceError}
        onProvenanceHashChange={onProvenanceHashChange}
        onLoad={onLoadProvenance}
      />
    </section>
  )
})

const SubscriberIdentityPanel = memo(function SubscriberIdentityPanel({
  walletAddress,
  plans,
  selectedPlan,
  selectedPlanId,
  subscriberStrategyKey,
  accessStatus,
  isLoading,
  error,
  onWalletChange,
  onPlanChange,
  onRefreshAccess
}: {
  walletAddress: string
  plans: ArcSubscriptionPlan[]
  selectedPlan: ArcSubscriptionPlan | null
  selectedPlanId: number | null
  subscriberStrategyKey: string
  accessStatus: ArcStrategyAccessStatus | null
  isLoading: boolean
  error: string | null
  onWalletChange: (value: string) => void
  onPlanChange: (planId: number | null) => void
  onRefreshAccess: () => void
}) {
  return (
    <article className="subscriber-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Subscriber identity</p>
          <h3>Wallet and tier</h3>
        </div>
        <ShieldCheck size={19} aria-hidden="true" />
      </div>

      <div className="subscriber-form-grid">
        <label>
          <span>Wallet</span>
          <input
            value={walletAddress}
            placeholder="0x..."
            spellCheck={false}
            onChange={(event) => onWalletChange(event.target.value)}
          />
        </label>
        <label>
          <span>Plan</span>
          <select
            value={selectedPlanId ?? ''}
            onChange={(event) => onPlanChange(event.target.value ? Number(event.target.value) : null)}
          >
            {plans.length === 0 ? (
              <option value="">No configured plans</option>
            ) : plans.map((plan) => (
              <option value={plan.planId} key={plan.planId}>
                {plan.planName} / {plan.strategyKey}
              </option>
            ))}
          </select>
        </label>
        <button className="command-button" type="button" disabled={isLoading} onClick={onRefreshAccess}>
          <RefreshCw size={16} aria-hidden="true" />
          {isLoading ? 'Checking' : 'Refresh access'}
        </button>
      </div>

      {error && <div className="error-strip report-error">{error}</div>}

      <div className="subscriber-status-strip">
        <span className={`access-status ${resolveAccessStatusTone(accessStatus)}`}>
          {accessStatus?.statusCode ?? 'Not checked'}
        </span>
        <span>{accessStatus?.reason ?? 'Enter a wallet to read entitlement status.'}</span>
      </div>

      <div className="subscriber-facts">
        <MetricPair label="Strategy key" value={subscriberStrategyKey || '-'} />
        <MetricPair label="Tier" value={accessStatus?.tier ?? selectedPlan?.tier ?? '-'} />
        <MetricPair label="Permissions" value={formatPermissionList(accessStatus?.permissions ?? selectedPlan?.permissions ?? [])} />
        <MetricPair label="Expires" value={formatNullableDate(accessStatus?.expiresAtUtc)} />
      </div>
    </article>
  )
})

const SubscriptionPanel = memo(function SubscriptionPanel({
  plan,
  accessStatus,
  isLoading
}: {
  plan: ArcSubscriptionPlan | null
  accessStatus: ArcStrategyAccessStatus | null
  isLoading: boolean
}) {
  return (
    <article className="subscriber-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Subscription</p>
          <h3>{plan?.planName ?? 'Plan status'}</h3>
        </div>
        <DatabaseZap size={19} aria-hidden="true" />
      </div>

      {!plan ? (
        <div className="empty-panel">{isLoading ? 'Loading plans.' : 'No Arc subscription plan is configured.'}</div>
      ) : (
        <>
          <div className="subscriber-facts">
            <MetricPair label="Price" value={formatUsdc(plan.priceUsdc)} />
            <MetricPair label="Duration" value={formatPlanDuration(plan.durationSeconds)} />
            <MetricPair label="Max markets" value={plan.maxMarkets ?? 'Unlimited'} />
            <MetricPair label="Status" value={accessStatus?.hasAccess ? 'Unlocked' : accessStatus?.statusCode ?? 'Unknown'} />
          </div>
          <div className="permission-list">
            {plan.permissions.map((permission) => (
              <span key={permission}>{permission}</span>
            ))}
          </div>
          <CopyableHash label="Subscription tx" value={accessStatus?.sourceTransactionHash ?? null} />
          {!accessStatus?.hasAccess && (
            <p className="subscriber-note">
              {accessStatus?.reason ?? 'This wallet has not unlocked the selected plan.'}
            </p>
          )}
        </>
      )}
    </article>
  )
})

const OpportunityGatePanel = memo(function OpportunityGatePanel({
  opportunities,
  selectedOpportunityId,
  detail,
  accessDecision,
  isLoading,
  error,
  onSelect,
  onRefresh
}: {
  opportunities: ArcOpportunitySummary[]
  selectedOpportunityId: string | null
  detail: ArcOpportunityDetail | null
  accessDecision: ArcAccessDecision | null
  isLoading: boolean
  error: string | null
  onSelect: (opportunityId: string | null) => void
  onRefresh: () => void
}) {
  const selectedSummary = opportunities.find((item) => item.opportunityId === selectedOpportunityId) ?? null

  return (
    <article className="subscriber-panel gated-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Gated opportunity</p>
          <h3>Published opportunity detail</h3>
        </div>
        <FileText size={19} aria-hidden="true" />
      </div>

      <div className="subscriber-toolbar">
        <select
          value={selectedOpportunityId ?? ''}
          onChange={(event) => onSelect(event.target.value || null)}
        >
          {opportunities.length === 0 ? (
            <option value="">No published opportunities</option>
          ) : opportunities.map((item) => (
            <option value={item.opportunityId} key={item.opportunityId}>
              {compactId(item.opportunityId)} / {item.marketId}
            </option>
          ))}
        </select>
        <button className="command-button" type="button" disabled={isLoading || !selectedOpportunityId} onClick={onRefresh}>
          <Search size={16} aria-hidden="true" />
          {isLoading ? 'Loading' : 'Inspect'}
        </button>
      </div>

      {error && <div className="error-strip report-error">{error}</div>}
      {selectedSummary && (
        <div className="subscriber-facts">
          <MetricPair label="Market" value={selectedSummary.marketId} />
          <MetricPair label="Outcome" value={selectedSummary.outcome} />
          <MetricPair label="Edge" value={formatBps(selectedSummary.edge * 10_000)} />
          <MetricPair label="Valid until" value={formatDate(selectedSummary.validUntilUtc)} />
        </div>
      )}

      {accessDecision && <AccessDecisionNotice decision={accessDecision} />}

      {!detail && !accessDecision ? (
        <div className="empty-panel">{isLoading ? 'Reading API-gated opportunity detail.' : 'Select an opportunity to inspect.'}</div>
      ) : detail ? (
        <div className="unlocked-detail">
          <div className="subscriber-facts">
            <MetricPair label="Fair probability" value={formatRateValue(detail.fairProbability)} />
            <MetricPair label="Confidence" value={formatRateValue(detail.confidence)} />
            <MetricPair label="Reasoning gate" value={detail.reasoningDecision.allowed ? 'Unlocked' : detail.reasoningDecision.reasonCode} />
            <MetricPair label="Evidence count" value={detail.evidence.length} />
          </div>
          <div className="detail-copy-block">
            <span>Reasoning</span>
            <p>{detail.reason ?? detail.reasoningDecision.reason}</p>
          </div>
          <div className="detail-copy-block">
            <span>Execution policy</span>
            <pre>{compactJson(detail.compiledPolicyJson)}</pre>
          </div>
          <div className="evidence-list compact">
            {detail.evidence.map((item) => (
              <div className="provenance-evidence-row" key={item.id}>
                <div>
                  <span>{item.sourceName}</span>
                  <strong>{item.title}</strong>
                  <p>{item.summary}</p>
                </div>
                <small>{compactId(item.contentHash)}</small>
              </div>
            ))}
          </div>
        </div>
      ) : (
        <div className="locked-detail">
          <strong>Full reasoning and execution policy are hidden by the API.</strong>
          <span>Required permission: {accessDecision?.requiredPermission ?? 'ViewSignals'}</span>
        </div>
      )}
    </article>
  )
})

const SignalProofPanel = memo(function SignalProofPanel({
  signals,
  selectedSignalId,
  detail,
  accessDecision,
  isLoading,
  error,
  onSelect,
  onRefresh,
  onUseProvenance
}: {
  signals: ArcSignalSummary[]
  selectedSignalId: string | null
  detail: ArcSignalDetail | null
  accessDecision: ArcAccessDecision | null
  isLoading: boolean
  error: string | null
  onSelect: (signalId: string | null) => void
  onRefresh: () => void
  onUseProvenance: (hash: string) => void
}) {
  const selectedSummary = signals.find((item) => item.signalId === selectedSignalId) ?? null

  return (
    <article className="subscriber-panel gated-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Signal proof</p>
          <h3>Published signal detail</h3>
        </div>
        <ClipboardCheck size={19} aria-hidden="true" />
      </div>

      <div className="subscriber-toolbar">
        <select value={selectedSignalId ?? ''} onChange={(event) => onSelect(event.target.value || null)}>
          {signals.length === 0 ? (
            <option value="">No published signals</option>
          ) : signals.map((item) => (
            <option value={item.signalId} key={item.signalId}>
              {compactId(item.signalId)} / {item.strategyId}
            </option>
          ))}
        </select>
        <button className="command-button" type="button" disabled={isLoading || !selectedSignalId} onClick={onRefresh}>
          <Search size={16} aria-hidden="true" />
          {isLoading ? 'Loading' : 'Inspect'}
        </button>
      </div>

      {error && <div className="error-strip report-error">{error}</div>}
      {selectedSummary && (
        <div className="subscriber-facts">
          <MetricPair label="Strategy" value={selectedSummary.strategyId} />
          <MetricPair label="Edge" value={formatBps(selectedSummary.expectedEdgeBps)} />
          <MetricPair label="Notional" value={formatUsdc(selectedSummary.maxNotionalUsdc)} />
          <MetricPair label="Status" value={selectedSummary.status} />
        </div>
      )}

      {accessDecision && <AccessDecisionNotice decision={accessDecision} />}

      {!detail && !accessDecision ? (
        <div className="empty-panel">{isLoading ? 'Reading API-gated signal detail.' : 'Select a signal to inspect.'}</div>
      ) : detail ? (
        <div className="unlocked-detail">
          <div className="subscriber-facts">
            <MetricPair label="Reasoning gate" value={detail.reasoningDecision.allowed ? 'Unlocked' : detail.reasoningDecision.reasonCode} />
            <MetricPair label="Venue" value={detail.venue} />
            <MetricPair label="Valid until" value={formatDate(detail.validUntilUtc)} />
            <MetricPair label="Published" value={formatNullableDate(detail.publishedAtUtc)} />
          </div>
          <CopyableHash label="Signal tx" value={detail.transactionHash} href={detail.explorerUrl} />
          <CopyableHash label="Signal hash" value={detail.signalHash} />
          <CopyableHash label="Reasoning hash" value={detail.reasoningHash} />
          <CopyableHash label="Risk envelope" value={detail.riskEnvelopeHash} />
          <CopyableHash label="Provenance" value={detail.provenanceHash} />
          {detail.provenanceHash && (
            <button className="command-button inline-action" type="button" onClick={() => onUseProvenance(detail.provenanceHash ?? '')}>
              <ChevronRight size={16} aria-hidden="true" />
              Use provenance hash
            </button>
          )}
        </div>
      ) : (
        <div className="locked-detail">
          <strong>Full signal proof is hidden by the API.</strong>
          <span>Required permission: {accessDecision?.requiredPermission ?? 'ViewSignals'}</span>
        </div>
      )}
    </article>
  )
})

const AutoTradePanel = memo(function AutoTradePanel({
  selectedStrategyId,
  accessStatus,
  response,
  isLoading,
  onRequest
}: {
  selectedStrategyId: string
  accessStatus: ArcStrategyAccessStatus | null
  response: ArcPaperAutoTradeResponse | null
  isLoading: boolean
  onRequest: () => void
}) {
  const disabledReason = resolvePaperAutoTradeDisabledReason(accessStatus, selectedStrategyId, isLoading)
  const decision = response?.accessDecision ?? null

  return (
    <article className="subscriber-panel automation-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Automation permission</p>
          <h3>Paper auto-trade request</h3>
        </div>
        <Zap size={19} aria-hidden="true" />
      </div>

      <div className="automation-grid">
        <div className="subscriber-facts">
          <MetricPair label="Target strategy" value={selectedStrategyId || '-'} />
          <MetricPair label="Paper permission" value={hasPermission(accessStatus, 'RequestPaperAutoTrade') ? 'Available' : 'Blocked'} />
          <MetricPair label="Live trading" value="Disabled in subscriber portal" />
          <MetricPair label="Blocked reason" value={disabledReason ?? 'None'} />
        </div>
        <button className="command-button" type="button" disabled={Boolean(disabledReason)} onClick={onRequest}>
          <Play size={16} aria-hidden="true" />
          {isLoading ? 'Requesting' : 'Request paper auto-trade'}
        </button>
      </div>

      {response && (
        <div className={`automation-result ${response.status === 'Accepted' ? 'good' : 'watch'}`}>
          <strong>{response.status}</strong>
          <span>{response.message}</span>
          <CopyableHash label="Access audit tx" value={decision?.evidenceTransactionHash ?? null} />
        </div>
      )}
    </article>
  )
})

const LastOutcomePanel = memo(function LastOutcomePanel({
  outcome,
  isLoading,
  error,
  onRefresh
}: {
  outcome: ArcPerformanceOutcomeRecord | null
  isLoading: boolean
  error: string | null
  onRefresh: () => void
}) {
  return (
    <article className="subscriber-panel outcome-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Performance outcome</p>
          <h3>Last signal outcome proof</h3>
        </div>
        <Gauge size={19} aria-hidden="true" />
      </div>

      {error && <div className="error-strip report-error">{error}</div>}
      {!outcome ? (
        <div className="empty-panel">{isLoading ? 'Loading latest outcome proof.' : 'No terminal outcome recorded for the selected signal.'}</div>
      ) : (
        <>
          <div className="subscriber-facts">
            <MetricPair label="Status" value={outcome.status} />
            <MetricPair label="Record" value={outcome.recordStatus} />
            <MetricPair label="PnL" value={formatBps(outcome.realizedPnlBps)} />
            <MetricPair label="Slippage" value={formatBps(outcome.slippageBps)} />
            <MetricPair label="Fill rate" value={formatRateValue(outcome.fillRate)} />
            <MetricPair label="Recorded" value={formatDate(outcome.recordedAtUtc)} />
          </div>
          <CopyableHash label="Outcome tx" value={outcome.transactionHash} href={outcome.explorerUrl} />
          <CopyableHash label="Outcome hash" value={outcome.outcomeHash} />
        </>
      )}

      <div className="panel-action-row">
        <button className="command-button" type="button" disabled={isLoading} onClick={onRefresh}>
          <RefreshCw size={16} aria-hidden="true" />
          {isLoading ? 'Loading' : 'Refresh outcome'}
        </button>
      </div>
    </article>
  )
})

const RevenueSettlementPanel = memo(function RevenueSettlementPanel({
  summary
}: {
  summary: ArcRevenueSettlementSummary
}) {
  return (
    <article className="subscriber-panel settlement-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Revenue settlement</p>
          <h3>Builder attribution</h3>
        </div>
        <TrendingUp size={19} aria-hidden="true" />
      </div>

      <div className="subscriber-facts">
        <MetricPair label="Source" value={summary.source} />
        <MetricPair label="Subscription revenue" value={formatUsdc(summary.subscriptionRevenueUsdc)} />
        <MetricPair label="Builder flow" value={formatUsdc(summary.builderAttributedFlowUsdc)} />
        <MetricPair label="Builder split" value={formatUsdc(summary.simulatedBuilderShareUsdc)} />
        <MetricPair label="Treasury split" value={formatUsdc(summary.simulatedTreasuryShareUsdc)} />
        <MetricPair label="Generated" value={formatDate(summary.generatedAtUtc)} />
      </div>
      <CopyableHash label="Settlement tx" value={summary.settlementTransactionHash} />
      <p className="subscriber-note">Settlement values are explicitly {summary.source}; no mainnet revenue claim is implied.</p>
    </article>
  )
})

function AccessDecisionNotice({ decision }: { decision: ArcAccessDecision }) {
  return (
    <div className={`access-decision ${decision.allowed ? 'allowed' : 'denied'}`}>
      <strong>{decision.allowed ? 'Access granted' : 'Access denied'} / {decision.reasonCode}</strong>
      <span>{decision.reason}</span>
      <small>
        API gate required {decision.requiredPermission} for {decision.resourceKind}:{compactNullableId(decision.resourceId)}
      </small>
    </div>
  )
}

function CopyableHash({
  label,
  value,
  href
}: {
  label: string
  value: string | null | undefined
  href?: string | null
}) {
  const normalized = value?.trim() ?? ''
  return (
    <div className="copyable-hash">
      <span>{label}</span>
      {href && normalized ? (
        <a href={href} target="_blank" rel="noreferrer">{compactId(normalized)}</a>
      ) : (
        <strong title={normalized || undefined}>{normalized ? compactId(normalized) : '-'}</strong>
      )}
      <button
        type="button"
        className="copy-button"
        disabled={!normalized}
        title={normalized ? `Copy ${label}` : `${label} unavailable`}
        onClick={() => {
          if (normalized) {
            void navigator.clipboard.writeText(normalized)
          }
        }}
      >
        <ClipboardCheck size={14} aria-hidden="true" />
      </button>
    </div>
  )
}

const ProvenancePanel = memo(function ProvenancePanel({
  provenanceHash,
  provenance,
  isLoading,
  error,
  onProvenanceHashChange,
  onLoad
}: {
  provenanceHash: string
  provenance: ArcSubscriberProvenanceExplanation | null
  isLoading: boolean
  error: string | null
  onProvenanceHashChange: (value: string) => void
  onLoad: () => void
}) {
  return (
    <article className="provenance-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">Signal provenance</p>
          <h3>Source evidence</h3>
        </div>
        <ClipboardCheck size={19} aria-hidden="true" />
      </div>

      <div className="provenance-loader">
        <label htmlFor="arc-provenance-hash">Provenance hash</label>
        <div className="provenance-loader-row">
          <input
            id="arc-provenance-hash"
            value={provenanceHash}
            placeholder="0x..."
            onChange={(event) => onProvenanceHashChange(event.target.value)}
          />
          <button className="command-button" type="button" disabled={isLoading} onClick={onLoad}>
            <Search size={16} aria-hidden="true" />
            {isLoading ? 'Loading' : 'Load'}
          </button>
        </div>
      </div>

      {error && <div className="error-strip report-error">{error}</div>}

      {!provenance ? (
        <div className="empty-panel">No provenance document loaded.</div>
      ) : (
        <div className="provenance-body">
          <div className="provenance-facts">
            <MetricPair label="Source" value={provenance.sourceModule} />
            <MetricPair label="Validation" value={provenance.validationStatus} />
            <MetricPair label="Strategy" value={provenance.strategyId} />
            <MetricPair label="Market" value={provenance.marketId} />
            <MetricPair label="Source id" value={provenance.sourceId} />
            <MetricPair label="Created" value={formatDate(provenance.createdAtUtc)} />
          </div>

          <div className="provenance-hashes">
            <MetricPair label="Provenance" value={compactNullableId(provenance.provenanceHash)} />
            <MetricPair label="Evidence" value={compactNullableId(provenance.evidenceSummaryHash)} />
            <MetricPair label="LLM output" value={compactNullableId(provenance.llmOutputHash)} />
            <MetricPair label="Policy" value={compactNullableId(provenance.compiledPolicyHash)} />
            <MetricPair label="Risk" value={compactNullableId(provenance.riskEnvelopeHash)} />
            <MetricPair label="Package" value={compactNullableId(provenance.generatedPackageHash)} />
          </div>

          <div className="provenance-evidence-list">
            {provenance.evidence.map((item) => (
              <div className="provenance-evidence-row" key={item.evidenceId}>
                <div>
                  <span>{item.title}</span>
                  <strong>{item.summary}</strong>
                </div>
                <small>{compactNullableId(item.contentHash)}</small>
              </div>
            ))}
          </div>

          <p className="provenance-note">{provenance.privacyNote}</p>
        </div>
      )}
    </article>
  )
})

function ReputationPanel({
  title,
  subtitle,
  reputation,
  isLoading,
  emptyText
}: {
  title: string
  subtitle: string
  reputation: ArcAgentReputation | null
  isLoading: boolean
  emptyText: string
}) {
  if (!reputation) {
    return (
      <article className="performance-panel">
        <div className="section-head compact">
          <div>
            <p className="eyebrow">{subtitle}</p>
            <h3>{title}</h3>
          </div>
          <Gauge size={19} aria-hidden="true" />
        </div>
        <div className="empty-panel">{isLoading ? 'Loading Arc reputation.' : emptyText}</div>
      </article>
    )
  }

  return (
    <article className="performance-panel">
      <div className="section-head compact">
        <div>
          <p className="eyebrow">{subtitle}</p>
          <h3>{title}</h3>
        </div>
        <span className="calculated-at">{formatDate(reputation.calculatedAtUtc)}</span>
      </div>

      <div className="performance-summary-grid">
        <PerformanceReadout label="Signals" value={reputation.totalSignals} detail="published" />
        <PerformanceReadout label="Terminal" value={reputation.terminalSignals} detail={formatRateValue(reputation.confidenceCoverage)} />
        <PerformanceReadout label="Pending" value={reputation.pendingSignals} detail="not terminal" />
        <PerformanceReadout label="Executed" value={reputation.executedSignals} detail={`${reputation.winCount}/${reputation.lossCount}/${reputation.flatCount}`} />
        <PerformanceReadout label="Avg PnL" value={formatBps(reputation.averageRealizedPnlBps)} detail="realized" />
        <PerformanceReadout label="Avg slip" value={formatBps(reputation.averageSlippageBps)} detail="execution" />
        <PerformanceReadout label="Risk reject" value={formatRateValue(reputation.riskRejectionRate)} detail={`${reputation.rejectedSignals} rejected`} />
        <PerformanceReadout label="Expired" value={reputation.expiredSignals} detail="terminal" />
      </div>

      <div className="outcome-mix">
        <OutcomeMixBar label="Wins" count={reputation.winCount} total={reputation.totalSignals} tone="good" />
        <OutcomeMixBar label="Losses" count={reputation.lossCount} total={reputation.totalSignals} tone="danger" />
        <OutcomeMixBar label="Rejected" count={reputation.rejectedSignals} total={reputation.totalSignals} tone="watch" />
        <OutcomeMixBar label="Expired" count={reputation.expiredSignals} total={reputation.totalSignals} tone="muted" />
        <OutcomeMixBar label="Pending" count={reputation.pendingSignals} total={reputation.totalSignals} tone="neutral" />
      </div>
    </article>
  )
}

function PerformanceReadout({ label, value, detail }: { label: string; value: string | number; detail: string }) {
  return (
    <div className="performance-readout">
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </div>
  )
}

function OutcomeMixBar({
  label,
  count,
  total,
  tone
}: {
  label: string
  count: number
  total: number
  tone: 'good' | 'danger' | 'watch' | 'muted' | 'neutral'
}) {
  const width = total <= 0 ? 0 : Math.max(2, Math.min(100, (count / total) * 100))
  return (
    <div className="outcome-mix-row">
      <div>
        <span>{label}</span>
        <strong>{count}</strong>
      </div>
      <div className="outcome-track" aria-hidden="true">
        <span className={`outcome-fill ${tone}`} style={{ width: `${width}%` }} />
      </div>
    </div>
  )
}

const RunReportWorkspace = memo(function RunReportWorkspace({
  sessionId,
  activeSession,
  report,
  checklist,
  isLoading,
  error,
  onSessionIdChange,
  onLoad
}: {
  sessionId: string
  activeSession: PaperRunSessionRecord | null
  report: PaperRunReport | null
  checklist: PaperPromotionChecklist | null
  isLoading: boolean
  error: string | null
  onSessionIdChange: (sessionId: string) => void
  onLoad: () => void
}) {
  const gateStatus = checklist?.overallStatus ?? 'Not loaded'
  const gateTone = checklist?.overallStatus === 'Passed' ? 'good' : checklist ? 'danger' : 'watch'
  const attribution = report?.attribution

  return (
    <section className="surface report-workspace" aria-label="Paper run report">
      <div className="report-hero">
        <div className="report-title">
          <p className="eyebrow">Paper evidence</p>
          <h2>Run report and evidence gate</h2>
          <span>Promotion evidence is read-only. Passing this gate never arms Live by itself.</span>
        </div>
        <div className="report-loader">
          <label htmlFor="run-session-id">Run session id</label>
          <div className="report-loader-row">
            <input
              id="run-session-id"
              value={sessionId}
              placeholder="00000000-0000-0000-0000-000000000000"
              onChange={(event) => onSessionIdChange(event.target.value)}
            />
            <button className="command-button" type="button" disabled={isLoading} onClick={onLoad}>
              <RefreshCw size={16} aria-hidden="true" />
              {isLoading ? 'Loading' : 'Load'}
            </button>
          </div>
          {activeSession ? (
            <small className="report-session-hint">
              Active Paper session {compactId(activeSession.sessionId)} started {formatDate(activeSession.startedAtUtc)}
            </small>
          ) : (
            <small className="report-session-hint">No active Paper session discovered yet.</small>
          )}
        </div>
      </div>

      {error && <div className="error-strip report-error">{error}</div>}

      {!report ? (
        <div className="empty-panel">
          Load a Paper run session to inspect JSON evidence, promotion criteria, residual Live risks, and attribution.
        </div>
      ) : (
        <>
          <div className="report-gate">
            <div className={`gate-status ${gateTone}`}>
              <span>Evidence gate</span>
              <strong>{gateStatus}</strong>
              <small>{checklist?.canConsiderLive ? 'Can consider Live after separate arming flow' : 'Live consideration blocked'}</small>
            </div>
            <div className="gate-copy">
              <span className="mode-chip paper">Paper evidence</span>
              <span className="mode-chip readonly">Live arming unchanged</span>
              <p>{checklist?.liveArmingUnchanged ? 'Checklist evaluation did not mutate Live arming state.' : 'Checklist state is missing; do not use this run for promotion.'}</p>
            </div>
            <div className="gate-copy">
              <span>Residual Live risks</span>
              {checklist && checklist.residualRisks.length > 0 ? (
                <ul>
                  {checklist.residualRisks.map((risk) => <li key={risk}>{risk}</li>)}
                </ul>
              ) : (
                <p>No residual risks reported by the checklist.</p>
              )}
            </div>
          </div>

          <div className="report-summary-grid">
            <MetricPair label="Session" value={compactId(report.session.sessionId)} />
            <MetricPair label="Mode" value={report.session.executionMode} />
            <MetricPair label="Status" value={report.reportStatus} />
            <MetricPair label="Started" value={formatDate(report.session.startedAtUtc)} />
            <MetricPair label="Stopped" value={formatNullableDate(report.session.stoppedAtUtc)} />
            <MetricPair label="Net PnL" value={formatSignedCurrency(report.summary.netPnl)} />
            <MetricPair label="Fees" value={formatCurrency(report.summary.totalFees)} />
            <MetricPair label="Trades" value={report.summary.tradeCount} />
          </div>

          <div className="report-section-grid">
            <section className="report-block">
              <div className="section-head mini">
                <div>
                  <p className="eyebrow">Attribution</p>
                  <h3>PnL, slippage, latency</h3>
                </div>
                <ClipboardCheck size={18} aria-hidden="true" />
              </div>
              <div className="attribution-grid">
                <MetricPair label="Realized PnL" value={formatSignedCurrency(attribution?.pnl.realizedPnl)} />
                <MetricPair label="Unrealized PnL" value={formatSignedCurrency(attribution?.pnl.unrealizedPnl)} />
                <MetricPair label="Slippage" value={formatSignedCurrency(attribution?.slippage.estimatedSlippage)} />
                <MetricPair label="Decision to fill" value={formatDurationMs(attribution?.latency.averageDecisionToFillLatencyMs)} />
                <MetricPair label="Stale events" value={attribution?.staleData.eventCount ?? 0} />
                <MetricPair label="Unhedged seconds" value={formatDurationSeconds(attribution?.unhedgedExposure.totalDurationSeconds)} />
              </div>
              <div className="report-notes">
                {(attribution?.reconciliationNotes ?? []).map((note) => <p key={note}>{note}</p>)}
                {(attribution?.pnl.notes ?? []).map((note) => <p key={note}>{note}</p>)}
              </div>
            </section>

            <section className="report-block">
              <div className="section-head mini">
                <div>
                  <p className="eyebrow">Checklist</p>
                  <h3>Promotion criteria</h3>
                </div>
                <ShieldCheck size={18} aria-hidden="true" />
              </div>
              {!checklist ? (
                <div className="empty-panel">Checklist was not loaded.</div>
              ) : (
                <div className="criteria-list">
                  {checklist.criteria.map((criterion) => (
                    <article className={`criterion-row ${criterion.status.toLowerCase()}`} key={criterion.id}>
                      <div>
                        <strong>{criterion.name}</strong>
                        <span>{criterion.reason}</span>
                        {criterion.evidenceIds.length > 0 && <small>{criterion.evidenceIds.map(compactId).join(' / ')}</small>}
                      </div>
                      <b>{criterion.status}</b>
                    </article>
                  ))}
                </div>
              )}
            </section>
          </div>

          <div className="report-section-grid">
            <ReportBreakdownTable title="Strategy attribution" rows={report.strategyBreakdown} rowKey="strategyId" />
            <ReportBreakdownTable title="Market attribution" rows={report.marketBreakdown} rowKey="marketId" />
          </div>

          <section className="report-block">
            <div className="section-head mini">
              <div>
                <p className="eyebrow">Exports</p>
                <h3>Audit references</h3>
              </div>
              <FileText size={18} aria-hidden="true" />
            </div>
            <div className="export-grid">
              <MetricPair label="JSON API" value={report.exportReferences.jsonApi} />
              <MetricPair label="JSON CLI" value={report.exportReferences.jsonCli} />
              <MetricPair label="CSV CLI" value={report.exportReferences.csvCli} />
              <MetricPair label="CSV tables" value={report.exportReferences.csvTables.join(', ')} />
            </div>
          </section>
        </>
      )}
    </section>
  )
})

function ReportBreakdownTable({
  title,
  rows,
  rowKey
}: {
  title: string
  rows: Array<PaperRunStrategyBreakdown | PaperRunMarketBreakdown>
  rowKey: 'strategyId' | 'marketId'
}) {
  return (
    <section className="report-block">
      <div className="section-head mini">
        <div>
          <p className="eyebrow">Breakdown</p>
          <h3>{title}</h3>
        </div>
      </div>
      <div className="table-wrap">
        <table className="report-table">
          <thead>
            <tr>
              <th>Key</th>
              <th>Decisions</th>
              <th>Trades</th>
              <th>Net PnL</th>
              <th>Slippage</th>
              <th>Latency</th>
              <th>Unhedged</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={getReportBreakdownKey(row, rowKey)}>
                <td>{getReportBreakdownKey(row, rowKey)}</td>
                <td>{row.decisionCount}</td>
                <td>{row.tradeCount}</td>
                <td className={pnlClass(row.netPnl)}>{formatSignedCurrency(row.netPnl)}</td>
                <td className={pnlClass(row.estimatedSlippage)}>{formatSignedCurrency(row.estimatedSlippage)}</td>
                <td>{formatDurationMs(row.averageDecisionToFillLatencyMs)}</td>
                <td>{formatDurationSeconds(row.unhedgedExposureSeconds)} / {formatCurrency(row.unhedgedExposureNotional)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

function getReportBreakdownKey(
  row: PaperRunStrategyBreakdown | PaperRunMarketBreakdown,
  rowKey: 'strategyId' | 'marketId'
) {
  return rowKey === 'strategyId' && 'strategyId' in row ? row.strategyId : 'marketId' in row ? row.marketId : '-'
}

function translateMetricLabel(label: string, formatMessage: (descriptor: { id: string }) => string) {
  const key = label.toLowerCase()
  const map: Record<string, string> = {
    'running strategies': 'metric.runningStrategies',
    'open notional': 'metric.openNotional',
    'available capital': 'metric.availableCapital',
    'paper pnl': 'metric.paperPnl',
    'market liquidity': 'metric.marketLiquidity',
    'last decision': 'metric.lastDecision',
    'best bid': 'metric.bestBid',
    'best ask': 'metric.bestAsk',
    spread: 'book.spread',
    'depth imbalance': 'metric.depthImbalance',
    'book freshness': 'metric.bookFreshness',
    'signal score': 'metric.signalScore'
  }

  const id = map[key]
  return id ? formatMessage({ id }) : label
}

function translateOperationalCopy(value: string, intl: IntlShape) {
  const normalized = value.trim()
  const key = normalized.toLowerCase()
  const statusMap: Record<string, string> = {
    fresh: 'book.freshness.fresh.label',
    delayed: 'book.freshness.delayed.label',
    stale: 'book.freshness.stale.label',
    empty: 'book.freshness.empty.label'
  }
  const statusId = statusMap[key]
  if (statusId) {
    return intl.formatMessage({ id: statusId })
  }

  const freshMatch = /^order book updated (\d+)s ago\.?$/i.exec(normalized)
  if (freshMatch) {
    return intl.formatMessage({ id: 'book.freshness.fresh.message' }, { ageSeconds: Number(freshMatch[1]) })
  }

  const delayedMatch = /^order book is delayed by (\d+)s\.?$/i.exec(normalized)
  if (delayedMatch) {
    return intl.formatMessage({ id: 'book.freshness.delayed.message' }, { ageSeconds: Number(delayedMatch[1]) })
  }

  const staleMatch = /^order book is stale by (\d+)s; do not treat it as live\.?$/i.exec(normalized)
  if (staleMatch) {
    return intl.formatMessage({ id: 'book.freshness.stale.message' }, { ageSeconds: Number(staleMatch[1]) })
  }

  return value
}

function parseOptionalNumber(value: string) {
  const normalized = value.trim()
  if (!normalized) {
    return undefined
  }

  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? Math.max(0, parsed) : undefined
}

function resolveMarketRankScore(market: ControlRoomMarket) {
  return typeof market.rankScore === 'number' ? market.rankScore : market.signalScore
}

function resolveMarketRankReason(market: ControlRoomMarket) {
  if (market.rankReason?.trim()) {
    return market.rankReason.trim()
  }

  const signalLabel = market.signalScore >= 0.7 ? 'strong signal' : market.signalScore >= 0.45 ? 'moderate signal' : 'weak signal'
  const liquidityLabel = market.liquidity >= 25_000 ? 'deep liquidity' : market.liquidity >= 1_000 ? 'usable liquidity' : 'thin liquidity'
  const volumeLabel = market.volume24h >= 10_000 ? 'active 24h volume' : market.volume24h >= 500 ? 'some 24h volume' : 'quiet 24h volume'
  const acceptingLabel = market.acceptingOrders ? 'accepting orders' : 'not accepting orders'
  return `${signalLabel}; ${liquidityLabel}; ${volumeLabel}; ${acceptingLabel}`
}

function resolveMarketUnsuitableReasons(market: ControlRoomMarket) {
  if (market.unsuitableReasons && market.unsuitableReasons.length > 0) {
    return market.unsuitableReasons.filter((reason) => reason.trim().length > 0)
  }

  const reasons: string[] = []
  if (market.status.toLowerCase() !== 'active') {
    reasons.push(`Market status is ${market.status}.`)
  }
  if (!market.acceptingOrders) {
    reasons.push('Market is not accepting orders.')
  }
  if (market.liquidity < 1_000) {
    reasons.push('Liquidity is below 1000.')
  }
  if (market.volume24h < 500) {
    reasons.push('24h volume is below 500.')
  }
  if (market.signalScore < 0.25) {
    reasons.push('Signal score is below 0.25.')
  }

  return reasons
}

function translateMarketDiscoveryCopy(copy: string, intl: IntlShape) {
  const separator = intl.locale.toLowerCase().startsWith('zh') ? '；' : '; '
  return copy
    .split(';')
    .map((part) => translateMarketDiscoveryPhrase(part, intl))
    .filter((part) => part.length > 0)
    .join(separator)
}

function translateMarketDiscoveryPhrase(phrase: string, intl: IntlShape) {
  const normalized = phrase.trim().replace(/\.$/, '')
  const key = normalized.toLowerCase()
  const staticMap: Record<string, string> = {
    'strong signal': 'market.reason.signal.strong',
    'moderate signal': 'market.reason.signal.moderate',
    'weak signal': 'market.reason.signal.weak',
    'deep liquidity': 'market.reason.liquidity.deep',
    'usable liquidity': 'market.reason.liquidity.usable',
    'thin liquidity': 'market.reason.liquidity.thin',
    'active 24h volume': 'market.reason.volume.active',
    'some 24h volume': 'market.reason.volume.some',
    'quiet 24h volume': 'market.reason.volume.quiet',
    'accepting orders': 'market.reason.accepting',
    'not accepting orders': 'market.reason.notAccepting',
    'expiry unavailable': 'market.reason.expiry.unavailable',
    'expiry is unavailable': 'market.reason.expiry.unavailable',
    expired: 'market.reason.expired',
    'market has expired': 'market.reason.expired',
    'expires within 24h': 'market.reason.expiresWithin24h',
    'market is not accepting orders': 'market.reason.notAccepting'
  }
  const id = staticMap[key]
  if (id) {
    return intl.formatMessage({ id })
  }

  const statusMatch = /^market status is (.+)$/i.exec(normalized)
  if (statusMatch) {
    return intl.formatMessage(
      { id: 'market.reason.status' },
      { status: translateMarketStatus(statusMatch[1], (descriptor) => intl.formatMessage(descriptor)) }
    )
  }

  const expiresInDaysMatch = /^expires in (\d+)d$/i.exec(normalized)
  if (expiresInDaysMatch) {
    return intl.formatMessage({ id: 'market.reason.expiresInDays' }, { days: Number(expiresInDaysMatch[1]) })
  }

  const liquidityBelowMatch = /^liquidity is below ([\d.]+)$/i.exec(normalized)
  if (liquidityBelowMatch) {
    return intl.formatMessage({ id: 'market.reason.liquidityBelow' }, { value: liquidityBelowMatch[1] })
  }

  const volumeBelowMatch = /^24h volume is below ([\d.]+)$/i.exec(normalized)
  if (volumeBelowMatch) {
    return intl.formatMessage({ id: 'market.reason.volumeBelow' }, { value: volumeBelowMatch[1] })
  }

  const signalBelowMatch = /^signal score is below ([\d.]+)$/i.exec(normalized)
  if (signalBelowMatch) {
    return intl.formatMessage({ id: 'market.reason.signalBelow' }, { value: signalBelowMatch[1] })
  }

  const spreadWideMatch = /^spread is wider than ([\d.]+)$/i.exec(normalized)
  if (spreadWideMatch) {
    return intl.formatMessage({ id: 'market.reason.spreadWide' }, { value: spreadWideMatch[1] })
  }

  return normalized
}

function resolveMarketStatusTone(market: ControlRoomMarket) {
  const status = market.status.toLowerCase()
  if (status === 'active' && market.acceptingOrders) {
    return 'open'
  }

  if (status === 'closed') {
    return 'closed'
  }

  return 'paused'
}

function isOrderBookForSelectedToken(
  orderBook: ControlRoomOrderBook | null | undefined,
  selectedToken: ControlRoomMarketToken | null
) {
  if (!orderBook) {
    return false
  }

  return !selectedToken || orderBook.tokenId === selectedToken.tokenId
}

function buildOrderBookMicrostructureMetrics(
  orderBook: ControlRoomOrderBook | null,
  market: ControlRoomMarket,
  fallbackMetrics: ControlRoomMetric[]
): ControlRoomMetric[] {
  if (!orderBook) {
    return fallbackMetrics
  }

  return [
    {
      label: 'Best bid',
      value: formatProbability(orderBook.bestBidPrice),
      delta: formatBookSizeDelta(orderBook.bestBidSize),
      tone: orderBook.bestBidPrice === null ? 'muted' : 'good'
    },
    {
      label: 'Best ask',
      value: formatProbability(orderBook.bestAskPrice),
      delta: formatBookSizeDelta(orderBook.bestAskSize),
      tone: orderBook.bestAskPrice === null ? 'muted' : 'watch'
    },
    {
      label: 'Spread',
      value: orderBook.spread === null ? '-' : orderBook.spread.toFixed(3),
      delta: orderBook.source,
      tone: orderBook.spread === null ? 'muted' : orderBook.spread <= 0.03 ? 'good' : 'watch'
    },
    {
      label: 'Depth imbalance',
      value: `${orderBook.imbalancePct.toFixed(1)}%`,
      delta: 'bid minus ask',
      tone: Math.abs(orderBook.imbalancePct) >= 80 ? 'watch' : 'neutral'
    },
    {
      label: 'Book freshness',
      value: orderBook.freshness.status,
      delta: orderBook.freshness.message,
      tone: resolveMetricToneFromFreshness(orderBook.freshness.status)
    },
    {
      label: 'Signal score',
      value: formatPercent(market.signalScore),
      delta: `${formatCurrency(market.volume24h)} 24h volume`,
      tone: market.signalScore >= 0.7 ? 'good' : market.signalScore >= 0.45 ? 'neutral' : 'watch'
    }
  ]
}

function formatBookSizeDelta(size: number | null | undefined) {
  return size === null || size === undefined ? '-' : `${size.toFixed(2)} shares`
}

function resolveMetricToneFromFreshness(status: string): ControlRoomMetric['tone'] {
  switch (status.toLowerCase()) {
    case 'fresh':
      return 'good'
    case 'delayed':
      return 'watch'
    case 'stale':
      return 'watch'
    default:
      return 'muted'
  }
}

function resolveMarketSortLabel(label: string, intl: IntlShape) {
  const key = label.toLowerCase()
  const map: Record<string, string> = {
    rank: 'filter.sort.rank',
    volume: 'filter.sort.volume',
    signal: 'filter.sort.signal',
    liquidity: 'filter.sort.liquidity',
    expiry: 'filter.sort.expiry'
  }

  const id = map[key]
  return id ? intl.formatMessage({ id }) : label
}

function resolveOrderBookFreshness(orderBook: ControlRoomOrderBook | null, intl: IntlShape) {
  if (!orderBook) {
    return {
      tone: 'empty',
      label: intl.formatMessage({ id: 'book.freshness.empty.label' }),
      message: intl.formatMessage({ id: 'book.freshness.empty.message' })
    }
  }

  const status = orderBook.freshness?.status ?? 'Unknown'
  const ageSeconds = orderBook.freshness?.ageSeconds ?? 0

  if (status.toLowerCase() === 'fresh') {
    return {
      tone: 'good',
      label: intl.formatMessage({ id: 'book.freshness.fresh.label' }),
      message: intl.formatMessage({ id: 'book.freshness.fresh.message' }, { ageSeconds })
    }
  }

  if (status.toLowerCase() === 'delayed') {
    return {
      tone: 'watch',
      label: intl.formatMessage({ id: 'book.freshness.delayed.label' }),
      message: intl.formatMessage({ id: 'book.freshness.delayed.message' }, { ageSeconds })
    }
  }

  if (status.toLowerCase() === 'stale') {
    return {
      tone: 'danger',
      label: intl.formatMessage({ id: 'book.freshness.stale.label' }),
      message: intl.formatMessage({ id: 'book.freshness.stale.message' }, { ageSeconds })
    }
  }

  return {
    tone: 'watch',
    label: status,
    message: orderBook.freshness?.message ?? intl.formatMessage(
      { id: 'book.lastUpdate' },
      { staleSeconds: orderBook.freshness?.staleSeconds ?? 0, updatedAt: formatTime(orderBook.lastUpdatedUtc) }
    )
  }
}

function translateMarketStatus(label: string, formatMessage: (descriptor: { id: string }) => string) {
  const key = label.toLowerCase()
  const map: Record<string, string> = {
    accepting: 'market.accepting',
    active: 'filter.active',
    closed: 'market.closed',
    paused: 'market.paused'
  }

  const id = map[key]
  return id ? formatMessage({ id }) : label
}

function buildApiHref(path: string) {
  return `${apiBaseUrl}${path.startsWith('/') ? path : `/${path}`}`
}

function buildUnhedgedExposureCsvApi(riskEventId: string) {
  const params = new URLSearchParams({ limit: '20' })
  const normalizedRiskEventId = riskEventId.trim()
  if (normalizedRiskEventId) {
    params.set('riskEventId', normalizedRiskEventId)
  }

  return `/api/control-room/risk/unhedged-exposures.csv?${params.toString()}`
}

function resolveIncidentActionDisabledReason(
  action: IncidentActionDescriptor,
  selectedStrategyId: string | null,
  cancelScope: IncidentCancelScope,
  busyCommand: string | null
) {
  const pendingKey = resolveIncidentActionBusyKey(action, selectedStrategyId)
  if (pendingKey && busyCommand === pendingKey) {
    return 'Command is pending'
  }

  if ((action.id === 'pause-strategy' || action.id === 'stop-strategy') && !selectedStrategyId) {
    return 'Select a strategy before running this action.'
  }

  if (action.id === 'cancel-open-orders' && cancelScope === 'strategy' && !selectedStrategyId) {
    return 'Select a strategy before cancelling scoped open orders.'
  }

  if (action.disabledReason) {
    return action.disabledReason
  }

  return action.enabled ? null : 'Action is unavailable.'
}

function isIncidentActionPending(
  action: IncidentActionDescriptor,
  selectedStrategyId: string | null,
  busyCommand: string | null
) {
  const pendingKey = resolveIncidentActionBusyKey(action, selectedStrategyId)
  return Boolean(pendingKey && busyCommand === pendingKey)
}

function resolveIncidentActionBusyKey(action: IncidentActionDescriptor, selectedStrategyId: string | null) {
  switch (action.id) {
    case 'hard-stop':
      return 'kill-switch:on'
    case 'reset-kill-switch':
      return 'kill-switch:off'
    case 'pause-strategy':
      return selectedStrategyId ? `${selectedStrategyId}:Paused` : null
    case 'stop-strategy':
      return selectedStrategyId ? `${selectedStrategyId}:Stopped` : null
    case 'cancel-open-orders':
      return 'incident:cancel-open-orders'
    default:
      return null
  }
}

function createIncidentActionHandler(
  action: IncidentActionDescriptor,
  selectedStrategyId: string | null,
  onKillSwitch: (active: boolean) => Promise<void>,
  onSetState: (strategyId: string, targetState: TargetStrategyState) => Promise<void>,
  onCancelOpenOrders: () => Promise<void>
): (() => Promise<void>) | null {
  switch (action.id) {
    case 'hard-stop':
      return () => onKillSwitch(true)
    case 'reset-kill-switch':
      return () => onKillSwitch(false)
    case 'pause-strategy':
      return selectedStrategyId ? () => onSetState(selectedStrategyId, 'Paused') : null
    case 'stop-strategy':
      return selectedStrategyId ? () => onSetState(selectedStrategyId, 'Stopped') : null
    case 'cancel-open-orders':
      return onCancelOpenOrders
    default:
      return null
  }
}

function getIncidentActionIcon(actionId: string): ReactNode {
  switch (actionId) {
    case 'hard-stop':
      return <OctagonX size={18} aria-hidden="true" />
    case 'reset-kill-switch':
      return <ShieldCheck size={18} aria-hidden="true" />
    case 'pause-strategy':
      return <CirclePause size={18} aria-hidden="true" />
    case 'stop-strategy':
      return <Square size={18} aria-hidden="true" />
    case 'cancel-open-orders':
      return <DatabaseZap size={18} aria-hidden="true" />
    case 'export-incident-package':
      return <FileText size={18} aria-hidden="true" />
    default:
      return <ClipboardCheck size={18} aria-hidden="true" />
  }
}

function formatNullableText(value: string | null | undefined) {
  const text = value?.trim()
  return text ? text : '-'
}

function compactNullableId(value: string | null | undefined) {
  return value ? compactId(value) : '-'
}

function formatRiskValue(value: number | null | undefined, unit: string | null | undefined) {
  if (value === null || value === undefined) {
    return '-'
  }

  const normalizedUnit = unit?.trim()
  if (!normalizedUnit || normalizedUnit === '$' || normalizedUnit.toUpperCase() === 'USD') {
    return formatCurrency(value)
  }

  if (normalizedUnit === '%') {
    return `${value.toFixed(1)}%`
  }

  if (normalizedUnit.toLowerCase() === 'orders') {
    return value.toFixed(0)
  }

  return `${formatCurrency(value)} ${normalizedUnit}`
}

function formatCurrency(value: number) {
  return value.toLocaleString('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  })
}

function formatCompact(value: number) {
  return Intl.NumberFormat('en-US', {
    notation: 'compact',
    maximumFractionDigits: 1
  }).format(value)
}

function formatLimit(value: number, unit: string) {
  if (unit === '%') {
    return `${value.toFixed(1)}%`
  }

  if (unit === 'orders') {
    return value.toFixed(0)
  }

  return formatCurrency(value)
}

function formatProbability(value: number | null | undefined) {
  return value === null || value === undefined ? '-' : value.toFixed(3)
}

function formatSignedCurrency(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return '-'
  }

  return `${value >= 0 ? '+' : ''}${value.toFixed(2)}`
}

function formatSignedPercent(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return '-'
  }

  return `${value >= 0 ? '+' : ''}${value.toFixed(1)}%`
}

function formatRateValue(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return '-'
  }

  return `${(value * 100).toFixed(1)}%`
}

function formatBps(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return '-'
  }

  return `${value >= 0 ? '+' : ''}${value.toFixed(0)} bps`
}

function formatDurationMs(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return '-'
  }

  if (value < 1000) {
    return `${value.toFixed(0)} ms`
  }

  return `${(value / 1000).toFixed(1)} s`
}

function formatDurationSeconds(value: number | null | undefined) {
  if (value === null || value === undefined) {
    return '-'
  }

  if (value < 60) {
    return `${value.toFixed(0)} s`
  }

  return `${(value / 60).toFixed(1)} m`
}

function compactId(value: string) {
  return value.length <= 12 ? value : `${value.slice(0, 8)}...${value.slice(-4)}`
}

function pnlClass(value: number | null | undefined) {
  if (value === null || value === undefined || value === 0) {
    return ''
  }

  return value > 0 ? 'positive' : 'negative'
}

function formatPercent(value: number) {
  return `${(value * 100).toFixed(0)}%`
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  }).format(new Date(value))
}

function formatNullableDate(value: string | null | undefined) {
  return value ? formatDate(value) : '-'
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  }).format(new Date(value))
}
