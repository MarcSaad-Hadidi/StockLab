import type { Language } from '../i18n/languages'

export type ForgotPasswordRequest = { email: string; language: Language }
export type ResetPasswordRequest = { userId: string; token: string; newPassword: string; confirmPassword: string }
export type PasswordRecoveryErrorCode = 'invalid_reset_token' | 'validation_error' | 'too_many_requests'
  | 'password_recovery_unavailable' | 'password_update_conflict' | 'internal_server_error' | 'offline' | 'invalid_response'

export class PasswordRecoveryApiError extends Error {
  readonly code: PasswordRecoveryErrorCode
  readonly fields: string[]
  readonly retryAfterSeconds: number | null
  constructor(code: PasswordRecoveryErrorCode, fields: string[] = [], retryAfterSeconds: number | null = null) {
    super(code)
    this.name = 'PasswordRecoveryApiError'
    this.code = code
    this.fields = fields
    this.retryAfterSeconds = retryAfterSeconds
  }
}

function retryDelay(response: Response, body: Record<string, unknown>): number | null {
  const valid = (value: unknown): value is number => typeof value === 'number'
    && Number.isSafeInteger(value) && value >= 0 && value <= 86400
  const header = response.headers.get('Retry-After')?.trim()
  if (header && /^\d+$/.test(header) && valid(Number(header))) return Number(header)
  if (header && /^[a-z]{3}, \d{2} [a-z]{3} \d{4} \d{2}:\d{2}:\d{2} GMT$/i.test(header)) {
    const serverDate = Date.parse(response.headers.get('Date') ?? '')
    const seconds = Math.max(0, Math.ceil((Date.parse(header) - (Number.isFinite(serverDate) ? serverDate : Date.now())) / 1000))
    if (valid(seconds)) return seconds
  }
  return valid(body.retryAfterSeconds) ? body.retryAfterSeconds : null
}

export function createPasswordRecoveryApi(baseUrl: string, fetcher: typeof fetch = (...args) => fetch(...args)) {
  async function request(path: string, payload: unknown, expectedStatus: number): Promise<void> {
    let response: Response
    try {
      response = await fetcher(`${baseUrl.replace(/\/$/, '')}/api/auth/password/${path}`, {
        method: 'POST', referrerPolicy: 'no-referrer',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' }, body: JSON.stringify(payload),
      })
    } catch { throw new PasswordRecoveryApiError('offline') }
    if (response.status === expectedStatus) return
    if (response.ok) throw new PasswordRecoveryApiError('invalid_response')
    let body: Record<string, unknown> = {}
    try {
      const value: unknown = await response.json()
      if (typeof value === 'object' && value !== null && !Array.isArray(value)) body = value as Record<string, unknown>
    } catch { /* Never expose server text or transport details. */ }
    const code: PasswordRecoveryErrorCode = response.status === 400 && body.error === 'invalid_reset_token' ? 'invalid_reset_token'
      : response.status === 400 && body.error === 'validation_error' ? 'validation_error'
      : response.status === 429 ? 'too_many_requests' : response.status === 503 ? 'password_recovery_unavailable'
      : response.status === 409 ? 'password_update_conflict' : response.status >= 500 ? 'internal_server_error' : 'invalid_response'
    const fields = code === 'validation_error' && typeof body.errors === 'object' && body.errors !== null && !Array.isArray(body.errors)
      ? Object.keys(body.errors).map(key => key.toLowerCase()).filter(key => ['email', 'newpassword', 'confirmpassword'].includes(key)) : []
    throw new PasswordRecoveryApiError(code, fields, code === 'too_many_requests' ? retryDelay(response, body) : null)
  }
  return {
    forgotPassword(payload: ForgotPasswordRequest) {
      return request('forgot', { ...payload, email: payload.email.trim() }, 202)
    },
    resetPassword(payload: ResetPasswordRequest) { return request('reset', payload, 204) },
  }
}
const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const passwordRecoveryApi = createPasswordRecoveryApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
