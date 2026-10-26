import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { watchlistApi, WatchlistApiError } from '../src/api/watchlistApi.ts'
import { clearAuthSession, getAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/components/charts/FinancialLineChart')) return { format: 'module', shortCircuit: true, source: 'export function FinancialLineChart() { return null }' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/market?symbol=AAPL' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { StockDetailsPage } = await import('../src/market/StockDetailsPage.tsx')
const session = { accessToken: 'account-a-token', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Account A', email: 'a@example.test' } }
const member = (symbol: string) => ({ symbol, createdAtUtc: '2026-10-26T12:00:00Z' })
let persisted: ReturnType<typeof member>[]
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
beforeEach(async t => {
  window.localStorage.clear(); saveAuthSession(session); persisted = [member('AAPL')]
  await i18n.changeLanguage('en')
  t.mock.method(marketDataApi, 'quote', async symbol => ({ symbol, name: symbol, exchange: 'NASDAQ', currency: 'USD', price: 100,
    priceDecimal: '100', change: null, changePercent: null, volume: null, asOfUtc: '2026-10-26T12:00:00Z', open: null,
    high: null, low: null, previousClose: null, averageVolume: null, isMarketOpen: false, fiftyTwoWeek: null }))
  t.mock.method(marketDataApi, 'history', async () => { throw new Error('test history unavailable') })
  t.mock.method(watchlistApi, 'getWatchlist', async () => [...persisted])
  t.mock.method(watchlistApi, 'addToWatchlist', async symbol => { const item = member(symbol); persisted.push(item); return item })
  t.mock.method(watchlistApi, 'removeFromWatchlist', async symbol => { persisted = persisted.filter(item => item.symbol !== symbol) })
})
async function mount(symbol = 'AAPL') {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  const render = async (requestedSymbol: string) => {
    await act(async () => root.render(React.createElement(StockDetailsPage, { requestedSymbol, onBack() {} })))
  }
  await render(symbol)
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 25)) })
  return { container, render,
    buttons: () => [...container.querySelectorAll<HTMLButtonElement>('.stock-title-star, .stock-details-actions button:first-child')],
    feedback: () => container.querySelector('.stock-watchlist-feedback'),
    async close() { await act(async () => root.unmount()); container.remove() } }
}
function selected(view: Awaited<ReturnType<typeof mount>>, value: boolean) {
  assert.equal(view.buttons().length, 2)
  for (const button of view.buttons()) { assert.equal(button.getAttribute('aria-pressed'), String(value)); assert.equal(button.disabled, false) }
}

for (const language of ['en', 'fr']) {
  test(`both controls reflect persisted membership and toggle together in ${language}`, async t => {
    await i18n.changeLanguage(language)
    const quotes = t.mock.method(marketDataApi, 'quote', async () => { throw new Error('quote unavailable') })
    const view = await mount(' aapl ')
    try {
      selected(view, true)
      assert.match(view.buttons()[0].getAttribute('aria-label')!, language === 'en' ? /Remove AAPL/ : /Retirer AAPL/)
      assert.match(view.buttons()[1].textContent!, language === 'en' ? /In Watchlist/ : /Dans la liste de suivi/)
      assert.ok(view.buttons()[0].classList.contains('stock-title-star-active'))
      await act(async () => view.buttons()[0].click())
      selected(view, false)
      assert.deepEqual(persisted, [])
      await act(async () => view.buttons()[1].click())
      selected(view, true)
      assert.deepEqual(persisted.map(item => item.symbol), ['AAPL'])
      assert.equal(quotes.mock.callCount(), 1, 'watchlist membership must not trigger its own quote enrichment')
    } finally { await view.close() }
  })
}

test('unknown membership disables both controls and is never announced as not followed', async t => {
  const response = deferred<ReturnType<typeof member>[]>()
  t.mock.method(watchlistApi, 'getWatchlist', () => response.promise)
  const view = await mount()
  try {
    for (const button of view.buttons()) { assert.equal(button.disabled, true); assert.equal(button.getAttribute('aria-pressed'), null) }
    assert.match(view.feedback()!.textContent!, /Loading watchlist/)
    await act(async () => response.resolve([member('AAPL')]))
    selected(view, true)
  } finally { await view.close() }
})

for (const operation of ['add', 'remove'] as const) {
  test(`a pending ${operation} disables both controls, deduplicates clicks and waits for server confirmation`, async t => {
    persisted = operation === 'add' ? [] : [member('AAPL')]
    const response = deferred<ReturnType<typeof member>>()
    let signal!: AbortSignal
    const mutation = operation === 'add'
      ? t.mock.method(watchlistApi, 'addToWatchlist', (_symbol, incoming) => { signal = incoming!; return response.promise })
      : t.mock.method(watchlistApi, 'removeFromWatchlist', async (_symbol, incoming) => { signal = incoming!; await response.promise })
    const get = t.mock.method(watchlistApi, 'getWatchlist', async () => persisted)
    const view = await mount()
    try {
      const buttons = view.buttons()
      await act(async () => { buttons[0].click(); buttons[1].click() })
      assert.equal(mutation.mock.callCount(), 1)
      for (const button of view.buttons()) {
        assert.equal(button.disabled, true)
        assert.equal(button.getAttribute('aria-pressed'), String(operation === 'remove'))
      }
      await act(async () => window.dispatchEvent(new dom.window.Event('focus')))
      assert.equal(get.mock.callCount(), 1, 'focus must not cancel a pending mutation')
      assert.equal(signal.aborted, false)
      await act(async () => response.resolve(member('AAPL')))
      selected(view, operation === 'add')
    } finally { await view.close() }
  })
}

for (const language of ['en', 'fr']) for (const operation of ['add', 'remove'] as const) {
  test(`a rejected ${operation} retains membership and shows the actual failure in ${language}`, async t => {
    await i18n.changeLanguage(language)
    persisted = operation === 'add' ? [] : [member('AAPL')]
    t.mock.method(watchlistApi, operation === 'add' ? 'addToWatchlist' : 'removeFromWatchlist', async () => { throw new WatchlistApiError(503, 'server_error') })
    const view = await mount()
    try {
      await act(async () => view.buttons()[0].click())
      selected(view, operation === 'remove')
      assert.match(view.feedback()!.querySelector('[role="alert"]')!.textContent!, language === 'en' ? /could not be loaded or updated/ : /Impossible de charger ou de modifier votre liste de suivi/)
      assert.deepEqual(persisted.map(item => item.symbol), operation === 'add' ? [] : ['AAPL'])
    } finally { await view.close() }
  })
}

test('failed membership offers retry and enables controls only after membership is known', async t => {
  let calls = 0
  const response = deferred<ReturnType<typeof member>[]>()
  t.mock.method(watchlistApi, 'getWatchlist', async () => { if (++calls === 1) throw new WatchlistApiError(0, 'offline'); return response.promise })
  const view = await mount()
  try {
    assert.match(view.feedback()!.textContent!, /Check your connection/)
    assert.ok(view.buttons().every(button => button.disabled && !button.hasAttribute('aria-pressed')))
    await act(async () => (view.feedback()!.querySelector('button') as HTMLButtonElement).click())
    assert.match(view.feedback()!.textContent!, /Loading watchlist/)
    await act(async () => response.resolve([member('AAPL')]))
    selected(view, true)
    assert.equal(view.feedback()!.querySelector('[role="alert"]'), null)
  } finally { await view.close() }
})

test('anonymous Stock Details offers sign in and sends no watchlist request', async t => {
  clearAuthSession()
  const get = t.mock.method(watchlistApi, 'getWatchlist', async () => persisted)
  const view = await mount()
  try {
    assert.equal(get.mock.callCount(), 0)
    assert.ok(view.buttons().every(button => button.disabled && !button.hasAttribute('aria-pressed')))
    assert.ok(view.feedback()!.querySelector('a[href="/login"]'))
    assert.equal(view.feedback()!.querySelector('button'), null)
  } finally { await view.close() }
})

for (const operation of ['add', 'remove'] as const) {
  test(`${operation} conflicts reconcile with persisted membership instead of guessing success`, async t => {
    let reads = 0
    t.mock.method(watchlistApi, 'getWatchlist', async () => (++reads === 1) === (operation === 'remove') ? [member('AAPL')] : [])
    t.mock.method(watchlistApi, operation === 'add' ? 'addToWatchlist' : 'removeFromWatchlist', async () => { throw new WatchlistApiError(operation === 'add' ? 409 : 404, operation === 'add' ? 'already_exists' : 'not_found') })
    const view = await mount()
    try {
      await act(async () => view.buttons()[0].click())
      assert.equal(reads, 2)
      selected(view, operation === 'add')
      assert.equal(view.feedback()!.querySelector('[role="alert"]'), null)
    } finally { await view.close() }
  })
}

for (const status of [200, 401]) {
  test(`a late mutation response (${status}) cannot change another account's controls or invalidate its session`, async t => {
    const response = deferred<void>()
    let signal!: AbortSignal
    t.mock.method(watchlistApi, 'getWatchlist', async () => getAuthSession()?.user.id === 'account-a' ? [member('AAPL')] : [])
    t.mock.method(watchlistApi, 'removeFromWatchlist', async (_symbol, incoming) => { signal = incoming!; await response.promise; if (status === 401) throw new WatchlistApiError(401, 'unauthorized') })
    const view = await mount()
    try {
      await act(async () => view.buttons()[0].click())
      await act(async () => saveAuthSession({ ...session, accessToken: 'account-b-token', user: { ...session.user, id: 'account-b' } }))
      assert.equal(signal.aborted, true)
      await act(async () => response.resolve())
      selected(view, false)
      assert.equal(getAuthSession()?.accessToken, 'account-b-token')
      assert.equal(view.feedback()!.querySelector('[role="alert"]'), null)
    } finally { await view.close() }
  })
}

test('a failure for the previous symbol does not appear on the new symbol or block its next operation', async t => {
  const response = deferred<void>()
  t.mock.method(watchlistApi, 'removeFromWatchlist', async () => { await response.promise; throw new WatchlistApiError(503, 'server_error') })
  const view = await mount()
  try {
    await act(async () => view.buttons()[0].click())
    await view.render('NVDA')
    assert.ok(view.buttons().every(button => button.disabled), 'the previous in-flight operation must finish before another can start')
    await act(async () => response.resolve())
    selected(view, false)
    assert.equal(view.feedback()!.querySelector('[role="alert"]'), null)
    await act(async () => view.buttons()[1].click())
    selected(view, true)
    assert.equal(view.feedback()!.querySelector('[role="alert"]'), null)
  } finally { await view.close() }
})

for (const trigger of ['focus', 'visibility', 'bfcache'] as const) {
  test(`returning through ${trigger} reloads membership changed elsewhere`, async t => {
    const view = await mount()
    try {
      selected(view, true)
      persisted = []
      await act(async () => {
        if (trigger === 'focus') window.dispatchEvent(new dom.window.Event('focus'))
        if (trigger === 'visibility') { t.mock.getter(document, 'visibilityState', () => 'visible'); document.dispatchEvent(new dom.window.Event('visibilitychange')) }
        if (trigger === 'bfcache') window.dispatchEvent(new dom.window.PageTransitionEvent('pageshow', { persisted: true }))
      })
      selected(view, false)
    } finally { await view.close() }
  })
}

test('expiry clears selected membership without focus and removes its session timers on unmount', async t => {
  let now = Date.parse('2026-10-26T12:00:00Z')
  let nextId = 0
  const timers = new Map<number, () => void>()
  t.mock.method(Date, 'now', () => now)
  t.mock.method(window, 'setTimeout', callback => { const id = ++nextId; timers.set(id, callback as () => void); return id })
  t.mock.method(window, 'clearTimeout', id => { timers.delete(id!) })
  saveAuthSession({ ...session, expiresAtUtc: new Date(now + 5000).toISOString() })
  const view = await mount()
  try {
    selected(view, true)
    now += 5000
    await act(async () => { for (const [id, run] of [...timers]) { timers.delete(id); run() } })
    assert.equal(getAuthSession(), null)
    assert.ok(view.buttons().every(button => button.disabled && !button.hasAttribute('aria-pressed')))
    assert.ok(view.feedback()!.querySelector('a[href="/login"]'))
  } finally { await view.close() }
  assert.equal(timers.size, 0)
})
