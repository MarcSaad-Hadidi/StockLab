import { useEffect, useState } from 'react'
import { errorKey } from '../api/marketDataApi'
export type RequestState<T> = { data?: T; error?: string; loading: boolean }
/** Identity changes discard old results immediately, including uncancellable responses. */
export function useMarketRequest<T>(
  key: string,
  loader: (signal: AbortSignal) => Promise<T>,
  enabled = true,
  delay = 0
) {
  const [attempt, setAttempt] = useState(0)
  const identity = `${key}:${attempt}`
  const [state, setState] = useState<RequestState<T> & { identity: string }>({
    identity: '',
    loading: false
  })
  useEffect(() => {
    if (!enabled) return
    const controller = new AbortController()
    const timer = setTimeout(() => {
      setState({ identity, loading: true })
      loader(controller.signal)
        .then((data) => {
          if (!controller.signal.aborted)
            setState({ identity, data, loading: false })
        })
        .catch((error) => {
          if (!controller.signal.aborted)
            setState({ identity, error: errorKey(error), loading: false })
        })
    }, delay)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [identity, loader, enabled, delay])
  const visible: RequestState<T> = !enabled
    ? { loading: false }
    : state.identity === identity
      ? state
      : { loading: true }
  return { ...visible, retry: () => setAttempt((n) => n + 1) }
}
