import { retryDelay, validLoginResponse } from './authApi'
import type { AuthSession } from '../auth/authStorage'
import type { Language } from '../i18n/languages'

export type GoogleConfig = { enabled: false } | { enabled: true; clientId: string }
export type GoogleLinkTicket = { linkToken: string; expiresAtUtc: string }
export type GoogleAuthErrorCode = 'google_link_required' | 'google_link_expired' | 'google_account_conflict'
  | 'google_invalid_response' | 'google_unavailable'
  | 'invalid_credentials' | 'validation_error' | 'rate_limited' | 'offline' | 'invalid_response' | 'server_error'

export class GoogleAuthApiError extends Error {
  readonly status: number
  readonly code: GoogleAuthErrorCode
  readonly linkTicket?: GoogleLinkTicket
  readonly retryAfterSeconds: number | null
  constructor(status: number, code: GoogleAuthErrorCode, linkTicket?: GoogleLinkTicket, retryAfterSeconds: number | null = null) {
    super('Google authentication could not be completed.')
    this.name = 'GoogleAuthApiError'
    this.status = status
    this.code = code
    this.linkTicket = linkTicket
    this.retryAfterSeconds = retryAfterSeconds
  }
}

const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const nonempty = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
function validConfig(value: unknown): value is GoogleConfig {
  return record(value) && (value.enabled === false || value.enabled === true && nonempty(value.clientId))
}
function validTicket(value: Record<string, unknown>): value is Record<string, unknown> & GoogleLinkTicket {
  return nonempty(value.linkToken) && nonempty(value.expiresAtUtc) && Number.isFinite(Date.parse(value.expiresAtUtc))
}
function errorCode(status: number, error: unknown): GoogleAuthErrorCode {
  if (status === 429) return 'rate_limited'
  switch (error) {
    case 'google_link_expired': case 'google_account_conflict': case 'google_invalid_response':
    case 'google_unavailable': case 'invalid_credentials':
    case 'validation_error': return error
    default: return status >= 500 ? 'server_error' : status === 400 ? 'validation_error' : 'invalid_response'
  }
}

export function createGoogleAuthApi(baseUrl: string, fetcher: typeof fetch = fetch) {
  async function request<T>(path: string, payload: unknown, valid: (value: unknown) => value is T, signal?: AbortSignal): Promise<T> {
    let response: Response
    try {
      response = await fetcher(`${baseUrl.replace(/\/$/, '')}/api/auth/google/${path}`, {
        method: payload === undefined ? 'GET' : 'POST',
        headers: payload === undefined ? { Accept: 'application/json' }
          : { Accept: 'application/json', 'Content-Type': 'application/json', 'X-Requested-With': 'StockLab.GoogleOAuth' },
        ...(payload === undefined ? {} : { body: JSON.stringify(payload) }),
        signal,
      })
    } catch {
      throw new GoogleAuthApiError(0, 'offline')
    }
    let body: unknown
    try { body = await response.json() } catch { body = null }
    if (!response.ok) {
      const details = record(body) ? body : {}
      if (response.status === 409 && details.error === 'google_link_required') {
        if (!validTicket(details)) throw new GoogleAuthApiError(502, 'invalid_response')
        throw new GoogleAuthApiError(409, 'google_link_required', { linkToken: details.linkToken, expiresAtUtc: details.expiresAtUtc })
      }
      throw new GoogleAuthApiError(response.status, errorCode(response.status, details.error), undefined,
        response.status === 429 ? retryDelay(response, details) : null)
    }
    if (response.status !== 200 || !valid(body)) throw new GoogleAuthApiError(502, 'invalid_response')
    return body
  }
  return {
    getConfig(origin: string, signal?: AbortSignal) {
      return request(`config?origin=${encodeURIComponent(origin)}`, undefined, validConfig, signal)
    },
    exchange(payload: { code: string; language: Language }, signal?: AbortSignal): Promise<AuthSession> {
      return request('exchange', payload, validLoginResponse, signal)
    },
    link(payload: { linkToken: string; password: string; language: Language }, signal?: AbortSignal): Promise<AuthSession> {
      return request('link', payload, validLoginResponse, signal)
    },
  }
}

const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const googleAuthApi = createGoogleAuthApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
