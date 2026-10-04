import { getAuthorizationHeader } from '../auth/authStorage'

export type ChangePasswordRequest = { currentPassword: string; newPassword: string; confirmPassword: string }
export type PasswordErrorCode = 'unauthorized' | 'invalid_current_password' | 'validation_error'
  | 'user_not_found' | 'password_update_conflict' | 'rate_limited' | 'offline' | 'server_error' | 'invalid_response' | 'session_changed'
export class PasswordApiError extends Error {
  readonly code: PasswordErrorCode
  readonly fields: string[]
  constructor(code: PasswordErrorCode, fields: string[] = []) {
    super(code)
    this.code = code
    this.fields = fields
    this.name = 'PasswordApiError'
  }
}
export function createPasswordApi(baseUrl: string, fetcher: typeof fetch = (...args) => fetch(...args)) {
  return { async changePassword(payload: ChangePasswordRequest, signal?: AbortSignal): Promise<void> {
    const auth = getAuthorizationHeader()
    if (!auth) throw new PasswordApiError('unauthorized')
    let response: Response
    try {
      response = await fetcher(`${baseUrl.replace(/\/$/, '')}/api/auth/password`, {
        method: 'PUT', signal, headers: { ...auth, 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(payload),
      })
    } catch {
      if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
      throw new PasswordApiError('offline')
    }
    if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
    if (response.status === 204) return
    if (response.ok) throw new PasswordApiError('invalid_response')
    let details: { error?: string; errors?: Record<string, unknown> } = {}
    try { details = await response.json() ?? {} } catch { /* Use a safe status-based error. */ }
    const code: PasswordErrorCode = response.status === 401 ? 'unauthorized'
      : response.status === 400 && details.error === 'invalid_current_password' ? 'invalid_current_password'
      : response.status === 400 ? 'validation_error' : response.status === 404 ? 'user_not_found'
      : response.status === 409 ? 'password_update_conflict' : response.status === 429 ? 'rate_limited' : 'server_error'
    const fields = details.errors && typeof details.errors === 'object' ? Object.keys(details.errors).map(key => key.toLowerCase()) : []
    throw new PasswordApiError(code, code === 'invalid_current_password' ? ['currentpassword'] : fields)
  } }
}
const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const passwordApi = createPasswordApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
