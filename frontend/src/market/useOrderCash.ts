import { useEffect, useRef, useState } from 'react'
import { PortfolioApiError, portfolioApi } from '../api/portfolioApi'
import { getAuthSession } from '../auth/authStorage'
import { useAuthSession } from '../auth/useAuthUser'

export function useOrderCash() {
  const session = useAuthSession()
  const userId = session?.user.id ?? null
  const authorization = session ? `${session.tokenType} ${session.accessToken}` : null
  const [revision, setRevision] = useState(0)
  const key = JSON.stringify([userId, authorization, revision])
  const [result, setResult] = useState<{ key: string; data: { balance: string; currency: string } | null; error: PortfolioApiError | null } | null>(null)
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
    void portfolioApi.getPortfolio(controller.signal, authorization)
      .then(portfolio => {
        if (active()) setResult({ key, data: { balance: portfolio.cashBalance, currency: portfolio.currency }, error: null })
      })
      .catch((error: unknown) => {
        if (active()) setResult({ key, data: null, error: error instanceof PortfolioApiError ? error
          : new PortfolioApiError(502, 'invalid_response', 'The portfolio service returned an invalid response.') })
      })
      .finally(() => { if (activeRequest.current === controller) activeRequest.current = null })
    return () => {
      controller.abort()
      if (activeRequest.current === controller) activeRequest.current = null
    }
  }, [userId, authorization, key])

  function retry() {
    const current = getAuthSession()
    if (!current || current.user.id !== userId || `${current.tokenType} ${current.accessToken}` !== authorization
      || activeRequest.current || retryQueued.current) return
    retryQueued.current = true
    setRevision(value => value + 1)
  }

  const current = result?.key === key ? result : null
  // Mask private data during render, before effect cleanup on a session change.
  if (!session) return { data: null, isLoading: false, retry,
    error: new PortfolioApiError(401, 'unauthorized', 'Your session has expired. Please sign in again.') }
  return { data: current?.data ?? null, isLoading: current === null, error: current?.error ?? null, retry }
}
