import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { marketDataApi } from '../api/marketDataClient'
import { watchlistApi, WatchlistApiError, type WatchlistApiItem, type WatchlistErrorCode } from '../api/watchlistApi'
import { authStorageKey, clearAuthSession, getAuthSession, type AuthSession } from '../auth/authStorage'
import type { WatchlistItem } from './watchlistData'

function sameSession(expected: AuthSession | null) {
  const current = getAuthSession()
  return expected ? current?.accessToken === expected.accessToken && current.user.id === expected.user.id : current === null
}

function errorCode(error: unknown): WatchlistErrorCode {
  return error instanceof WatchlistApiError ? error.code : 'server_error'
}

function unquoted(item: WatchlistApiItem): WatchlistItem {
  return { ...item, name: item.symbol, exchange: '—', currency: null, price: null, change: null, changePercent: null, tone: '' }
}

async function enrich(items: WatchlistItem[], signal: AbortSignal): Promise<WatchlistItem[]> {
  const result: WatchlistItem[] = []
  for (let start = 0; start < items.length; start += 5) {
    signal.throwIfAborted()
    result.push(...await Promise.all(items.slice(start, start + 5).map(async item => {
      try {
        const quote = await marketDataApi.quote(item.symbol, signal)
        const currency = quote.currency.trim().toUpperCase()
        return { ...item, name: quote.name?.trim() || item.symbol, exchange: quote.exchange?.trim() || '—',
          currency: /^[A-Z]{3}$/.test(currency) ? currency : null, price: quote.price, change: quote.change, changePercent: quote.changePercent,
          tone: quote.changePercent === null ? '' : quote.changePercent >= 0 ? 'positive' : 'negative' } satisfies WatchlistItem
      } catch {
        signal.throwIfAborted()
        return item
      }
    })))
  }
  return result
}

type State = { items: WatchlistItem[]; loading: boolean; error: WatchlistErrorCode | null; mutationError: WatchlistErrorCode | null; pendingSymbols: Set<string> }
const initialState = (): State => ({ items: [], loading: true, error: null, mutationError: null, pendingSymbols: new Set() })

export function useWatchlistData(enrichQuotes = true) {
  const [state, setState] = useState<State>(initialState)
  const [revision, setRevision] = useState(0)
  const sessionRef = useRef<AuthSession | null>(null)
  const pending = useRef(new Set<string>())
  const mutations = useRef(new Set<AbortController>())
  const synchronize = useRef<(() => void) | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const session = getAuthSession()
    sessionRef.current = session
    const mutationControllers = mutations.current
    const pendingSymbols = pending.current
    function synchronizeSession() {
      if (sameSession(session)) return
      controller.abort()
      mutationControllers.forEach(request => request.abort())
      pendingSymbols.clear()
      setState(initialState())
      setRevision(value => value + 1)
    }
    synchronize.current = synchronizeSession
    const onStorage = (event: StorageEvent) => {
      if (event.key === authStorageKey || event.key === null) synchronizeSession()
    }
    window.addEventListener('storage', onStorage)
    window.addEventListener('focus', synchronizeSession)
    void Promise.resolve().then(async () => {
      controller.signal.throwIfAborted()
      setState(initialState())
      if (!session) throw new WatchlistApiError(401, 'unauthorized')
      const membership = await watchlistApi.getWatchlist(controller.signal)
      if (controller.signal.aborted) return
      if (!sameSession(session)) { synchronizeSession(); return }
      const items = membership.map(unquoted)
      // Membership is available even while the independent quote provider is loading.
      setState({ ...initialState(), items, loading: enrichQuotes && items.length > 0 })
      if (enrichQuotes && items.length > 0) {
        const quoted = await enrich(items, controller.signal)
        if (controller.signal.aborted) return
        if (!sameSession(session)) { synchronizeSession(); return }
        setState(current => ({ ...current, items: quoted, loading: false }))
      }
    }).catch(error => {
      if (controller.signal.aborted) return
      if (!sameSession(session)) { synchronizeSession(); return }
      const code = errorCode(error)
      if (code === 'unauthorized') clearAuthSession()
      setState({ ...initialState(), loading: false, error: code })
    })
    return () => {
      controller.abort()
      mutationControllers.forEach(request => request.abort())
      pendingSymbols.clear()
      window.removeEventListener('storage', onStorage)
      window.removeEventListener('focus', synchronizeSession)
      if (synchronize.current === synchronizeSession) synchronize.current = null
    }
  }, [revision, enrichQuotes])

  const reload = useCallback(() => {
    if (pending.current.size === 0) setRevision(value => value + 1)
  }, [])

  async function mutate(symbol: string, operation: 'add' | 'remove'): Promise<boolean> {
    symbol = symbol.trim().toUpperCase()
    if (pending.current.has(symbol) || state.loading || state.error) return false
    const session = sessionRef.current
    if (!session || !sameSession(session)) { synchronize.current?.(); return false }
    const controller = new AbortController()
    mutations.current.add(controller)
    pending.current.add(symbol)
    setState(current => ({ ...current, mutationError: null, pendingSymbols: new Set(pending.current) }))
    const active = () => !controller.signal.aborted && sameSession(session)
    try {
      try {
        if (operation === 'add') {
          const item = await watchlistApi.addToWatchlist(symbol, controller.signal)
          if (!active()) { synchronize.current?.(); return false }
          setState(current => ({ ...current, items: [...current.items.filter(row => row.symbol !== item.symbol), unquoted(item)] }))
        } else {
          await watchlistApi.removeFromWatchlist(symbol, controller.signal)
          if (!active()) { synchronize.current?.(); return false }
          setState(current => ({ ...current, items: current.items.filter(item => item.symbol !== symbol) }))
        }
      } catch (error) {
        const code = errorCode(error)
        if ((operation === 'add' && code === 'already_exists') || (operation === 'remove' && code === 'not_found')) {
          const membership = await watchlistApi.getWatchlist(controller.signal)
          if (!active()) { synchronize.current?.(); return false }
          const item = membership.find(item => item.symbol === symbol)
          if (Boolean(item) !== (operation === 'add')) throw error
          setState(current => ({ ...current, items: item
            ? [...current.items.filter(row => row.symbol !== symbol), current.items.find(row => row.symbol === symbol) ?? unquoted(item)]
            : current.items.filter(row => row.symbol !== symbol) }))
        } else throw error
      }
      return true
    } catch (error) {
      if (!active()) { synchronize.current?.(); return false }
      const code = errorCode(error)
      if (code === 'unauthorized') {
        clearAuthSession()
        mutations.current.forEach(request => request.abort())
        pending.current.clear()
        setState({ ...initialState(), loading: false, error: 'unauthorized' })
      } else setState(current => ({ ...current, mutationError: code }))
      return false
    } finally {
      mutations.current.delete(controller)
      if (!controller.signal.aborted) pending.current.delete(symbol)
      if (active()) setState(current => ({ ...current, pendingSymbols: new Set(pending.current) }))
    }
  }

  const favorites = useMemo(() => new Set(state.items.map(item => item.symbol)), [state.items])
  return { ...state, favorites, reload,
    add: (symbol: string) => mutate(symbol, 'add'), remove: (symbol: string) => mutate(symbol, 'remove') }
}
