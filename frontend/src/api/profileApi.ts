import { getAuthorizationHeader } from '../auth/authStorage'
import { handleUnauthorized } from '../auth/handleUnauthorized'
import { isSupportedLanguage, type Language } from '../i18n/languages'

export type UserProfile = {
  id: string
  displayName: string
  email: string
  createdAtUtc: string
  updatedAtUtc: string
  preferredLanguage: Language
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
    && isSupportedLanguage(value.preferredLanguage)
}

export function createProfileApi(baseUrl: string, fetcher: typeof fetch = fetch,
  authorization: () => { Authorization: string } | null = getAuthorizationHeader) {
  async function request(payload?: UpdateProfileRequest | { preferredLanguage: Language }, signal?: AbortSignal, loginAuthorization?: string, version?: string) {
    const auth = loginAuthorization ? { Authorization: loginAuthorization } : authorization()
    if (!auth) throw new ProfileApiError(401, 'unauthorized')
    let response: Response
    const languageUpdate = payload && 'preferredLanguage' in payload
    const body = payload ? JSON.stringify('preferredLanguage' in payload
      ? { preferredLanguage: payload.preferredLanguage }
      : { displayName: payload.displayName.trim(), email: payload.email.trim() }) : undefined
    try {
      response = await fetcher(`${baseUrl.replace(/\/$/, '')}/api/profile${languageUpdate ? '/language' : ''}`, {
        method: payload ? 'PUT' : 'GET', signal,
        ...(!payload ? { cache: 'no-store' as const } : {}),
        ...(languageUpdate ? { keepalive: true } : {}),
        headers: { Accept: 'application/json', ...auth, ...(payload ? { 'Content-Type': 'application/json' } : {}), ...(version ? { 'If-Match': version } : {}) },
        ...(payload ? { body } : {}),
      })
    } catch {
      if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
      throw new ProfileApiError(0, 'offline')
    }
    handleUnauthorized(response, auth.Authorization, signal)
    let result: unknown
    try { result = await response.json() }
    catch {
      if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
      if (response.ok) throw new ProfileApiError(502, 'invalid_response')
    }
    if (!response.ok) {
      const details = record(result) ? result : {}
      const code: ProfileErrorCode = response.status === 401 ? 'unauthorized'
        : response.status === 404 ? 'profile_not_found'
        : response.status === 400 ? 'validation_error'
        : response.status === 409 && details.error === 'email_already_registered' ? 'email_already_registered'
        : response.status === 409 || response.status === 412 ? 'profile_update_conflict' : 'server_error'
      const fields = record(details.errors) ? Object.fromEntries(Object.entries(details.errors)
        .filter((entry): entry is [string, string[]] => Array.isArray(entry[1]) && entry[1].every(text))) : {}
      throw new ProfileApiError(response.status, code, fields)
    }
    if (!isProfile(result)) throw new ProfileApiError(502, 'invalid_response')
    if (languageUpdate && result.preferredLanguage !== payload.preferredLanguage) throw new ProfileApiError(502, 'invalid_response')
    return { profile: result, version: response.headers.get('ETag') }
  }
  return {
    getProfile: async (signal?: AbortSignal) => (await request(undefined, signal)).profile,
    updateProfile: async (payload: UpdateProfileRequest, signal?: AbortSignal) => (await request(payload, signal)).profile,
    async updateLanguage(preferredLanguage: Language, signal?: AbortSignal, loginAuthorization?: string, beforeWrite?: (profile: UserProfile) => boolean) {
      // Keep GET and PUT bound to the same account even if another tab logs in.
      const token = loginAuthorization ?? authorization()?.Authorization
      if (!token) throw new ProfileApiError(401, 'unauthorized')
      const snapshot = await request(undefined, signal, token)
      const version = snapshot.version
      if (!version || version.length > 128 || !/^"[A-Za-z0-9+/]*={0,2}"$/.test(version)) throw new ProfileApiError(502, 'invalid_response')
      try { const decoded = atob(version.slice(1, -1)); if (decoded.length !== 8 || btoa(decoded) !== version.slice(1, -1)) throw new Error('Invalid ETag') }
      catch { throw new ProfileApiError(502, 'invalid_response') }
      if (beforeWrite && !beforeWrite(snapshot.profile)) throw new DOMException('Language choice superseded', 'AbortError')
      return (await request({ preferredLanguage }, signal, token, version)).profile
    },
  }
}

const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const profileApi = createProfileApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
