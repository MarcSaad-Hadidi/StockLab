import { useCallback, useEffect, useRef, useState } from 'react'
import { aiTraderApi, AiTraderApiError, type AiTraderApi } from '../api/aiTraderApi'
import { getAuthSession } from '../auth/authStorage'
import { useAuthSession } from '../auth/useAuthUser'
import type { TabName } from './aiTraderData'

type ResourceData = {
  performance: Awaited<ReturnType<AiTraderApi['getPerformance']>>
  positions: Awaited<ReturnType<AiTraderApi['getPositions']>>
  trades: Awaited<ReturnType<AiTraderApi['getTrades']>>
  decisions: Awaited<ReturnType<AiTraderApi['getDecisions']>>
  rejections: Awaited<ReturnType<AiTraderApi['getRejectedDecisions']>>
  history: Awaited<ReturnType<AiTraderApi['getEquityHistory']>>
  models: Awaited<ReturnType<AiTraderApi['getModels']>>
  backtests: Awaited<ReturnType<AiTraderApi['getBacktests']>>
}
export type ResourceName = keyof ResourceData
export type ResourceState<T> = {
  status: 'idle' | 'loading' | 'success' | 'empty' | 'unavailable' | 'error' | 'unauthorized'
  data: T | null; error: AiTraderApiError | null
}
export type AiTraderResources = { [K in ResourceName]: ResourceState<ResourceData[K]> }
export type AiTraderDataState = {
  resources: AiTraderResources; isLoading: boolean; refresh: () => void; retry: (name: ResourceName) => void; rejectSession: () => void
}
const names: ResourceName[] = ['performance', 'positions', 'history', 'trades', 'decisions', 'rejections', 'models', 'backtests']
type DataView = TabName | 'Summary'
const resourcesFor: Record<DataView, ResourceName[]> = {
  Overview: names, Positions: ['positions', 'performance'], Trades: ['trades'], Decisions: ['decisions', 'rejections'],
  Performance: ['performance', 'history'], Model: ['models'], Backtests: ['backtests'],
  Summary: ['performance'],
}
function emptyResources(status: 'idle' | 'unauthorized' = 'idle'): AiTraderResources {
  return Object.fromEntries(names.map(name => [name, { status, data: null, error: status === 'unauthorized' ? new AiTraderApiError(401, 'unauthorized') : null }])) as AiTraderResources
}

/** One session owns this bounded queue and cache. Nothing is shared across mounts/accounts. */
function createLoader(api: AiTraderApi, userId: string, authorization: string, publish: (resources: AiTraderResources) => void) {
  let resources = emptyResources()
  let disposed = false
  let revoked = false
  let running = 0
  const queue: ResourceName[] = []
  const controllers = new Map<ResourceName, AbortController>()
  const loadedLimits = new Map<ResourceName, number>()
  const requestedLimits = new Map<ResourceName, number>()
  const active = () => {
    const current = getAuthSession()
    return !disposed && !revoked && current?.user.id === userId && `${current.tokenType} ${current.accessToken}` === authorization
  }
  const emit = () => { if (!disposed) publish({ ...resources }) }
  function revoke() {
    revoked = true
    queue.length = 0
    controllers.forEach(controller => controller.abort())
    resources = emptyResources('unauthorized')
    emit()
  }
  async function load<K extends ResourceName>(name: K, limit: number, controller: AbortController) {
    const signal = controller.signal
    const loaders = {
      performance: () => api.getPerformance(signal, authorization), positions: () => api.getPositions(signal, authorization),
      history: () => api.getEquityHistory({ limit: 200 }, signal, authorization), trades: () => api.getTrades(limit, signal, authorization),
      decisions: () => api.getDecisions(limit, signal, authorization), rejections: () => api.getRejectedDecisions(limit, signal, authorization),
      models: () => api.getModels(signal, authorization), backtests: () => api.getBacktests(signal, authorization),
    }
    try {
      const data = await loaders[name]() as ResourceData[K]
      if (!active() || signal.aborted || controllers.get(name) !== controller) return
      resources = { ...resources, [name]: { status: Array.isArray(data) && data.length === 0 ? 'empty' : 'success', data, error: null } }
      loadedLimits.set(name, limit)
      emit()
    } catch (error: unknown) {
      if (!active() || signal.aborted || controllers.get(name) !== controller) return
      const failure = error instanceof AiTraderApiError ? error : new AiTraderApiError(502, 'invalid_response')
      if (failure.code === 'unauthorized') { revoke(); return }
      // Do not disguise a failed refresh as a successful empty result.
      resources = { ...resources, [name]: { status: failure.code === 'unavailable' ? 'unavailable' : 'error', data: null, error: failure } }
      loadedLimits.set(name, limit)
      emit()
    } finally {
      if (controllers.get(name) === controller) controllers.delete(name)
      running--
      if (active()) {
        if ((requestedLimits.get(name) ?? 0) > limit) enqueue(name, requestedLimits.get(name)!, true)
        pump()
      }
    }
  }
  function pump() {
    while (active() && running < 3 && queue.length) {
      const name = queue.shift()!
      const controller = new AbortController()
      controllers.set(name, controller)
      running++
      void load(name, requestedLimits.get(name) ?? 20, controller)
    }
  }
  function enqueue(name: ResourceName, limit: number, force = false) {
    requestedLimits.set(name, Math.max(limit, requestedLimits.get(name) ?? 0))
    if (!active() || controllers.has(name) || queue.includes(name)) return
    if (!force && loadedLimits.has(name) && loadedLimits.get(name)! >= limit) return
    resources = { ...resources, [name]: { status: 'loading', data: null, error: null } }
    queue.push(name)
  }
  return {
    rejectSession: revoke,
    ensure(tab: DataView, force = false) {
      if (!active()) return
      for (const name of resourcesFor[tab]) {
        const history = name === 'trades' || name === 'decisions' || name === 'rejections'
        enqueue(name, history && tab !== 'Overview' ? 200 : 20, force)
      }
      emit(); pump()
    },
    retry(name: ResourceName) { enqueue(name, requestedLimits.get(name) ?? 20, true); emit(); pump() },
    dispose() { disposed = true; queue.length = 0; controllers.forEach(controller => controller.abort()) },
  }
}

export function useAiTraderData(activeTab: DataView, api: AiTraderApi = aiTraderApi): AiTraderDataState {
  const session = useAuthSession()
  const userId = session?.user.id ?? null
  const authorization = session ? `${session.tokenType} ${session.accessToken}` : null
  const sessionKey = JSON.stringify([userId, authorization, session?.expiresAtUtc])
  const [result, setResult] = useState<{ key: string; resources: AiTraderResources } | null>(null)
  const loader = useRef<{ key: string; value: ReturnType<typeof createLoader> } | null>(null)
  useEffect(() => {
    if (!userId || !authorization) return
    const value = createLoader(api, userId, authorization, resources => setResult({ key: sessionKey, resources }))
    loader.current = { key: sessionKey, value }
    return () => { value.dispose(); if (loader.current?.value === value) loader.current = null }
  }, [api, userId, authorization, sessionKey])
  useEffect(() => {
    // Scheduling also prevents StrictMode's discarded mount from sending reads.
    let cancelled = false
    queueMicrotask(() => { if (!cancelled && loader.current?.key === sessionKey) loader.current.value.ensure(activeTab) })
    return () => { cancelled = true }
  }, [sessionKey, activeTab, api])
  const rejectSession = useCallback(() => { if (loader.current?.key === sessionKey) loader.current.value.rejectSession() }, [sessionKey])
  const refresh = useCallback(() => { if (loader.current?.key === sessionKey) loader.current.value.ensure(activeTab, true) }, [sessionKey, activeTab])
  const resources = !session ? emptyResources('unauthorized') : result?.key === sessionKey ? result.resources : emptyResources()
  return {
    resources, isLoading: Boolean(session) && resourcesFor[activeTab].some(name => resources[name].status === 'loading' || resources[name].status === 'idle'),
    refresh,
    retry: name => { if (loader.current?.key === sessionKey) loader.current.value.retry(name) },
    rejectSession,
  }
}

/** Details use exact persisted IDs and are cleared during render when session/selection changes. */
export function useAiTraderDetail(kind: 'trade' | 'decision', id: string | null, api: AiTraderApi = aiTraderApi, rejectSession?: () => void) {
  const session = useAuthSession()
  const userId = session?.user.id
  const authorization = session ? `${session.tokenType} ${session.accessToken}` : undefined
  const key = JSON.stringify([userId, authorization, kind, id])
  type Detail = Awaited<ReturnType<AiTraderApi['getTrade']>> | Awaited<ReturnType<AiTraderApi['getDecision']>>
  const [result, setResult] = useState<{ key: string; state: ResourceState<Detail> } | null>(null)
  const [revision, setRevision] = useState(0)
  useEffect(() => {
    if (!id || !userId || !authorization) return
    const controller = new AbortController()
    const active = () => {
      const current = getAuthSession()
      return !controller.signal.aborted && current?.user.id === userId && `${current.tokenType} ${current.accessToken}` === authorization
    }
    const request = kind === 'trade' ? api.getTrade(id, controller.signal, authorization) : api.getDecision(id, controller.signal, authorization)
    void request.then(data => {
      if (active()) setResult({ key, state: { status: 'success', data, error: null } })
    }).catch((error: unknown) => {
      if (active()) {
        const failure = error instanceof AiTraderApiError ? error : new AiTraderApiError(502, 'invalid_response')
        if (failure.code === 'unauthorized') rejectSession?.()
        setResult({ key, state: { status: failure.code === 'unauthorized' ? 'unauthorized' : 'error', data: null, error: failure } })
      }
    })
    return () => controller.abort()
  }, [id, kind, userId, authorization, key, revision, api, rejectSession])
  return { ...(session ? result?.key === key ? result.state : { status: 'loading' as const, data: null, error: null }
    : { status: 'unauthorized' as const, data: null, error: new AiTraderApiError(401, 'unauthorized') }),
    retry: () => { setResult(null); setRevision(value => value + 1) } }
}
