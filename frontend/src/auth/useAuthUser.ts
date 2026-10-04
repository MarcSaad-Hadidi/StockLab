import { useSyncExternalStore } from 'react'
import { authSessionChangedEvent, authStorageKey, getAuthSession, type AuthUser } from './authStorage'

function snapshot(): string | null {
  const session = getAuthSession()
  // A primitive snapshot stays stable between reads and exposes no access token.
  return session ? JSON.stringify({ user: session.user, expiresAtUtc: session.expiresAtUtc }) : null
}
function subscribe(changed: () => void) {
  let timer: number | undefined
  function refresh() {
    window.clearTimeout(timer)
    changed()
    const session = getAuthSession()
    if (session) timer = window.setTimeout(refresh,
      Math.min(2_147_483_647, Math.max(0, Date.parse(session.expiresAtUtc) - Date.now())))
  }
  const onStorage = (event: StorageEvent) => { if (event.key === authStorageKey || event.key === null) refresh() }
  const onVisibility = () => { if (document.visibilityState === 'visible') refresh() }
  window.addEventListener(authSessionChangedEvent, refresh)
  window.addEventListener('storage', onStorage)
  window.addEventListener('focus', refresh)
  document.addEventListener('visibilitychange', onVisibility)
  refresh()
  return () => {
    window.clearTimeout(timer)
    window.removeEventListener(authSessionChangedEvent, refresh)
    window.removeEventListener('storage', onStorage)
    window.removeEventListener('focus', refresh)
    document.removeEventListener('visibilitychange', onVisibility)
  }
}

export function useAuthUser(): AuthUser | null {
  const value = useSyncExternalStore(subscribe, snapshot, () => null)
  return value ? (JSON.parse(value) as { user: AuthUser }).user : null
}
