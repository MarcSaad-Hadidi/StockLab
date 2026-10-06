import { getAuthorizationHeader } from '../auth/authStorage'
import { isPositiveAmount } from './decimalAmount'

export type TradeSide = 'BUY' | 'SELL'
export type TradeOrderType = 'market' | 'limit'

export type ExecuteTradeRequest = {
  orderId: string
  side: TradeSide
  symbol: string
  quantity: number
  orderType: TradeOrderType
  limitPrice?: number
}

export type PaperTradeResponse = {
  transactionId: string
  orderId: string
  side: TradeSide
  symbol: string
  quantity: number
  executionPrice: number
  totalAmount: string
  cashBalance: number
  holdingQuantity: number
  averageCost: number | null
  executedAtUtc: string
}

export type TradingApiErrorCode =
  | 'validation_error'
  | 'unauthorized'
  | 'session_changed'
  | 'portfolio_not_found'
  | 'stock_not_found'
  | 'currency_mismatch'
  | 'insufficient_cash'
  | 'insufficient_holdings'
  | 'duplicate_order'
  | 'concurrency_conflict'
  | 'invalid_order'
  | 'limit_not_reached'
  | 'rate_limited'
  | 'offline'
  | 'invalid_response'
  | 'server_error'

const knownCodes = new Set<TradingApiErrorCode>([
  'validation_error', 'unauthorized', 'portfolio_not_found', 'stock_not_found', 'currency_mismatch',
  'insufficient_cash', 'insufficient_holdings', 'duplicate_order',
  'concurrency_conflict', 'invalid_order', 'limit_not_reached', 'rate_limited', 'offline',
  'invalid_response', 'server_error',
])

export class TradingApiError extends Error {
  readonly status: number
  readonly code: TradingApiErrorCode

  constructor(status: number, code: TradingApiErrorCode, message: string) {
    super(message)
    this.name = 'TradingApiError'
    this.status = status
    this.code = code
  }
}

const record = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null

const string = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0

const number = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value)

function validResponse(value: unknown): value is PaperTradeResponse {
  return record(value)
    && string(value.transactionId)
    && string(value.orderId)
    && (value.side === 'BUY' || value.side === 'SELL')
    && string(value.symbol)
    && number(value.quantity) && value.quantity > 0
    && number(value.executionPrice) && value.executionPrice > 0
    && isPositiveAmount(value.totalAmount)
    && number(value.cashBalance) && value.cashBalance >= 0
    && number(value.holdingQuantity) && value.holdingQuantity >= 0
    && (value.averageCost === null || (number(value.averageCost) && value.averageCost > 0))
    && string(value.executedAtUtc)
    && Number.isFinite(Date.parse(value.executedAtUtc))
}

function codeFor(status: number, value: unknown): TradingApiErrorCode {
  if (typeof value === 'string' && knownCodes.has(value as TradingApiErrorCode))
    return value as TradingApiErrorCode
  if (status === 401) return 'unauthorized'
  if (status === 404) return 'portfolio_not_found'
  if (status === 409) return 'concurrency_conflict'
  if (status === 422) return 'invalid_order'
  if (status === 429) return 'rate_limited'
  if (status === 400) return 'validation_error'
  if (status >= 500) return 'server_error'
  return 'server_error'
}

function messageFor(code: TradingApiErrorCode): string {
  return ({
    validation_error: 'The order information is invalid.',
    unauthorized: 'Your session has expired. Please sign in again.',
    session_changed: 'Your sign-in session changed. Please prepare your order again.',
    portfolio_not_found: 'Your paper portfolio could not be found.',
    stock_not_found: 'This stock could not be found.',
    currency_mismatch: 'This stock is quoted in a different currency than your portfolio.',
    insufficient_cash: 'There is not enough available cash for this order.',
    insufficient_holdings: 'You do not hold enough shares for this order.',
    duplicate_order: 'This order has already been submitted with different details.',
    concurrency_conflict: 'The portfolio changed while the order was executing.',
    invalid_order: 'The order could not be executed.',
    limit_not_reached: 'The current market price does not meet your limit price.',
    rate_limited: 'Too many requests. Please try again shortly.',
    offline: 'StockLab is unreachable. Check that the backend is running.',
    invalid_response: 'The trading service returned an invalid response.',
    server_error: 'The trading service is temporarily unavailable.',
  } satisfies Record<TradingApiErrorCode, string>)[code]
}

export function createTradingApi(
  baseUrl: string,
  fetcher: typeof fetch = fetch,
  authorization: () => { Authorization: string } | null = getAuthorizationHeader,
) {
  return {
    async executeTrade(request: ExecuteTradeRequest, signal?: AbortSignal, expectedAuthorization?: string,
      onRequestSent?: () => void): Promise<PaperTradeResponse> {
      const authHeader = authorization()
      if (expectedAuthorization !== undefined && authHeader?.Authorization !== expectedAuthorization)
        throw new TradingApiError(401, 'session_changed', messageFor('session_changed'))
      if (!authHeader)
        throw new TradingApiError(401, 'unauthorized', messageFor('unauthorized'))

      let response: Response
      try {
        const body = JSON.stringify({
          orderId: request.orderId,
          side: request.side,
          symbol: request.symbol.trim().toUpperCase(),
          quantity: request.quantity,
          orderType: request.orderType,
          ...(request.orderType === 'limit' ? { limitPrice: request.limitPrice } : {}),
        })
        onRequestSent?.()
        response = await fetcher(`${baseUrl.replace(/\/$/, '')}/api/portfolio/trades`, {
          method: 'POST',
          headers: { Accept: 'application/json', 'Content-Type': 'application/json', ...authHeader },
          body,
          signal,
        })
      } catch {
        throw new TradingApiError(0, 'offline', messageFor('offline'))
      }

      let body: unknown = null
      try {
        body = await response.json()
      } catch {
        if (response.ok)
          throw new TradingApiError(502, 'invalid_response', messageFor('invalid_response'))
      }

      if (!response.ok) {
        const details = record(body) ? body : {}
        const code = codeFor(response.status, details.error)
        throw new TradingApiError(response.status, code, messageFor(code))
      }
      if (!validResponse(body))
        throw new TradingApiError(502, 'invalid_response', messageFor('invalid_response'))
      return body
    },
  }
}

const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const tradingApi = createTradingApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
