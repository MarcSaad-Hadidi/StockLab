import type { AuthSession, AuthUser } from '../auth/authStorage'

export type RegisterRequest = {
  displayName: string
  email: string
  password: string
}

export type LoginRequest = {
  email: string
  password: string
}

export type RegisterResponse = {
  id: string
  displayName: string
  email: string
  createdAtUtc: string
}

export type AuthApiErrorCode =
  | 'validation_error'
  | 'email_already_registered'
  | 'invalid_credentials'
  | 'rate_limited'
  | 'offline'
  | 'invalid_response'
  | 'server_error'

export class AuthApiError extends Error {
  readonly status: number
  readonly code: AuthApiErrorCode
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, code: AuthApiErrorCode, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.name = 'AuthApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
  }
}

const record = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null
const string = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0

function errorCode(status: number, value: unknown): AuthApiErrorCode {
  if (value === 'validation_error') return 'validation_error'
  if (value === 'email_already_registered') return 'email_already_registered'
  if (value === 'invalid_credentials') return 'invalid_credentials'
  if (status === 429) return 'rate_limited'
  if (status >= 500) return 'server_error'
  if (status === 401) return 'invalid_credentials'
  if (status === 400) return 'validation_error'
  return 'server_error'
}

function defaultMessage(code: AuthApiErrorCode): string {
  return ({
    validation_error: 'The submitted information is invalid.',
    email_already_registered: 'An account already exists for this email.',
    invalid_credentials: 'The email or password is invalid.',
    rate_limited: 'Too many attempts. Try again later.',
    offline: 'StockLab is unreachable. Check that the backend is running.',
    invalid_response: 'The authentication response is invalid.',
    server_error: 'The authentication service is temporarily unavailable.',
  } satisfies Record<AuthApiErrorCode, string>)[code]
}

function fieldErrors(value: unknown): Record<string, string[]> {
  if (!record(value)) return {}
  return Object.fromEntries(Object.entries(value).flatMap(([key, messages]) => {
    if (!Array.isArray(messages)) return []
    const safe = messages.filter(string).map(message => message.trim())
    return safe.length > 0 ? [[key, safe]] : []
  }))
}

function validUser(value: unknown): value is AuthUser {
  return record(value) && string(value.id) && string(value.displayName) && string(value.email)
}

function validRegisterResponse(value: unknown): value is RegisterResponse {
  return record(value) && string(value.id) && string(value.displayName) && string(value.email)
    && string(value.createdAtUtc) && Number.isFinite(Date.parse(value.createdAtUtc))
}

function validLoginResponse(value: unknown): value is AuthSession {
  return record(value) && string(value.accessToken) && value.tokenType === 'Bearer'
    && string(value.expiresAtUtc) && Number.isFinite(Date.parse(value.expiresAtUtc))
    && validUser(value.user)
}

export function createAuthApi(baseUrl: string, fetcher: typeof fetch = fetch) {
  async function request<T>(path: string, payload: unknown, valid: (value: unknown) => value is T): Promise<T> {
    let response: Response
    try {
      response = await fetcher(`${baseUrl.replace(/\/$/, '')}${path}`, {
        method: 'POST',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
    } catch {
      throw new AuthApiError(0, 'offline', defaultMessage('offline'))
    }

    let body: unknown = null
    try {
      body = await response.json()
    } catch {
      if (response.ok) throw new AuthApiError(502, 'invalid_response', defaultMessage('invalid_response'))
    }

    if (!response.ok) {
      const details = record(body) ? body : {}
      const code = errorCode(response.status, details.error)
      throw new AuthApiError(
        response.status,
        code,
        defaultMessage(code),
        fieldErrors(details.errors),
      )
    }
    if (!valid(body)) throw new AuthApiError(502, 'invalid_response', defaultMessage('invalid_response'))
    return body
  }

  return {
    register(requestPayload: RegisterRequest) {
      return request('/api/auth/register', {
        displayName: requestPayload.displayName.trim(),
        email: requestPayload.email.trim(),
        password: requestPayload.password,
      }, validRegisterResponse)
    },
    login(requestPayload: LoginRequest) {
      return request('/api/auth/login', {
        email: requestPayload.email.trim(),
        password: requestPayload.password,
      }, validLoginResponse)
    },
  }
}

const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const authApi = createAuthApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
