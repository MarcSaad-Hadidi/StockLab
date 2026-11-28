import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { preferencesApi, PreferencesApiError, type PreferenceKey } from '../api/preferencesApi'
import { getAuthSession } from '../auth/authStorage'
import { useAuthSession } from '../auth/useAuthUser'
import { PreferencesContext, preferencesChangedKey, type PreferencesState } from './preferencesContext'

/** Shared by authenticated documents and the public market when signed in. */
export function AccountPreferences({ children }: { children: ReactNode }) {
  const session = useAuthSession()
  return session ? <PreferencesScope key={session.user.id} userId={session.user.id}
    token={session.accessToken}>{children}</PreferencesScope> : children
}

function PreferencesScope({ userId, token, children }: { userId: string; token: string; children: ReactNode }) {
  const authorization = `Bearer ${token}`
  const [values, setValues] = useState<PreferencesState['values']>(null)
  const [status, setStatus] = useState<PreferencesState['status']>('loading')
  const pendingRead = useRef<AbortController | null>(null)
  const pendingWrite = useRef<AbortController | null>(null)
  const refreshAfterWrite = useRef(false)
  const ownsSession = useCallback(() => {
    const latest = getAuthSession()
    return latest?.accessToken === token && latest.user.id === userId
  }, [token, userId])

  const refresh = useCallback(async (silently = false) => {
    if (!ownsSession()) return
    if (pendingWrite.current) { refreshAfterWrite.current = true; return }
    pendingRead.current?.abort()
    const request = new AbortController()
    pendingRead.current = request
    try {
      const stored = await preferencesApi.get(authorization, userId, request.signal)
      if (!request.signal.aborted && ownsSession()) {
        setValues(stored)
        setStatus(current => silently && current !== 'loading' && current !== 'loadError' ? current : 'idle')
      }
    } catch {
      if (!request.signal.aborted && ownsSession()) {
        setStatus(current => !silently || current === 'loading' ? 'loadError' : current)
      }
    } finally {
      if (pendingRead.current === request) pendingRead.current = null
    }
  }, [authorization, ownsSession, userId])

  useEffect(() => {
    let active = true
    queueMicrotask(() => { if (active) void refresh() })
    const onStorage = (event: StorageEvent) => {
      if (event.key === preferencesChangedKey && event.newValue?.startsWith(`${userId}:`)) void refresh(true)
    }
    const onFocus = () => { void refresh(true) }
    window.addEventListener('storage', onStorage)
    window.addEventListener('focus', onFocus)
    window.addEventListener('pageshow', onFocus)
    return () => {
      active = false
      window.removeEventListener('storage', onStorage)
      window.removeEventListener('focus', onFocus)
      window.removeEventListener('pageshow', onFocus)
      pendingRead.current?.abort()
      pendingWrite.current?.abort()
      pendingWrite.current = null
      refreshAfterWrite.current = false
    }
  }, [refresh, userId])

  useLayoutEffect(() => {
    const root = document.documentElement
    if (values?.darkMode) root.dataset.stocklabTheme = 'dark'
    else delete root.dataset.stocklabTheme
    return () => { delete root.dataset.stocklabTheme }
  }, [values?.darkMode])

  async function save(key: PreferenceKey, enabled: boolean) {
    if (!values || values[key] === enabled || pendingWrite.current || !ownsSession()) return
    pendingRead.current?.abort()
    const request = new AbortController()
    pendingWrite.current = request
    setValues({ ...values, [key]: enabled })
    setStatus('saving')
    try {
      const updated = await preferencesApi.update(authorization, userId, { [key]: enabled }, request.signal)
      if (request.signal.aborted || !ownsSession()) return
      setValues(updated)
      setStatus('saved')
      // Notify same-origin tabs to read from the API; never broadcast account values or tokens.
      try { window.localStorage.setItem(preferencesChangedKey, `${userId}:${crypto.randomUUID()}`) } catch { /* Refresh on focus if storage is unavailable. */ }
    } catch (error) {
      if (request.signal.aborted || !ownsSession()) return
      setValues(values)
      setStatus(error instanceof PreferencesApiError && error.status === 409 ? 'conflict' : 'saveError')
      refreshAfterWrite.current = true // A network failure can hide a committed update.
    } finally {
      if (pendingWrite.current === request) {
        pendingWrite.current = null
        if (refreshAfterWrite.current && !request.signal.aborted && ownsSession()) {
          refreshAfterWrite.current = false
          void refresh(true)
        }
      }
    }
  }

  return <PreferencesContext.Provider value={{ values, status, save, retry: () => { setStatus('loading'); void refresh() } }}>
    {children}
  </PreferencesContext.Provider>
}
