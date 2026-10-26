import { Fragment, useEffect, useRef, type ReactNode } from 'react'
import { getAuthSession } from './authStorage'
import { useAuthSession } from './useAuthUser'
import { routeFor } from '../navigation/routes'

/** Every private document mounts behind this boundary, including direct/legacy URLs. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const session = useAuthSession()
  const authenticated = session !== null
  const redirecting = useRef(false)
  useEffect(() => {
    if (authenticated) redirecting.current = false
    else if (!redirecting.current && !getAuthSession()) {
      redirecting.current = true
      window.location.replace(routeFor('login'))
    }
  }, [authenticated])

  // Do not mount loaders or expose a protected shell while navigation is pending.
  // Replacing the owner also discards local drafts and component-owned caches.
  return session ? <Fragment key={session.user.id}>{children}</Fragment> : null
}
