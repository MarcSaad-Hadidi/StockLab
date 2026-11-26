import { useSyncExternalStore } from 'react'
import { profileApi, ProfileApiError } from '../api/profileApi'
import { getAuthSession, type AuthSession } from '../auth/authStorage'
import { isSupportedLanguage, type Language } from './languages'

const loginChoiceKey = 'stocklab-login-language'
let unstoredLoginChoice: Language | null | undefined

// A manual choice on Register/Login belongs to the next login in this tab.
export function chooseLoginLanguage(language: Language) {
  unstoredLoginChoice = language
  try { window.sessionStorage.setItem(loginChoiceKey, language) } catch { /* Still works in the current page. */ }
}
export function getLoginLanguage(): Language | null {
  if (unstoredLoginChoice !== undefined) return unstoredLoginChoice
  try {
    const language = window.sessionStorage.getItem(loginChoiceKey)
    return isSupportedLanguage(language) ? language : null
  } catch { return null }
}
export function clearLoginLanguage() {
  unstoredLoginChoice = null
  try { window.sessionStorage.removeItem(loginChoiceKey) } catch { /* The in-memory choice is cleared too. */ }
}

const pending = new Set<string>()
const listeners = new Set<() => void>()
const notify = () => { for (const changed of listeners) changed() }
const subscribe = (changed: () => void) => { listeners.add(changed); return () => { listeners.delete(changed) } }
export const isLanguageSaving = (userId?: string) => userId !== undefined && pending.has(userId)
export function useLanguageSaving(userId?: string) {
  return useSyncExternalStore(subscribe, () => isLanguageSaving(userId), () => false)
}

export type LanguageIntent = { id: string; language: Language; pending: boolean }
const intentKey = (userId: string) => `stocklab-language-intent:${userId}`
const completionKey = (userId: string, id: string) => `stocklab-language-completed:${userId}:${id}`
function readIntent(userId: string): LanguageIntent | null {
  try {
    const value: unknown = JSON.parse(window.localStorage.getItem(intentKey(userId)) ?? 'null')
    if (value && typeof value === 'object' && 'id' in value && typeof value.id === 'string' && value.id.length > 0 && value.id.length <= 128
      && 'language' in value && isSupportedLanguage(value.language) && 'pending' in value && typeof value.pending === 'boolean')
      return { ...value, pending: value.pending && window.localStorage.getItem(completionKey(userId, value.id)) !== '1' } as LanguageIntent
  } catch { /* Malformed/blocked storage cannot authorize a write. */ }
  return null
}
function writeIntent(userId: string, intent: LanguageIntent) {
  try {
    const value = JSON.stringify(intent)
    window.localStorage.setItem(intentKey(userId), value)
    if (window.localStorage.getItem(intentKey(userId)) !== value) throw new Error('Preference intent was not stored')
  } catch { throw new ProfileApiError(0, 'offline') }
}
function completeIntent(userId: string, intent: LanguageIntent) {
  // Completion never rewrites the latest pointer: another document may have
  // published a newer choice immediately after the last ownership check.
  const key = completionKey(userId, intent.id)
  try {
    window.localStorage.setItem(key, '1')
    if (window.localStorage.getItem(key) !== '1') throw new Error('Preference completion was not stored')
  } catch { throw new ProfileApiError(0, 'offline') }
}
export function getPendingLanguageIntent(userId: string) {
  const intent = readIntent(userId)
  return intent?.pending ? intent : null
}
export const isSupersededLanguageChange = (error: unknown) =>
  error !== null && typeof error === 'object' && 'name' in error && error.name === 'AbortError'

// All selectors in this document share one language write per account.
export async function saveLanguagePreference(language: Language, session: AuthSession, signal?: AbortSignal, login = false, resume?: LanguageIntent) {
  if (pending.has(session.user.id)) throw new ProfileApiError(409, 'profile_update_conflict')
  const intent = resume ?? { id: crypto.randomUUID(), language, pending: true }
  if (!resume) writeIntent(session.user.id, intent)
  const sameIntent = () => { const latest = readIntent(session.user.id); return latest?.id === intent.id && latest.pending }
  const current = () => sameIntent() && (login || getAuthSession()?.user.id === session.user.id)
  pending.add(session.user.id); notify()
  try {
    for (let attempt = 0; ; attempt++) {
      if (!current()) throw new DOMException('Language choice superseded', 'AbortError')
      try {
        const result = await profileApi.updateLanguage(language, signal,
          login ? `${session.tokenType} ${session.accessToken}` : undefined, snapshot => {
            if (snapshot.id !== session.user.id) throw new ProfileApiError(502, 'invalid_response')
            return current()
          })
        signal?.throwIfAborted()
        if (!current()) throw new DOMException('Language choice superseded', 'AbortError')
        if (result.id !== session.user.id || result.preferredLanguage !== language) throw new ProfileApiError(502, 'invalid_response')
        completeIntent(session.user.id, intent)
        if (readIntent(session.user.id)?.id !== intent.id || !login && getAuthSession()?.user.id !== session.user.id)
          throw new DOMException('Language choice superseded', 'AbortError')
        return result
      } catch (error) {
        // handleUnauthorized can clear the current session before rejecting the request.
        if (sameIntent() && !getAuthSession() && error instanceof ProfileApiError && error.code === 'unauthorized') throw error
        if (!current()) throw new DOMException('Language choice superseded', 'AbortError')
        // A newer write won the SQL rowversion race. Only the latest intent retries.
        if (!(error instanceof ProfileApiError) || error.status !== 412 || attempt >= 2) throw error
      }
    }
  } finally { pending.delete(session.user.id); notify() }
}
