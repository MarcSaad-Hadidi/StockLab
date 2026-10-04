import { getAuthorizationHeader } from '../auth/authStorage'

export type WatchlistApiItem = { symbol: string; createdAtUtc: string }
export type WatchlistErrorCode = 'unauthorized' | 'already_exists' | 'not_found' | 'offline' | 'invalid_response' | 'server_error' | 'invalid_symbol'

export class WatchlistApiError extends Error {
  readonly status: number
  readonly code: WatchlistErrorCode
  constructor(status: number, code: WatchlistErrorCode) {
    super(`Watchlist request failed: ${code}`)
    this.name = 'WatchlistApiError'
    this.status = status
    this.code = code
  }
}

function validItem(value: unknown): value is WatchlistApiItem {
  if (typeof value !== 'object' || value === null) return false
  const item = value as Record<string, unknown>
  return typeof item.symbol === 'string' && item.symbol.trim().length > 0
    && typeof item.createdAtUtc === 'string' && Number.isFinite(Date.parse(item.createdAtUtc))
}

function codeFor(status: number): WatchlistErrorCode {
  if (status === 401) return 'unauthorized'
  if (status === 404) return 'not_found'
  if (status === 409) return 'already_exists'
  if (status === 400) return 'invalid_symbol'
  return 'server_error'
}

export function createWatchlistApi(
  baseUrl: string,
  fetcher: typeof fetch = fetch,
  authorization = getAuthorizationHeader,
) {
  async function request(method: 'GET' | 'POST' | 'DELETE', path: string, status: number, signal?: AbortSignal, symbol?: string): Promise<unknown> {
    signal?.throwIfAborted()
    const auth = authorization()
    if (!auth) throw new WatchlistApiError(401, 'unauthorized')
    let response: Response
    try {
      response = await fetcher(`${baseUrl.replace(/\/$/, '')}${path}`, {
        method,
        headers: { Accept: 'application/json', ...auth, ...(method === 'POST' ? { 'Content-Type': 'application/json' } : {}) },
        ...(method === 'POST' ? { body: JSON.stringify({ symbol }) } : {}),
        signal,
      })
    } catch {
      signal?.throwIfAborted()
      throw new WatchlistApiError(0, 'offline')
    }
    signal?.throwIfAborted()
    if (!response.ok) throw new WatchlistApiError(response.status, codeFor(response.status))
    if (response.status !== status) throw new WatchlistApiError(502, 'invalid_response')
    if (status === 204) return undefined
    try {
      const body: unknown = await response.json()
      signal?.throwIfAborted()
      return body
    } catch {
      signal?.throwIfAborted()
      throw new WatchlistApiError(502, 'invalid_response')
    }
  }

  return {
    async getWatchlist(signal?: AbortSignal): Promise<WatchlistApiItem[]> {
      const body = await request('GET', '/api/watchlist', 200, signal)
      if (!Array.isArray(body) || !body.every(validItem)) throw new WatchlistApiError(502, 'invalid_response')
      return body
    },
    async addToWatchlist(symbol: string, signal?: AbortSignal): Promise<WatchlistApiItem> {
      const body = await request('POST', '/api/watchlist', 201, signal, symbol.trim().toUpperCase())
      if (!validItem(body)) throw new WatchlistApiError(502, 'invalid_response')
      return body
    },
    async removeFromWatchlist(symbol: string, signal?: AbortSignal): Promise<void> {
      await request('DELETE', `/api/watchlist/${encodeURIComponent(symbol.trim().toUpperCase())}`, 204, signal)
    },
  }
}

const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const watchlistApi = createWatchlistApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
