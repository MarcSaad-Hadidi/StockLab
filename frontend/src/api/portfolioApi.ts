import { getAuthorizationHeader } from '../auth/authStorage'

export type PortfolioApiPosition = {
  symbol: string
  quantity: number
  averageCost: number
}

export type PortfolioApiResponse = {
  cashBalance: number
  investedValue: number
  totalValue: number
  currency: string
  positions: PortfolioApiPosition[]
  initialCapital: number
}

export type PortfolioApiTransaction = {
  id: string
  side: 'BUY' | 'SELL'
  symbol: string
  quantity: number
  executionPrice: number
  totalAmount: number
  executedAtUtc: string
}

export type TransactionHistoryQuery = {
  page: number
  pageSize: number
  search?: string
  side?: 'BUY' | 'SELL'
  from?: string
  to?: string
}

export type TransactionHistoryResponse = {
  items: PortfolioApiTransaction[]
  page: number
  pageSize: number
  totalCount: number
  currency: string
  summary: { totalTrades: number; totalInvested: number; totalProceeds: number }
}

export type PortfolioApiErrorCode =
  | 'unauthorized'
  | 'portfolio_not_found'
  | 'offline'
  | 'invalid_response'
  | 'server_error'

export class PortfolioApiError extends Error {
  readonly status: number
  readonly code: PortfolioApiErrorCode

  constructor(status: number, code: PortfolioApiErrorCode, message: string) {
    super(message)
    this.name = 'PortfolioApiError'
    this.status = status
    this.code = code
  }
}

const record = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null

const finiteNumber = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value)

const nonEmptyString = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0

function validPosition(value: unknown): value is PortfolioApiPosition {
  return record(value)
    && nonEmptyString(value.symbol)
    && finiteNumber(value.quantity) && value.quantity > 0
    && finiteNumber(value.averageCost) && value.averageCost > 0
}

function validResponse(value: unknown): value is PortfolioApiResponse {
  if (!record(value)
    || !finiteNumber(value.cashBalance) || value.cashBalance < 0
    || !finiteNumber(value.investedValue) || value.investedValue < 0
    || !finiteNumber(value.totalValue) || value.totalValue < 0
    || !nonEmptyString(value.currency) || !/^[A-Z]{3}$/.test(value.currency)
    || !Array.isArray(value.positions)
    || !value.positions.every(validPosition)) {
    return false
  }

  return finiteNumber(value.initialCapital) && value.initialCapital > 0
}

function validTransaction(value: unknown): value is PortfolioApiTransaction {
  return record(value)
    && nonEmptyString(value.id)
    && (value.side === 'BUY' || value.side === 'SELL')
    && nonEmptyString(value.symbol)
    && finiteNumber(value.quantity) && value.quantity > 0
    && finiteNumber(value.executionPrice) && value.executionPrice > 0
    && finiteNumber(value.totalAmount) && value.totalAmount > 0
    && typeof value.executedAtUtc === 'string'
    && Number.isFinite(Date.parse(value.executedAtUtc))
}

function validHistory(value: unknown): value is TransactionHistoryResponse {
  if (!record(value) || !Array.isArray(value.items) || !value.items.every(validTransaction)
    || !finiteNumber(value.page) || !Number.isInteger(value.page) || value.page < 1
    || !finiteNumber(value.pageSize) || !Number.isInteger(value.pageSize) || value.pageSize < 1 || value.pageSize > 50
    || !finiteNumber(value.totalCount) || !Number.isInteger(value.totalCount) || value.totalCount < 0
    || !nonEmptyString(value.currency) || !/^[A-Z]{3}$/.test(value.currency)
    || !record(value.summary)) return false
  const summary = value.summary
  return value.items.length <= value.pageSize
    && value.items.length <= value.totalCount
    && value.page <= Math.max(1, Math.ceil(value.totalCount / value.pageSize))
    && finiteNumber(summary.totalTrades) && Number.isInteger(summary.totalTrades) && summary.totalTrades >= value.totalCount
    && finiteNumber(summary.totalInvested) && summary.totalInvested >= 0
    && finiteNumber(summary.totalProceeds) && summary.totalProceeds >= 0
}

function codeFor(status: number, value: unknown): PortfolioApiErrorCode {
  if (value === 'portfolio_not_found') return 'portfolio_not_found'
  if (status === 401) return 'unauthorized'
  if (status === 404) return 'portfolio_not_found'
  if (status >= 500) return 'server_error'
  return 'server_error'
}

function messageFor(code: PortfolioApiErrorCode): string {
  return ({
    unauthorized: 'Your session has expired. Please sign in again.',
    portfolio_not_found: 'Your paper portfolio could not be found.',
    offline: 'StockLab is unreachable. Check that the backend is running.',
    invalid_response: 'The portfolio service returned an invalid response.',
    server_error: 'The portfolio service is temporarily unavailable.',
  } satisfies Record<PortfolioApiErrorCode, string>)[code]
}

export function createPortfolioApi(
  baseUrl: string,
  fetcher: typeof fetch = fetch,
  authorization: () => { Authorization: string } | null = getAuthorizationHeader,
) {
  async function getJson(path: string, signal?: AbortSignal, expectedAuthorization?: string): Promise<unknown> {
    const authHeader = authorization()
    if (!authHeader)
      throw new PortfolioApiError(401, 'unauthorized', messageFor('unauthorized'))
    if (expectedAuthorization !== undefined && authHeader.Authorization !== expectedAuthorization)
      throw new PortfolioApiError(401, 'unauthorized', messageFor('unauthorized'))

    let response: Response
    try {
      response = await fetcher(`${baseUrl.replace(/\/$/, '')}${path}`, {
        headers: { Accept: 'application/json', ...authHeader },
        signal,
      })
    } catch {
      if (signal?.aborted) throw new DOMException('The request was aborted.', 'AbortError')
      throw new PortfolioApiError(0, 'offline', messageFor('offline'))
    }

    let body: unknown = null
    try {
      body = await response.json()
    } catch {
      if (response.ok)
        throw new PortfolioApiError(502, 'invalid_response', messageFor('invalid_response'))
    }
    if (!response.ok) {
      const details = record(body) ? body : {}
      const code = codeFor(response.status, details.error)
      throw new PortfolioApiError(response.status, code, messageFor(code))
    }
    return body
  }

  return {
    async getTransactionHistory(query: TransactionHistoryQuery, signal?: AbortSignal, expectedAuthorization?: string): Promise<TransactionHistoryResponse> {
      const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
      for (const key of ['search', 'side', 'from', 'to'] as const) {
        const value = query[key]?.trim()
        if (value) params.set(key, value)
      }
      const body = await getJson(`/api/portfolio/transactions/history?${params}`, signal, expectedAuthorization)
      if (!validHistory(body))
        throw new PortfolioApiError(502, 'invalid_response', messageFor('invalid_response'))
      return body
    },
    async getPortfolio(signal?: AbortSignal, expectedAuthorization?: string): Promise<PortfolioApiResponse> {
      const body = await getJson('/api/portfolio', signal, expectedAuthorization)
      if (!validResponse(body))
        throw new PortfolioApiError(502, 'invalid_response', messageFor('invalid_response'))

      return body
    },
    async getRecentTransactions(limit = 5, signal?: AbortSignal, expectedAuthorization?: string): Promise<PortfolioApiTransaction[]> {
      const body = await getJson(`/api/portfolio/transactions?limit=${encodeURIComponent(String(limit))}`, signal, expectedAuthorization)
      if (!Array.isArray(body) || !body.every(validTransaction))
        throw new PortfolioApiError(502, 'invalid_response', messageFor('invalid_response'))
      return body
    },
  }
}

const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const portfolioApi = createPortfolioApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
