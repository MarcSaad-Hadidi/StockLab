import assert from 'node:assert/strict'
import { beforeEach, test, type TestContext } from 'node:test'
import React, { act } from 'react'
import { createRoot } from 'react-dom/client'
import { JSDOM } from 'jsdom'
import { portfolioApi, PortfolioApiError, type PortfolioApiResponse } from '../src/api/portfolioApi.ts'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { clearAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'
import { usePortfolioData, type PortfolioDataState } from '../src/portfolio/usePortfolioData.ts'

const dom = new JSDOM('', { url: 'http://localhost/portfolio', pretendToBeVisual: true })
Object.assign(globalThis, { window: dom.window, document: dom.window.document,
  IS_REACT_ACT_ENVIRONMENT: true })
let visibility = 'visible'
Object.defineProperty(document, 'visibilityState', { get: () => visibility })
const session = (id = 'alpha', token = id) => ({ accessToken: token, tokenType: 'Bearer' as const,
  expiresAtUtc: '2099-01-01T00:00:00Z', user: { id, displayName: id, email: `${id}@example.test` } })
const empty = { cashBalance: '100000', initialCapital: '100000', investedValue: '0',
  totalValue: '100000', currency: 'USD', positions: [] }
const bought = { ...empty, cashBalance: '99897.75', investedValue: '102.25',
  positions: [{ symbol: 'AAPL', quantity: '0.5', averageCost: '204.5' }] }
const trade = { id: 'purchase', side: 'BUY' as const, symbol: 'AAPL', quantity: '0.5',
  executionPrice: '204.5', totalAmount: '102.25', executedAtUtc: '2026-11-10T02:00:00Z' }
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
beforeEach(() => { visibility = 'visible'; window.localStorage.clear(); saveAuthSession(session()) })

async function mount(t: TestContext) {
  let nextTimer = 0
  const timers = new Map<number, { delay: number; run: () => void }>()
  t.mock.method(window, 'setTimeout', (callback, delay = 0) => {
    const id = ++nextTimer
    timers.set(id, { delay, run: callback as () => void })
    return id
  })
  t.mock.method(window, 'clearTimeout', id => { timers.delete(id!) })
  t.mock.method(marketDataApi, 'quote', async () => ({ symbol: 'AAPL', name: 'Apple Inc.',
    exchange: 'NASDAQ', currency: 'USD', price: 204.5, priceDecimal: '204.5', change: 1, changePercent: 0.49,
    source: 'Mock', asOfUtc: '2026-08-28T20:00:00Z', receivedAtUtc: '2026-11-10T02:00:00Z',
    volume: null, open: null, high: null, low: null, previousClose: null, averageVolume: null,
    isMarketOpen: false, fiftyTwoWeek: null }))
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  let state!: PortfolioDataState
  function Probe() { state = usePortfolioData(); return null }
  await act(async () => root.render(React.createElement(Probe)))
  let closed = false
  const close = async () => {
    if (closed) return
    closed = true
    await act(async () => root.unmount()); container.remove()
  }
  t.after(close)
  return {
    get state() { return state }, close,
    async event(type: 'focus' | 'visibilitychange' | 'pageshow', persisted = false) {
      await act(async () => {
        if (type === 'visibilitychange') document.dispatchEvent(new window.Event(type))
        else if (type === 'pageshow') window.dispatchEvent(new window.PageTransitionEvent(type, { persisted }))
        else window.dispatchEvent(new window.Event(type))
      })
    },
    async flush() {
      await act(async () => {
        // Run short UI timers, leaving session-expiration timers untouched.
        for (const [id, timer] of [...timers]) if (timer.delay <= 500) {
          timers.delete(id); timer.run()
        }
      })
    },
  }
}

test('returning to the tab refreshes cash, positions and recent transactions after a remote trade', async t => {
  let changed = false
  t.mock.method(portfolioApi, 'getPortfolio', async () => changed ? bought : empty)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => changed ? [trade] : [])
  const view = await mount(t)
  assert.equal(view.state.data?.cashBalance, '100000')
  assert.deepEqual(view.state.data?.transactions, [])
  changed = true
  await view.event('focus'); await view.flush()
  assert.equal(view.state.data?.cashBalance, '99897.75')
  assert.equal(view.state.data?.positions[0].quantity, '0.5')
  assert.equal(view.state.data?.positions[0].marketValue, '102.25')
  assert.equal(view.state.data?.totalValue, '100000')
  assert.deepEqual(view.state.data?.transactions.map(item => item.id), ['purchase'])
})

test('visibility and focus bursts produce one refresh with unchanged financial values', async t => {
  const portfolio = t.mock.method(portfolioApi, 'getPortfolio', async () => bought)
  const recent = t.mock.method(portfolioApi, 'getRecentTransactions', async () => [trade])
  const view = await mount(t)
  const previous = view.state.data
  await view.event('visibilitychange'); await view.event('focus'); await view.event('focus')
  assert.equal(view.state.data, previous)
  await view.flush()
  assert.equal(portfolio.mock.callCount(), 2)
  assert.equal(recent.mock.callCount(), 2)
  assert.equal(view.state.data?.cashBalance, '99897.75')
  assert.deepEqual(view.state.data?.transactions, [trade])
  assert.equal(view.state.isLoading, false)
})

test('hidden tabs wait until visible to refresh', async t => {
  const portfolio = t.mock.method(portfolioApi, 'getPortfolio', async () => empty)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  const view = await mount(t)
  visibility = 'hidden'
  await view.event('focus'); await view.event('visibilitychange'); await view.flush()
  assert.equal(portfolio.mock.callCount(), 1)
  visibility = 'visible'
  await view.event('visibilitychange'); await view.flush()
  assert.equal(portfolio.mock.callCount(), 2)
})

test('a restored page refreshes, whereas the normal initial pageshow does not duplicate loading', async t => {
  const portfolio = t.mock.method(portfolioApi, 'getPortfolio', async () => empty)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  const view = await mount(t)
  await view.event('pageshow'); await view.flush()
  assert.equal(portfolio.mock.callCount(), 1)
  await view.event('pageshow', true); await view.flush()
  assert.equal(portfolio.mock.callCount(), 2)
})

test('returning during an older portfolio request replaces it instead of losing the refresh', async t => {
  const old = deferred<PortfolioApiResponse>()
  let oldSignal: AbortSignal | undefined, calls = 0
  t.mock.method(portfolioApi, 'getPortfolio', signal => {
    if (++calls === 1) { oldSignal = signal; return old.promise }
    return Promise.resolve(bought)
  })
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [trade])
  const view = await mount(t)
  await view.event('focus'); await view.flush()
  assert.equal(oldSignal?.aborted, true)
  assert.equal(view.state.data?.cashBalance, '99897.75')
  await act(async () => old.resolve(empty))
  assert.equal(view.state.data?.cashBalance, '99897.75')
  assert.deepEqual(view.state.data?.transactions, [trade])
})

test('a late recent-transactions response cannot undo the refreshed activity', async t => {
  const old = deferred<typeof trade[]>()
  let oldSignal: AbortSignal | undefined, calls = 0
  t.mock.method(portfolioApi, 'getPortfolio', async () => bought)
  t.mock.method(portfolioApi, 'getRecentTransactions', (_count, signal) => {
    if (++calls === 1) { oldSignal = signal; return old.promise }
    return Promise.resolve([trade])
  })
  const view = await mount(t)
  await view.event('focus'); await view.flush()
  assert.equal(oldSignal?.aborted, true)
  assert.deepEqual(view.state.data?.transactions, [trade])
  await act(async () => old.resolve([]))
  assert.deepEqual(view.state.data?.transactions, [trade])
})

test('a failed background portfolio refresh retains the last data with an error and retry recovers', async t => {
  let fail = false
  t.mock.method(portfolioApi, 'getPortfolio', async () => {
    if (fail) throw new PortfolioApiError(503, 'server_error', 'unavailable')
    return bought
  })
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [trade])
  const view = await mount(t)
  fail = true
  await view.event('focus'); await view.flush()
  assert.equal(view.state.data?.cashBalance, '99897.75')
  assert.equal(view.state.error?.code, 'server_error')
  fail = false
  await act(async () => view.state.retry())
  assert.equal(view.state.error, null)
  assert.equal(view.state.data?.cashBalance, '99897.75')
})

test('queued foreground work cannot request data with the previous account or token', async t => {
  const authorizations: string[] = []
  t.mock.method(portfolioApi, 'getPortfolio', async (_signal, authorization) => {
    authorizations.push(authorization!)
    return { ...empty, cashBalance: authorization === 'Bearer alpha' ? '100000' : '2222' }
  })
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  const view = await mount(t)
  await view.event('focus')
  await act(async () => saveAuthSession(session('beta')))
  await view.flush()
  assert.deepEqual(authorizations, ['Bearer alpha', 'Bearer beta'])
  assert.equal(view.state.data?.cashBalance, '2222')
  await view.event('focus')
  await act(async () => clearAuthSession())
  await view.flush()
  assert.deepEqual(authorizations, ['Bearer alpha', 'Bearer beta'])
  assert.equal(view.state.data, null)
})

test('unmounting cancels queued foreground work and removes its listeners', async t => {
  const portfolio = t.mock.method(portfolioApi, 'getPortfolio', async () => empty)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  const view = await mount(t)
  await view.event('focus')
  await view.close(); await view.flush()
  await view.event('focus'); await view.event('visibilitychange'); await view.flush()
  assert.equal(portfolio.mock.callCount(), 1)
})
