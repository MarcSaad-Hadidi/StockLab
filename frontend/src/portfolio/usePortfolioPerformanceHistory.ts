import { useEffect, useRef, useState } from 'react'
import { portfolioApi, PortfolioApiError, type PortfolioPerformanceHistory, type PortfolioPerformanceRange } from '../api/portfolioApi'
import { getAuthSession } from '../auth/authStorage'
import { useAuthSession } from '../auth/useAuthUser'

export type PortfolioPerformanceHistoryState = {
  data: PortfolioPerformanceHistory | null
  isLoading: boolean
  error: PortfolioApiError | null
  retry: () => void
}

/** Historical observations load independently of the current portfolio valuation. */
export function usePortfolioPerformanceHistory(range: PortfolioPerformanceRange): PortfolioPerformanceHistoryState {
  const session = useAuthSession()
  const userId = session?.user.id ?? null
  const authorization = session ? `${session.tokenType} ${session.accessToken}` : null
  const [revision, setRevision] = useState(0)
  const key = JSON.stringify([userId, authorization, range, revision])
  const [result, setResult] = useState<{ key: string; data: PortfolioPerformanceHistory | null; error: PortfolioApiError | null } | null>(null)
  const activeRequest = useRef<AbortController | null>(null)
  const retryQueued = useRef(false)

  useEffect(() => {
    retryQueued.current = false
    if (!userId || !authorization) return
    const controller = new AbortController()
    activeRequest.current = controller
    const active = () => {
      const current = getAuthSession()
      return !controller.signal.aborted && current?.user.id === userId
        && `${current.tokenType} ${current.accessToken}` === authorization
    }
    void portfolioApi.getPerformanceHistory(range, controller.signal, authorization)
      .then(data => { if (active()) setResult({ key, data, error: null }) })
      .catch((error: unknown) => {
        if (!active()) return
        setResult({ key, data: null, error: error instanceof PortfolioApiError ? error
          : new PortfolioApiError(502, 'invalid_response', 'The history service returned an invalid response.') })
      })
      .finally(() => { if (activeRequest.current === controller) activeRequest.current = null })
    return () => {
      controller.abort()
      if (activeRequest.current === controller) activeRequest.current = null
    }
  }, [userId, authorization, range, key])

  useEffect(() => {
    if (!userId || !authorization) return
    const refresh = () => {
      if (document.visibilityState !== 'visible' || activeRequest.current || retryQueued.current) return
      retryQueued.current = true
      setRevision(value => value + 1)
    }
    const timer = window.setInterval(refresh, 60_000)
    window.addEventListener('focus', refresh)
    document.addEventListener('visibilitychange', refresh)
    return () => {
      window.clearInterval(timer)
      window.removeEventListener('focus', refresh)
      document.removeEventListener('visibilitychange', refresh)
    }
  }, [userId, authorization])

  function retry() {
    const current = getAuthSession()
    if (!current || current.user.id !== userId || `${current.tokenType} ${current.accessToken}` !== authorization
      || activeRequest.current || retryQueued.current) return
    retryQueued.current = true
    setRevision(value => value + 1)
  }
  if (!session) return { data: null, isLoading: false, retry,
    error: new PortfolioApiError(401, 'unauthorized', 'Your session has expired. Please sign in again.') }
  const current = result?.key === key ? result : null
  return { data: current?.data ?? null, error: current?.error ?? null, isLoading: current === null, retry }
}
