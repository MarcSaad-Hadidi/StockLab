import { getAuthorizationHeader } from '../auth/authStorage'
import { handleUnauthorized } from '../auth/handleUnauthorized'
import { isBackendAmount, isPositiveAmount } from './decimalAmount'

export type AiTraderSignal = 'BUY' | 'SELL' | 'HOLD'
export type AiTraderCoverageResponse = {
  valuationSampleCount: number; valuationFirstAtUtc: string | null; valuationLastAtUtc: string | null
  status: 'Absent' | 'Insufficient' | 'SampledSinceInitialization' | 'PartialHistory'
}
export type AiTraderOverviewResponse = {
  portfolioId: string; currency: string; initialCapital: string; cashBalance: string
  positionsMarketValue: string; totalValue: string; totalPnL: string; totalReturn: number
  winRate: number | null; maxDrawdown: number | null; executedTradeCount: number
  openPositionsCount: number; valuationTimestampUtc: string; historicalCoverage: AiTraderCoverageResponse
}
export type AiTraderPositionResponse = {
  symbol: string; quantity: string; averagePrice: string; currentPrice: string; marketValue: string; pnL: string
}
export type AiTraderTradeDecisionResponse = {
  decisionId: string; signal: AiTraderSignal; confidence: number; decisionDate: string; modelName: string; modelVersion: string
}
export type AiTraderTradeResponse = {
  tradeId: string; orderId: string; decisionId: string | null; side: 'BUY' | 'SELL'; symbol: string
  quantity: string; executionPrice: string; totalAmount: string; executedAtUtc: string
  realizedPnL: string | null; decision: AiTraderTradeDecisionResponse | null
}
export type AiTraderDecisionResponse = AiTraderTradeDecisionResponse & {
  symbol: string; recordedAtUtc: string; executionStatus: 'EXECUTED' | 'REJECTED' | 'HOLD' | 'UNRESOLVED'
  rejectionReason: string | null; tradeId: string | null
}
export type AiTraderRejectedDecisionResponse = AiTraderTradeDecisionResponse & {
  symbol: string; rejectionReason: string; rejectedAtUtc: string
}
export type AiTraderRealizedTradeResponse = {
  tradeId: string; symbol: string; quantity: string; executionPrice: string; executedAtUtc: string; realizedPnL: string
}
export type AiTraderPerformanceResponse = AiTraderOverviewResponse & {
  realizedPnL: string | null; unrealizedPnL: string; averageWin: string | null; averageLoss: string | null
  bestTrade: AiTraderRealizedTradeResponse | null; worstTrade: AiTraderRealizedTradeResponse | null
  buyTradeCount: number; sellTradeCount: number; winningSellCount: number; losingSellCount: number
  breakEvenSellCount: number; tradeStatisticsComplete: boolean; unvaluedLegacySellCount: number
}
export type AiTraderEquityResponse = {
  valuedAtUtc: string; cashBalance: string; positionsValue: string; totalValue: string; pnL: string
  return: number; currency: string; isInitialBaseline: boolean
}
export type AiTraderModelPeriodResponse = { startDate: string; endDate: string }
export type AiTraderModelResponse = {
  modelName: string; modelVersion: string; modelType: string; trainingRunId: string; trainedAtUtc: string
  status: 'Active' | 'Archived'; accuracy: number; precision: number; recall: number; f1: number
  trainingPeriod: AiTraderModelPeriodResponse; evaluationPeriod: AiTraderModelPeriodResponse
}
export type AiTraderBacktestResponse = {
  modelName: string; modelVersion: string | null; startDate: string; endDate: string; initialCapital: string
  finalValue: string; totalReturn: number; winRate: number | null; maxDrawdown: number; executedTrades: number | null
  benchmarkReturn: number | null; benchmarkMaxDrawdown: number | null; returnDelta: number | null
  evaluationSource: 'backtesting' | 'benchmark_comparison' | 'model_promotion'; sourceTimestampUtc: string | null
}
export type AiTraderApiErrorCode = 'unauthorized' | 'not_found' | 'conflict' | 'rate_limited' | 'upstream_invalid'
  | 'unavailable' | 'timeout' | 'offline' | 'invalid_response' | 'invalid_request' | 'server_error'
export class AiTraderApiError extends Error {
  readonly status: number
  readonly code: AiTraderApiErrorCode
  readonly portfolioNotInitialized: boolean
  constructor(status: number, code: AiTraderApiErrorCode, portfolioNotInitialized = false) {
    super(`AI Trader: ${code}`)
    this.name = 'AiTraderApiError'
    this.status = status
    this.code = code
    this.portfolioNotInitialized = portfolioNotInitialized
  }
}

type Validator<T> = (value: unknown) => value is T
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const text: Validator<string> = (value): value is string => typeof value === 'string' && value.trim().length > 0
const finite: Validator<number> = (value): value is number => typeof value === 'number' && Number.isFinite(value)
const count: Validator<number> = (value): value is number => finite(value) && Number.isSafeInteger(value) && value >= 0
const fraction: Validator<number> = (value): value is number => finite(value) && value >= 0 && value <= 1
const boolean: Validator<boolean> = (value): value is boolean => typeof value === 'boolean'
const guid: Validator<string> = (value): value is string => text(value) && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value)
const currency: Validator<string> = (value): value is string => text(value) && /^[A-Z]{3}$/.test(value)
const nonnegative: Validator<string> = (value): value is string => isBackendAmount(value) && !value.startsWith('-')
const positive: Validator<string> = (value): value is string => nonnegative(value) && /[1-9]/.test(value)
// SQL quantities have eight fractional digits; preserve their original string.
const quantity: Validator<string> = (value): value is string => isPositiveAmount(value) && (value.split('.')[1]?.length ?? 0) <= 8
const nullable = <T>(valid: Validator<T>): Validator<T | null> => (value): value is T | null => value === null || valid(value)
const oneOf = <T extends string>(...values: T[]): Validator<T> => (value): value is T => typeof value === 'string' && values.some(item => item === value)
function shape<T>(fields: { [K in keyof T]: Validator<T[K]> }): Validator<T> {
  return (value): value is T => record(value) && Object.entries(fields).every(([key, valid]) => (valid as Validator<unknown>)(value[key]))
}
const date: Validator<string> = (value): value is string => text(value) && /^\d{4}-\d{2}-\d{2}$/.test(value)
  && value.slice(0, 4) !== '0000' && Number.isFinite(Date.parse(`${value}T00:00:00Z`)) && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value
const utc: Validator<string> = (value): value is string => text(value)
  && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
  && date(value.slice(0, 10)) && Number.isFinite(Date.parse(value))
  && Number(value.slice(11, 13)) < 24 && Number(value.slice(14, 16)) < 60 && Number(value.slice(17, 19)) < 60
const signal = oneOf('BUY', 'SELL', 'HOLD')
const coverage = shape<AiTraderCoverageResponse>({ valuationSampleCount: count, valuationFirstAtUtc: nullable(utc), valuationLastAtUtc: nullable(utc), status: oneOf('Absent', 'Insufficient', 'SampledSinceInitialization', 'PartialHistory') })
const overviewFields = {
  portfolioId: guid, currency, initialCapital: positive, cashBalance: nonnegative, positionsMarketValue: nonnegative,
  totalValue: nonnegative, totalPnL: isBackendAmount, totalReturn: finite, winRate: nullable(fraction), maxDrawdown: nullable(fraction),
  executedTradeCount: count, openPositionsCount: count, valuationTimestampUtc: utc, historicalCoverage: coverage,
}
const overview = shape<AiTraderOverviewResponse>(overviewFields)
const position = shape<AiTraderPositionResponse>({ symbol: text, quantity, averagePrice: positive, currentPrice: positive, marketValue: nonnegative, pnL: isBackendAmount })
const decisionFields = { decisionId: guid, signal, confidence: fraction, decisionDate: date, modelName: text, modelVersion: text }
const tradeDecision = shape<AiTraderTradeDecisionResponse>(decisionFields)
const trade = shape<AiTraderTradeResponse>({ tradeId: guid, orderId: guid, decisionId: nullable(guid), side: oneOf('BUY', 'SELL'), symbol: text, quantity, executionPrice: positive, totalAmount: nonnegative, executedAtUtc: utc, realizedPnL: nullable(isBackendAmount), decision: nullable(tradeDecision) })
const decision = shape<AiTraderDecisionResponse>({ ...decisionFields, symbol: text, recordedAtUtc: utc, executionStatus: oneOf('EXECUTED', 'REJECTED', 'HOLD', 'UNRESOLVED'), rejectionReason: nullable(text), tradeId: nullable(guid) })
const rejection = shape<AiTraderRejectedDecisionResponse>({ ...decisionFields, symbol: text, rejectionReason: text, rejectedAtUtc: utc })
const realizedTrade = shape<AiTraderRealizedTradeResponse>({ tradeId: guid, symbol: text, quantity, executionPrice: positive, executedAtUtc: utc, realizedPnL: isBackendAmount })
const performance = shape<AiTraderPerformanceResponse>({ ...overviewFields, realizedPnL: nullable(isBackendAmount), unrealizedPnL: isBackendAmount, averageWin: nullable(isBackendAmount), averageLoss: nullable(isBackendAmount), bestTrade: nullable(realizedTrade), worstTrade: nullable(realizedTrade), buyTradeCount: count, sellTradeCount: count, winningSellCount: count, losingSellCount: count, breakEvenSellCount: count, tradeStatisticsComplete: boolean, unvaluedLegacySellCount: count })
const equity = shape<AiTraderEquityResponse>({ valuedAtUtc: utc, cashBalance: nonnegative, positionsValue: nonnegative, totalValue: nonnegative, pnL: isBackendAmount, return: finite, currency, isInitialBaseline: boolean })
const periodFields = shape<AiTraderModelPeriodResponse>({ startDate: date, endDate: date })
const period: Validator<AiTraderModelPeriodResponse> = (value): value is AiTraderModelPeriodResponse => periodFields(value) && value.startDate <= value.endDate
const model = shape<AiTraderModelResponse>({ modelName: text, modelVersion: text, modelType: text, trainingRunId: guid, trainedAtUtc: utc, status: oneOf('Active', 'Archived'), accuracy: fraction, precision: fraction, recall: fraction, f1: fraction, trainingPeriod: period, evaluationPeriod: period })
const backtestFields = shape<AiTraderBacktestResponse>({ modelName: text, modelVersion: nullable(text), startDate: date, endDate: date, initialCapital: positive, finalValue: nonnegative, totalReturn: finite, winRate: nullable(fraction), maxDrawdown: fraction, executedTrades: nullable(count), benchmarkReturn: nullable(finite), benchmarkMaxDrawdown: nullable(fraction), returnDelta: nullable(finite), evaluationSource: oneOf('backtesting', 'benchmark_comparison', 'model_promotion'), sourceTimestampUtc: nullable(utc) })
const backtest: Validator<AiTraderBacktestResponse> = (value): value is AiTraderBacktestResponse => backtestFields(value) && value.startDate <= value.endDate
const array = <T>(valid: Validator<T>, limit = Infinity): Validator<T[]> => (value): value is T[] => Array.isArray(value) && value.length <= limit && value.every(valid)

export type AiTraderHistoryQuery = { limit?: number; startDateUtc?: string; endDateUtc?: string }
function limitQuery(limit: number) {
  if (!count(limit) || limit < 1 || limit > 200) throw new AiTraderApiError(400, 'invalid_request')
  return new URLSearchParams({ limit: String(limit) })
}
export function createAiTraderApi(baseUrl: string, fetcher: typeof fetch = fetch,
  authorization: () => { Authorization: string } | null = getAuthorizationHeader) {
  async function get<T>(path: string, valid: Validator<T>, abort?: AbortSignal, expectedAuthorization?: string): Promise<T> {
    const header = authorization()
    if (!header || (expectedAuthorization !== undefined && expectedAuthorization !== header.Authorization)) throw new AiTraderApiError(401, 'unauthorized')
    abort?.throwIfAborted()
    let response: Response
    try {
      response = await fetcher(`${baseUrl.replace(/\/$/, '')}/api/ai-trader/${path}`, { headers: { Accept: 'application/json', ...header }, signal: abort })
    } catch {
      abort?.throwIfAborted()
      throw new AiTraderApiError(0, 'offline')
    }
    handleUnauthorized(response, header.Authorization, abort)
    let body: unknown
    try { body = await response.json() } catch {
      abort?.throwIfAborted()
      if (response.ok) throw new AiTraderApiError(502, 'invalid_response')
    }
    abort?.throwIfAborted()
    // A token rotation during the read must not return old-session data to any caller.
    if (authorization()?.Authorization !== header.Authorization) throw new AiTraderApiError(401, 'unauthorized')
    if (!response.ok) {
      const codes: Record<number, AiTraderApiErrorCode> = { 400: 'invalid_request', 401: 'unauthorized', 404: 'not_found', 409: 'conflict', 429: 'rate_limited', 502: 'upstream_invalid', 503: 'unavailable', 504: 'timeout' }
      throw new AiTraderApiError(response.status, codes[response.status] ?? 'server_error', record(body) && body.error === 'ai_trader_not_initialized')
    }
    if (!valid(body)) throw new AiTraderApiError(502, 'invalid_response')
    return body
  }
  async function detail<T>(path: string, id: string, valid: Validator<T>, abort?: AbortSignal, auth?: string) {
    if (!guid(id)) throw new AiTraderApiError(400, 'invalid_request')
    return get(`${path}/${encodeURIComponent(id)}`, valid, abort, auth)
  }
  return {
    getOverview: (abort?: AbortSignal, auth?: string) => get('overview', overview, abort, auth),
    getPositions: (abort?: AbortSignal, auth?: string) => get('positions', array(position), abort, auth),
    async getTrades(limit = 20, abort?: AbortSignal, auth?: string) { return get(`trades?${limitQuery(limit)}`, array(trade, limit), abort, auth) },
    getTrade: (id: string, abort?: AbortSignal, auth?: string) => detail('trades', id, trade, abort, auth),
    async getDecisions(limit = 20, abort?: AbortSignal, auth?: string) { return get(`decisions?${limitQuery(limit)}`, array(decision, limit), abort, auth) },
    getDecision: (id: string, abort?: AbortSignal, auth?: string) => detail('decisions', id, decision, abort, auth),
    async getRejectedDecisions(limit = 20, abort?: AbortSignal, auth?: string) { return get(`rejected-decisions?${limitQuery(limit)}`, array(rejection, limit), abort, auth) },
    getPerformance: (abort?: AbortSignal, auth?: string) => get('performance', performance, abort, auth),
    async getEquityHistory(query: AiTraderHistoryQuery = {}, abort?: AbortSignal, auth?: string) {
      const params = limitQuery(query.limit ?? 20)
      if ((query.startDateUtc !== undefined && !utc(query.startDateUtc)) || (query.endDateUtc !== undefined && !utc(query.endDateUtc))
        || (query.startDateUtc && query.endDateUtc && Date.parse(query.startDateUtc) > Date.parse(query.endDateUtc))) throw new AiTraderApiError(400, 'invalid_request')
      if (query.startDateUtc) params.set('startDateUtc', query.startDateUtc)
      if (query.endDateUtc) params.set('endDateUtc', query.endDateUtc)
      return get(`performance/history?${params}`, array(equity, query.limit ?? 20), abort, auth)
    },
    getModels: (abort?: AbortSignal, auth?: string) => get('models', array(model), abort, auth),
    getBacktests: (abort?: AbortSignal, auth?: string) => get('backtests', array(backtest), abort, auth),
  }
}
const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const aiTraderApi = createAiTraderApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
export type AiTraderApi = ReturnType<typeof createAiTraderApi>
