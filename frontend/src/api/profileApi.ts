import { getAuthorizationHeader } from '../auth/authStorage'

export type UserProfile = {
  id: string
  displayName: string
  email: string
  createdAtUtc: string
  updatedAtUtc: string
}
export type UpdateProfileRequest = Pick<UserProfile, 'displayName' | 'email'>
export type ProfileErrorCode = 'unauthorized' | 'profile_not_found' | 'validation_error'
  | 'email_already_registered' | 'profile_update_conflict' | 'offline' | 'invalid_response' | 'server_error'

export class ProfileApiError extends Error {
  readonly status: number
  readonly code: ProfileErrorCode
  readonly fieldErrors: Record<string, string[]>
  constructor(status: number, code: ProfileErrorCode, fieldErrors: Record<string, string[]> = {}) {
    super(`Profile request failed: ${code}`)
    this.name = 'ProfileApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
  }
}

const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const utcDate = (value: unknown) => text(value) && /(?:Z|[+-]\d{2}:\d{2})$/i.test(value) && Number.isFinite(Date.parse(value))
function isProfile(value: unknown): value is UserProfile {
  return record(value) && text(value.id) && /^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(value.id)
    && text(value.displayName) && text(value.email) && utcDate(value.createdAtUtc) && utcDate(value.updatedAtUtc)
}

export function createProfileApi(baseUrl: string, fetcher: typeof fetch = fetch,
  authorization: () => { Authorization: string } | null = getAuthorizationHeader) {
  async function request(payload?: UpdateProfileRequest, signal?: AbortSignal): Promise<UserProfile> {
    const auth = authorization()
    if (!auth) throw new ProfileApiError(401, 'unauthorized')
    let response: Response
    try {
      response = await fetcher(`${baseUrl.replace(/\/$/, '')}/api/profile`, {
        method: payload ? 'PUT' : 'GET', signal,
        headers: { Accept: 'application/json', ...auth, ...(payload ? { 'Content-Type': 'application/json' } : {}) },
        ...(payload ? { body: JSON.stringify({ displayName: payload.displayName.trim(), email: payload.email.trim() }) } : {}),
      })
    } catch {
      if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
      throw new ProfileApiError(0, 'offline')
    }
    let body: unknown
    try { body = await response.json() }
    catch {
      if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
      if (response.ok) throw new ProfileApiError(502, 'invalid_response')
    }
    if (!response.ok) {
      const details = record(body) ? body : {}
      const code: ProfileErrorCode = response.status === 401 ? 'unauthorized'
        : response.status === 404 ? 'profile_not_found'
        : response.status === 400 ? 'validation_error'
        : response.status === 409 && details.error === 'email_already_registered' ? 'email_already_registered'
        : response.status === 409 ? 'profile_update_conflict' : 'server_error'
      const fields = record(details.errors) ? Object.fromEntries(Object.entries(details.errors)
        .filter((entry): entry is [string, string[]] => Array.isArray(entry[1]) && entry[1].every(text))) : {}
      throw new ProfileApiError(response.status, code, fields)
    }
    if (!isProfile(body)) throw new ProfileApiError(502, 'invalid_response')
    return body
  }
  return {
    getProfile: (signal?: AbortSignal) => request(undefined, signal),
    updateProfile: (payload: UpdateProfileRequest, signal?: AbortSignal) => request(payload, signal),
  }
}

const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const profileApi = createProfileApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
