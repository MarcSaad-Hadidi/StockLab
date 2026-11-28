import { handleUnauthorized } from '../auth/handleUnauthorized'

export const preferenceKeys = ['emailNotifications', 'tradeAlerts', 'marketingCommunications', 'darkMode'] as const
export type PreferenceKey = typeof preferenceKeys[number]
export type UserPreferences = Record<PreferenceKey, boolean> & { userId: string }
export type PreferencesPatch = Partial<Record<PreferenceKey, boolean>>

export class PreferencesApiError extends Error {
  readonly status: number
  constructor(status: number) {
    super(`Preferences request failed (${status})`)
    this.status = status
  }
}

function isPreferences(value: unknown, userId: string): value is UserPreferences {
  if (!value || typeof value !== 'object') return false
  const data = value as Record<string, unknown>
  return data.userId === userId && preferenceKeys.every(key => typeof data[key] === 'boolean')
}

export function createPreferencesApi(baseUrl: string, fetcher: typeof fetch = (...args) => fetch(...args)) {
  async function request(authorization: string, userId: string, patch?: PreferencesPatch, signal?: AbortSignal) {
    const response = await fetcher(`${baseUrl.replace(/\/$/, '')}/api/profile/preferences`, {
      method: patch ? 'PATCH' : 'GET',
      headers: { Authorization: authorization, Accept: 'application/json', ...(patch ? { 'Content-Type': 'application/json' } : {}) },
      body: patch ? JSON.stringify(patch) : undefined,
      cache: 'no-store',
      signal,
    })
    handleUnauthorized(response, authorization, signal)
    if (!response.ok) throw new PreferencesApiError(response.status)
    const result: unknown = await response.json()
    if (!isPreferences(result, userId) || (patch && preferenceKeys.some(key => key in patch && patch[key] !== result[key]))) {
      throw new PreferencesApiError(502)
    }
    return result
  }
  return {
    get: (authorization: string, userId: string, signal?: AbortSignal) => request(authorization, userId, undefined, signal),
    update: (authorization: string, userId: string, patch: PreferencesPatch, signal?: AbortSignal) =>
      request(authorization, userId, patch, signal),
  }
}

const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const preferencesApi = createPreferencesApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
