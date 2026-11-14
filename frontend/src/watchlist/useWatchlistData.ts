import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { marketDataApi } from '../api/marketDataClient'
import { watchlistApi, WatchlistApiError, type WatchlistApiItem, type WatchlistErrorCode } from '../api/watchlistApi'
import { clearAuthSession, getAuthSession, type AuthSession } from '../auth/authStorage'
import { subscribeAuthSession } from '../auth/useAuthUser'
import type { WatchlistItem } from './watchlistData'

function sameSession(expected: AuthSession | null) {
  const current = getAuthSession()
  return expected ? current?.accessToken === expected.accessToken && current.user.id === expected.user.id : current === null
}

function errorCode(error: unknown): WatchlistErrorCode {
  return error instanceof WatchlistApiError ? error.code : 'server_error'
}

function unquoted(item: WatchlistApiItem): WatchlistItem {
  return { ...item, name: item.symbol, exchange: '—', currency: null, price: null, change: null, changePercent: null, tone: '', provenance: null }
}

async function enrich(items: WatchlistItem[], signal: AbortSignal, refreshQuotes: boolean): Promise<WatchlistItem[]> {
  const result: WatchlistItem[] = []
  for (let start = 0; start < items.length; start += 5) {
    signal.throwIfAborted()
    result.push(...await Promise.all(items.slice(start, start + 5).map(async item => {
      try {
        const quote = await marketDataApi.quote(item.symbol, signal, { refresh: refreshQuotes })
        const currency = quote.currency.trim().toUpperCase()
        return { ...item, name: quote.name?.trim() || item.symbol, exchange: quote.exchange?.trim() || '—',
          provenance: { source: quote.source, asOfUtc: quote.asOfUtc, receivedAtUtc: quote.receivedAtUtc },
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
const watchlistChangeKey = 'stocklab-watchlist-changed'

export function useWatchlistData(enrichQuotes = true) {
  const [state, setState] = useState<State>(initialState)
  const [loadRequest, setLoadRequest] = useState<{ refreshQuotes: boolean; background?: boolean; revision: number }>({ refreshQuotes: false, revision: 0 })
  const loadPending = useRef(true)
  const loadGeneration = useRef(0)
  const sessionRef = useRef<AuthSession | null>(null)
  const pending = useRef(new Set<string>())
  const mutations = useRef(new Set<AbortController>())
  const synchronize = useRef<(() => void) | null>(null)
  const origin = useRef({})
  const refreshNeeded = useRef(false)
  const refreshTimer = useRef<number | undefined>(undefined)

  const requestLoad = useCallback((refreshQuotes: boolean, background = false) => {
    // Block even handlers from an earlier render before React starts the read.
    loadPending.current = true
    setLoadRequest({ refreshQuotes, background, revision: ++loadGeneration.current })
  }, [])

  const cancelRefresh = useCallback(() => {
    window.clearTimeout(refreshTimer.current)
    refreshTimer.current = undefined
    refreshNeeded.current = false
  }, [])

  const scheduleRefresh = useCallback(() => {
    window.clearTimeout(refreshTimer.current)
    const session = sessionRef.current
    if (!session || !sameSession(session)) { cancelRefresh(); synchronize.current?.(); return }
    refreshNeeded.current = true
    // Keep writes alive; their final completion will replay any missed change.
    if (pending.current.size > 0 || document.visibilityState !== 'visible') return
    refreshTimer.current = window.setTimeout(() => {
      refreshTimer.current = undefined
      if (!sameSession(session)) { cancelRefresh(); synchronize.current?.(); return }
      if (!refreshNeeded.current || pending.current.size > 0 || document.visibilityState !== 'visible') return
      refreshNeeded.current = false
      requestLoad(false, true)
    }, 100)
  }, [cancelRefresh, requestLoad])

  useEffect(() => {
    const changed = (userId: unknown) => { if (typeof userId === 'string' && userId === getAuthSession()?.user.id) scheduleRefresh() }
    const onStorage = (event: StorageEvent) => {
      if (event.key !== watchlistChangeKey || !event.newValue) return
      try {
        const message: unknown = JSON.parse(event.newValue)
        if (message && typeof message === 'object' && 'userId' in message) changed(message.userId)
      } catch { /* A notification never supplies membership; malformed messages are ignored. */ }
    }
    const onLocalChange = (event: Event) => {
      const message = (event as CustomEvent<{ userId: string; origin: object }>).detail
      if (message && message.origin !== origin.current) changed(message.userId)
    }
    const onVisible = () => { if (document.visibilityState === 'visible') scheduleRefresh(); else window.clearTimeout(refreshTimer.current) }
    const onPageShow = (event: PageTransitionEvent) => { if (event.persisted) scheduleRefresh() }
    window.addEventListener('storage', onStorage)
    window.addEventListener(watchlistChangeKey, onLocalChange)
    window.addEventListener('focus', scheduleRefresh)
    document.addEventListener('visibilitychange', onVisible)
    window.addEventListener('pageshow', onPageShow)
    return () => {
      cancelRefresh()
      window.removeEventListener('storage', onStorage)
      window.removeEventListener(watchlistChangeKey, onLocalChange)
      window.removeEventListener('focus', scheduleRefresh)
      document.removeEventListener('visibilitychange', onVisible)
      window.removeEventListener('pageshow', onPageShow)
    }
  }, [cancelRefresh, scheduleRefresh])

  useEffect(() => {
    const controller = new AbortController()
    const session = getAuthSession()
    sessionRef.current = session
    const mutationControllers = mutations.current
    const pendingSymbols = pending.current
    function synchronizeSession() {
      if (sameSession(session)) return
      cancelRefresh()
      controller.abort()
      mutationControllers.forEach(request => request.abort())
      mutationControllers.clear()
      pendingSymbols.clear()
      setState(initialState())
      requestLoad(false)
    }
    synchronize.current = synchronizeSession
    const unsubscribe = subscribeAuthSession(synchronizeSession)
    void Promise.resolve().then(async () => {
      controller.signal.throwIfAborted()
      setState(current => loadRequest.background ? { ...current, loading: true, error: null } : initialState())
      if (!session) throw new WatchlistApiError(401, 'unauthorized')
      const membership = await watchlistApi.getWatchlist(controller.signal)
      if (controller.signal.aborted) return
      if (!sameSession(session)) { synchronizeSession(); return }
      const items = membership.map(unquoted)
      // Membership is available even while the independent quote provider is loading.
      setState(current => ({ ...current, items: items.map(item => {
        const previous = current.items.find(row => row.symbol === item.symbol)
        return previous ? { ...previous, createdAtUtc: item.createdAtUtc } : item
      }), error: null, loading: enrichQuotes && items.length > 0 }))
      if (enrichQuotes && items.length > 0) {
        const quoted = await enrich(items, controller.signal, loadRequest.refreshQuotes)
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
    }).finally(() => {
      if (!controller.signal.aborted && loadGeneration.current === loadRequest.revision) loadPending.current = false
    })
    return () => {
      controller.abort()
      mutationControllers.forEach(request => request.abort())
      mutationControllers.clear()
      pendingSymbols.clear()
      unsubscribe()
      if (synchronize.current === synchronizeSession) synchronize.current = null
    }
  }, [loadRequest, enrichQuotes, cancelRefresh, requestLoad])

  const reload = useCallback(() => {
    if (pending.current.size === 0) { cancelRefresh(); requestLoad(enrichQuotes) }
  }, [enrichQuotes, cancelRefresh, requestLoad])

  async function mutate(symbol: string, operation: 'add' | 'remove'): Promise<boolean> {
    symbol = symbol.trim().toUpperCase()
    if (loadPending.current || pending.current.has(symbol) || state.loading || state.error) return false
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
      // Publish only confirmed/reconciled changes, without storing credentials
      // or a second copy of membership. Focus remains a fallback if storage fails.
      try {
        window.localStorage.setItem(watchlistChangeKey, JSON.stringify({ userId: session.user.id, revision: crypto.randomUUID() }))
      } catch { /* Storage restrictions must not turn a successful API write into a failure. */ }
      window.dispatchEvent(new window.CustomEvent(watchlistChangeKey, { detail: { userId: session.user.id, origin: origin.current } }))
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
      if (active()) {
        setState(current => ({ ...current, pendingSymbols: new Set(pending.current) }))
        if (pending.current.size === 0 && refreshNeeded.current) scheduleRefresh()
      }
    }
  }

  const favorites = useMemo(() => new Set(state.items.map(item => item.symbol)), [state.items])
  return { ...state, favorites, reload,
    add: (symbol: string) => mutate(symbol, 'add'), remove: (symbol: string) => mutate(symbol, 'remove') }
}
