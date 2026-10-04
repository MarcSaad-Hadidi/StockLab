import { useEffect, useRef, useState } from 'react'
import { PortfolioApiError, portfolioApi, type PortfolioApiTransaction } from '../api/portfolioApi'
import { getAuthSession } from '../auth/authStorage'
import { useAuthSession } from '../auth/useAuthUser'

export type RecentTransactionsState = {
  data: PortfolioApiTransaction[] | null
  isLoading: boolean
  error: PortfolioApiError | null
  retry: () => void
}

export function useRecentTransactions(enabled: boolean): RecentTransactionsState {
  const session = useAuthSession()
  const userId = session?.user.id ?? null
  const authorization = session ? `${session.tokenType} ${session.accessToken}` : null
  const [revision, setRevision] = useState(0)
  const requestKey = JSON.stringify([userId, authorization, revision])
  const [result, setResult] = useState<{ key: string; data: PortfolioApiTransaction[] | null; error: PortfolioApiError | null } | null>(null)
  const activeRequest = useRef<AbortController | null>(null)
  const retryQueued = useRef(false)

  useEffect(() => {
    retryQueued.current = false
    if (!enabled || !userId || !authorization) return
    const controller = new AbortController()
    activeRequest.current = controller
    const active = () => {
      const current = getAuthSession()
      return !controller.signal.aborted && current?.user.id === userId
        && `${current.tokenType} ${current.accessToken}` === authorization
    }
    void portfolioApi.getRecentTransactions(5, controller.signal, authorization)
      .then(data => { if (active()) setResult({ key: requestKey, data, error: null }) })
      .catch((error: unknown) => {
        if (!active()) return
        setResult({ key: requestKey, data: null, error: error instanceof PortfolioApiError ? error
          : new PortfolioApiError(502, 'invalid_response', 'The recent transaction service returned an invalid response.') })
      })
      .finally(() => { if (activeRequest.current === controller) activeRequest.current = null })
    return () => {
      controller.abort()
      if (activeRequest.current === controller) activeRequest.current = null
    }
  }, [enabled, userId, authorization, requestKey])

  function retry() {
    const current = getAuthSession()
    if (!enabled || !current || current.user.id !== userId
      || `${current.tokenType} ${current.accessToken}` !== authorization
      || activeRequest.current || retryQueued.current) return
    retryQueued.current = true
    setRevision(value => value + 1)
  }

  if (!session) return { data: null, isLoading: false, retry,
    error: new PortfolioApiError(401, 'unauthorized', 'Your session has expired. Please sign in again.') }
  const current = enabled && result?.key === requestKey ? result : null
  return { data: current?.data ?? null, isLoading: current === null, error: current?.error ?? null, retry }
}
