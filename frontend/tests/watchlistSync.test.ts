import assert from 'node:assert/strict'
import { beforeEach, test, type TestContext } from 'node:test'
import React, { act } from 'react'
import { createRoot } from 'react-dom/client'
import { JSDOM } from 'jsdom'
import { watchlistApi, WatchlistApiError, type WatchlistApiItem } from '../src/api/watchlistApi.ts'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { clearAuthSession, getAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'
import { useWatchlistData } from '../src/watchlist/useWatchlistData.ts'

const dom = new JSDOM('', { url: 'http://localhost/watchlist', pretendToBeVisual: true })
Object.assign(globalThis, { window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
let visibility = 'visible'
Object.defineProperty(document, 'visibilityState', { get: () => visibility })
const session = (id = 'alpha', token = id) => ({ accessToken: token, tokenType: 'Bearer' as const,
  expiresAtUtc: '2099-01-01T00:00:00Z', user: { id, displayName: id, email: `${id}@example.test` } })
const member = (symbol: string): WatchlistApiItem => ({ symbol, createdAtUtc: '2026-11-12T00:00:00Z' })
const quote = (symbol = 'AAPL') => ({ symbol, name: symbol, exchange: 'NASDAQ', currency: 'USD', price: 200,
  priceDecimal: '200', change: 1, changePercent: 0.5, source: 'Mock' as const,
  asOfUtc: '2026-11-12T00:00:00Z', receivedAtUtc: '2026-11-12T00:00:01Z', volume: null,
  open: null, high: null, low: null, previousClose: null, averageVolume: null, isMarketOpen: false, fiftyTwoWeek: null })
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
let timers: Map<number, { delay: number; run: () => void }>
beforeEach(t => {
  visibility = 'visible'; window.localStorage.clear(); saveAuthSession(session())
  let nextId = 0
  timers = new Map()
  t.mock.method(window, 'setTimeout', (callback, delay = 0) => {
    const id = ++nextId; timers.set(id, { delay, run: callback as () => void }); return id
  })
  t.mock.method(window, 'clearTimeout', id => { timers.delete(id!) })
  t.mock.method(marketDataApi, 'quote', async symbol => quote(symbol))
})
async function flush() {
  await act(async () => {
    for (const [id, timer] of [...timers]) if (timer.delay <= 500) { timers.delete(id); timer.run() }
  })
}
async function remoteChange(value: unknown = { userId: 'alpha', revision: 'remote-change' }) {
  await act(async () => window.dispatchEvent(new window.StorageEvent('storage', {
    key: 'stocklab-watchlist-changed', newValue: typeof value === 'string' ? value : JSON.stringify(value),
    storageArea: window.localStorage,
  })))
}
async function mount(t: TestContext, enrich = false) {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  let state!: ReturnType<typeof useWatchlistData>
  const renders: Array<{ owner: string | undefined; symbols: string[] }> = []
  function Probe() {
    state = useWatchlistData(enrich)
    renders.push({ owner: getAuthSession()?.user.id, symbols: [...state.favorites] })
    return null
  }
  await act(async () => root.render(React.createElement(Probe)))
  let closed = false
  const close = async () => {
    if (closed) return
    closed = true; await act(async () => root.unmount()); container.remove()
  }
  t.after(close)
  return { get state() { return state }, close, renders }
}

test('a successful addition notifies other mounted views only after server confirmation', async t => {
  let persisted: WatchlistApiItem[] = []
  const get = t.mock.method(watchlistApi, 'getWatchlist', async () => [...persisted])
  const response = deferred<WatchlistApiItem>()
  t.mock.method(watchlistApi, 'addToWatchlist', () => response.promise)
  const first = await mount(t), second = await mount(t)
  let adding!: Promise<boolean>
  await act(async () => { adding = first.state.add('AAPL') })
  await flush()
  assert.equal(window.localStorage.getItem('stocklab-watchlist-changed'), null)
  assert.equal(second.state.favorites.has('AAPL'), false)
  persisted = [member('AAPL')]
  await act(async () => { response.resolve(persisted[0]); assert.equal(await adding, true) })
  await flush()
  assert.deepEqual([...first.state.favorites], ['AAPL'])
  assert.deepEqual([...second.state.favorites], ['AAPL'])
  assert.equal(get.mock.callCount(), 3, 'only the other view needs to re-fetch the confirmed addition')
  const notification = JSON.parse(window.localStorage.getItem('stocklab-watchlist-changed')!)
  assert.equal(notification.userId, 'alpha')
  assert.equal(typeof notification.revision, 'string')
  assert.equal('accessToken' in notification, false)
  assert.equal('items' in notification, false, 'the API remains the source of membership')
})

test('a confirmed removal updates another view without removing its other favorites', async t => {
  let persisted = [member('AAPL'), member('MSFT')]
  t.mock.method(watchlistApi, 'getWatchlist', async () => [...persisted])
  t.mock.method(watchlistApi, 'removeFromWatchlist', async symbol => { persisted = persisted.filter(row => row.symbol !== symbol) })
  const first = await mount(t), second = await mount(t)
  await act(async () => { assert.equal(await first.state.remove('AAPL'), true) })
  await flush()
  assert.deepEqual([...second.state.favorites], ['MSFT'])
})

test('a remote notification re-reads authoritative membership for additions and removals', async t => {
  let persisted = [member('AAPL')]
  t.mock.method(watchlistApi, 'getWatchlist', async () => persisted)
  const view = await mount(t)
  persisted = [member('MSFT')]
  await remoteChange({ userId: 'alpha', revision: 'changed', items: [member('INJECTED')] }); await flush()
  assert.deepEqual([...view.state.favorites], ['MSFT'])
  assert.equal(view.state.loading, false)
})

for (const event of ['focus', 'visibilitychange', 'pageshow']) {
  test(`${event} refreshes every hook consumer even when a mutation notification was missed`, async t => {
    let persisted = [member('AAPL')]
    t.mock.method(watchlistApi, 'getWatchlist', async () => persisted)
    const view = await mount(t)
    persisted = [member('MSFT')]
    await act(async () => {
      if (event === 'visibilitychange') document.dispatchEvent(new window.Event(event))
      else if (event === 'pageshow') window.dispatchEvent(new window.PageTransitionEvent(event, { persisted: true }))
      else window.dispatchEvent(new window.Event(event))
    })
    await flush()
    assert.deepEqual([...view.state.favorites], ['MSFT'])
  })
}

test('notification and foreground bursts coalesce without clearing known rows while the read is pending', async t => {
  const response = deferred<WatchlistApiItem[]>()
  let calls = 0
  const get = t.mock.method(watchlistApi, 'getWatchlist', () => ++calls === 1 ? Promise.resolve([member('AAPL')]) : response.promise)
  const view = await mount(t, true)
  await remoteChange(); await remoteChange()
  await act(async () => { window.dispatchEvent(new window.Event('focus')); document.dispatchEvent(new window.Event('visibilitychange')) })
  await flush()
  assert.equal(get.mock.callCount(), 2)
  assert.equal(view.state.items[0]?.price, 200)
  assert.equal(view.state.favorites.has('AAPL'), true)
  await act(async () => response.resolve([member('MSFT')]))
  assert.deepEqual([...view.state.favorites], ['MSFT'])
})

test('remote changes during a write are reconciled afterward without aborting the write', async t => {
  let persisted: WatchlistApiItem[] = []
  const get = t.mock.method(watchlistApi, 'getWatchlist', async () => [...persisted])
  const response = deferred<WatchlistApiItem>()
  let writeSignal: AbortSignal | undefined
  t.mock.method(watchlistApi, 'addToWatchlist', (_symbol, signal) => { writeSignal = signal; return response.promise })
  const view = await mount(t)
  let adding!: Promise<boolean>
  await act(async () => { adding = view.state.add('AAPL') })
  persisted = [member('MSFT')]
  await remoteChange(); await flush()
  assert.equal(writeSignal?.aborted, false)
  assert.equal(get.mock.callCount(), 1)
  persisted.push(member('AAPL'))
  await act(async () => { response.resolve(member('AAPL')); assert.equal(await adding, true) })
  await flush()
  assert.deepEqual([...view.state.favorites].sort(), ['AAPL', 'MSFT'])
  assert.equal(view.state.pendingSymbols.size, 0)
})

test('reconciliation waits until all concurrent writes have settled', async t => {
  let persisted: WatchlistApiItem[] = []
  const get = t.mock.method(watchlistApi, 'getWatchlist', async () => [...persisted])
  const aapl = deferred<WatchlistApiItem>(), nvda = deferred<WatchlistApiItem>()
  t.mock.method(watchlistApi, 'addToWatchlist', symbol => symbol === 'AAPL' ? aapl.promise : nvda.promise)
  const view = await mount(t)
  let addingAapl!: Promise<boolean>, addingNvda!: Promise<boolean>
  await act(async () => { addingAapl = view.state.add('AAPL'); addingNvda = view.state.add('NVDA') })
  persisted = [member('MSFT')]
  await remoteChange(); await flush()
  persisted.push(member('AAPL'))
  await act(async () => { aapl.resolve(member('AAPL')); await addingAapl })
  await flush()
  assert.equal(get.mock.callCount(), 1)
  assert.equal(view.state.pendingSymbols.has('NVDA'), true)
  persisted.push(member('NVDA'))
  await act(async () => { nvda.resolve(member('NVDA')); await addingNvda })
  await flush()
  assert.deepEqual([...view.state.favorites].sort(), ['AAPL', 'MSFT', 'NVDA'])
})

test('an outdated membership request cannot overwrite a later notification', async t => {
  const old = deferred<WatchlistApiItem[]>()
  let calls = 0, oldSignal: AbortSignal | undefined
  t.mock.method(watchlistApi, 'getWatchlist', signal => {
    if (++calls === 1) { oldSignal = signal; return old.promise }
    return Promise.resolve([member('MSFT')])
  })
  const view = await mount(t)
  await remoteChange(); await flush()
  assert.equal(oldSignal?.aborted, true)
  assert.deepEqual([...view.state.favorites], ['MSFT'])
  await act(async () => old.resolve([member('AAPL')]))
  assert.deepEqual([...view.state.favorites], ['MSFT'])
})

test('hidden views defer remote updates until they become visible', async t => {
  let persisted = [member('AAPL')]
  const get = t.mock.method(watchlistApi, 'getWatchlist', async () => persisted)
  const view = await mount(t)
  visibility = 'hidden'; persisted = [member('MSFT')]
  await remoteChange(); await flush()
  assert.equal(get.mock.callCount(), 1)
  visibility = 'visible'
  await act(async () => document.dispatchEvent(new window.Event('visibilitychange'))); await flush()
  assert.deepEqual([...view.state.favorites], ['MSFT'])
})

test('malformed messages and messages for another account do not refresh the current account', async t => {
  const get = t.mock.method(watchlistApi, 'getWatchlist', async () => [member('AAPL')])
  const view = await mount(t)
  for (const message of ['invalid json', null, [], { userId: 42 }, { userId: 'beta', revision: 'other' }]) {
    await remoteChange(message)
  }
  await flush()
  assert.equal(get.mock.callCount(), 1)
  assert.deepEqual([...view.state.favorites], ['AAPL'])
})

test('a failed mutation emits no change notification and keeps the actual error', async t => {
  t.mock.method(watchlistApi, 'getWatchlist', async () => [member('AAPL')])
  t.mock.method(watchlistApi, 'removeFromWatchlist', async () => { throw new WatchlistApiError(503, 'server_error') })
  const view = await mount(t)
  await act(async () => { assert.equal(await view.state.remove('AAPL'), false) })
  assert.equal(window.localStorage.getItem('stocklab-watchlist-changed'), null)
  assert.deepEqual([...view.state.favorites], ['AAPL'])
  assert.equal(view.state.mutationError, 'server_error')
})

test('blocked notification storage cannot turn a confirmed change into a failure', async t => {
  let persisted: WatchlistApiItem[] = []
  t.mock.method(watchlistApi, 'getWatchlist', async () => persisted)
  t.mock.method(watchlistApi, 'addToWatchlist', async symbol => { persisted = [member(symbol)]; return persisted[0] })
  const first = await mount(t), second = await mount(t)
  t.mock.method(window.Storage.prototype, 'setItem', () => { throw new Error('storage unavailable') })
  await act(async () => { assert.equal(await first.state.add('AAPL'), true) }); await flush()
  assert.deepEqual([...first.state.favorites], ['AAPL'])
  assert.deepEqual([...second.state.favorites], ['AAPL'])
  assert.equal(first.state.mutationError, null)
})

test('a queued notification cannot cross an account replacement or logout', async t => {
  const owners: string[] = []
  t.mock.method(watchlistApi, 'getWatchlist', async () => {
    const id = getAuthSession()!.user.id; owners.push(id); return [member(id === 'alpha' ? 'AAPL' : 'MSFT')]
  })
  const view = await mount(t)
  await remoteChange()
  await act(async () => saveAuthSession(session('beta'))); await flush()
  assert.deepEqual(owners, ['alpha', 'beta'])
  assert.deepEqual([...view.state.favorites], ['MSFT'])
  await remoteChange({ userId: 'alpha', revision: 'late-alpha' }); await flush()
  assert.deepEqual(owners, ['alpha', 'beta'])
  await remoteChange({ userId: 'beta', revision: 'beta' })
  await act(async () => clearAuthSession()); await flush()
  assert.equal(view.state.error, 'unauthorized')
  assert.equal(view.state.items.length, 0)
})

test('unmount cancels queued refreshes and removes change listeners', async t => {
  const get = t.mock.method(watchlistApi, 'getWatchlist', async () => [])
  const view = await mount(t)
  await remoteChange(); await view.close(); await flush()
  await remoteChange(); await flush()
  assert.equal(get.mock.callCount(), 1)
})

test('an already rendered add handler cannot start a write during a background read', async t => {
  const response = deferred<WatchlistApiItem[]>()
  let calls = 0
  t.mock.method(watchlistApi, 'getWatchlist', () => ++calls === 1 ? Promise.resolve([member('AAPL')]) : response.promise)
  const post = t.mock.method(watchlistApi, 'addToWatchlist', async symbol => member(symbol))
  const view = await mount(t)
  const renderedAdd = view.state.add
  await remoteChange(); await flush()
  await act(async () => { assert.equal(await renderedAdd('NVDA'), false) })
  assert.equal(post.mock.callCount(), 0)
  await act(async () => response.resolve([member('MSFT')]))
  assert.deepEqual([...view.state.favorites], ['MSFT'])
})

test('a queued notification does not replay under a rotated token', async t => {
  const tokens: string[] = []
  t.mock.method(watchlistApi, 'getWatchlist', async () => { tokens.push(getAuthSession()!.accessToken); return [member('AAPL')] })
  await mount(t)
  await remoteChange()
  await act(async () => saveAuthSession(session('alpha', 'rotated'))); await flush()
  assert.deepEqual(tokens, ['alpha', 'rotated'])
})

test('failed revalidation is explicit and the next notification can recover', async t => {
  let fail = false
  t.mock.method(watchlistApi, 'getWatchlist', async () => {
    if (fail) throw new WatchlistApiError(503, 'server_error')
    return [member('AAPL')]
  })
  const view = await mount(t)
  fail = true
  await remoteChange(); await flush()
  assert.equal(view.state.loading, false)
  assert.equal(view.state.error, 'server_error')
  fail = false
  await remoteChange(); await flush()
  assert.equal(view.state.error, null)
  assert.deepEqual([...view.state.favorites], ['AAPL'])
})

test('late quote enrichment cannot restore a remotely removed favorite', async t => {
  let persisted = [member('AAPL')]
  t.mock.method(watchlistApi, 'getWatchlist', async () => persisted)
  const response = deferred<ReturnType<typeof quote>>()
  t.mock.method(marketDataApi, 'quote', () => response.promise)
  const view = await mount(t, true)
  persisted = []
  await remoteChange(); await flush()
  assert.deepEqual([...view.state.favorites], [])
  await act(async () => response.resolve(quote()))
  assert.deepEqual([...view.state.favorites], [])
  assert.equal(view.state.loading, false)
})

test('reconciling a remote change after a failed write preserves the write error', async t => {
  let persisted = [member('AAPL')]
  t.mock.method(watchlistApi, 'getWatchlist', async () => persisted)
  const response = deferred<void>()
  t.mock.method(watchlistApi, 'removeFromWatchlist', async () => { await response.promise; throw new WatchlistApiError(0, 'offline') })
  const view = await mount(t)
  let removing!: Promise<boolean>
  await act(async () => { removing = view.state.remove('AAPL') })
  persisted = [member('AAPL'), member('MSFT')]
  await remoteChange(); await flush()
  await act(async () => { response.resolve(); assert.equal(await removing, false) }); await flush()
  assert.deepEqual([...view.state.favorites], ['AAPL', 'MSFT'])
  assert.equal(view.state.mutationError, 'offline')
})

test('account replacement after a background load never renders the former owner membership', async t => {
  const beta = deferred<WatchlistApiItem[]>()
  t.mock.method(watchlistApi, 'getWatchlist', () => getAuthSession()?.user.id === 'beta' ? beta.promise : Promise.resolve([member('AAPL')]))
  const view = await mount(t)
  await remoteChange(); await flush()
  await act(async () => saveAuthSession(session('beta')))
  assert.ok(view.renders.filter(render => render.owner === 'beta').every(render => !render.symbols.includes('AAPL')))
  await act(async () => beta.resolve([member('MSFT')]))
  assert.deepEqual([...view.state.favorites], ['MSFT'])
})
