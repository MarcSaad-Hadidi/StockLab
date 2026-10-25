import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { portfolioApi } from '../src/api/portfolioApi.ts'
import { watchlistApi, WatchlistApiError } from '../src/api/watchlistApi.ts'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { clearAuthSession, getAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/dashboard' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { DashboardPage } = await import('../src/dashboard/DashboardPage.tsx')
const session = { accessToken: 'account-a-token', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Account A', email: 'a@example.test' } }
const member = (symbol: string) => ({ symbol, createdAtUtc: '2026-10-24T12:00:00Z' })
const quote = (symbol: string) => ({ symbol, name: symbol === 'AAPL' ? 'Apple Inc.' : 'Microsoft Corporation', exchange: 'NASDAQ',
  price: 140.25, priceDecimal: '140.25', change: -3, changePercent: -2.5, currency: 'CAD', volume: null,
  asOfUtc: '2026-10-24T12:00:00Z', open: null, high: null, low: null, previousClose: null, averageVolume: null, isMarketOpen: null, fiftyTwoWeek: null })
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
beforeEach(async t => {
  window.localStorage.clear(); saveAuthSession(session)
  await i18n.changeLanguage('en')
  t.mock.method(portfolioApi, 'getPortfolio', async () => ({ cashBalance: '1000', initialCapital: '1000', investedValue: '0', totalValue: '1000', currency: 'USD', positions: [] }))
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  t.mock.method(watchlistApi, 'getWatchlist', async () => [member('AAPL')])
  t.mock.method(marketDataApi, 'quote', async symbol => quote(symbol))
})
async function mount() {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(DashboardPage)))
  return { container, card: () => container.querySelector('[aria-labelledby="watchlist-title"]')!,
    async close() { await act(async () => root.unmount()); container.remove() } }
}
function filter(view: Awaited<ReturnType<typeof mount>>, value: string) {
  const input = view.card().querySelector('input')!
  Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(input, value)
  input.dispatchEvent(new dom.window.Event('input', { bubbles: true }))
}

for (const language of ['en', 'fr']) {
  test(`Dashboard displays persisted symbols, quote currency and percentage, and usable links in ${language}`, async () => {
    await i18n.changeLanguage(language)
    const view = await mount()
    try {
      assert.match(view.card().textContent!, /AAPL/)
      assert.match(view.card().textContent!, /Apple Inc\./)
      assert.match(view.card().textContent!, language === 'en' ? /CA\$140\.25/ : /140,25\s+\$CA/)
      assert.match(view.card().textContent!, language === 'en' ? /-2\.50%/ : /-2,50\s*%/)
      assert.equal(view.card().querySelector('.watchlist-price span')?.className, 'negative')
      assert.equal(view.card().querySelector('.row-more')?.getAttribute('href'), '/market?symbol=AAPL')
      assert.equal(view.card().querySelector('.add-watchlist')?.getAttribute('href'), '/market')
      assert.equal(view.card().querySelector('.empty-state'), null)
    } finally { await view.close() }
  })
}

test('membership loading is not an empty watchlist and does not block other Dashboard cards', async t => {
  const response = deferred<ReturnType<typeof member>[]>()
  t.mock.method(watchlistApi, 'getWatchlist', () => response.promise)
  const view = await mount()
  try {
    assert.match(view.card().querySelector('[role="status"]')!.textContent!, /Loading watchlist/)
    assert.equal(view.card().getAttribute('aria-busy'), 'true')
    assert.equal(view.card().querySelector('.empty-state'), null)
    assert.match(view.container.querySelector('.metrics-grid')!.textContent!, /\$1,000\.00/)
    await act(async () => response.resolve([member('AAPL')]))
    assert.match(view.card().textContent!, /Apple Inc\./)
    assert.equal(view.card().getAttribute('aria-busy'), 'false')
  } finally { await view.close() }
})

test('slow or missing quotes keep persisted symbols visible and never invent USD prices', async t => {
  t.mock.method(watchlistApi, 'getWatchlist', async () => [member('AAPL'), member('NOQUOTE')])
  const response = deferred<ReturnType<typeof quote>>()
  t.mock.method(marketDataApi, 'quote', symbol => {
    if (symbol === 'NOQUOTE') throw new Error('quote unavailable')
    return response.promise
  })
  const view = await mount()
  try {
    assert.equal(view.card().querySelectorAll('.watchlist-row').length, 2)
    assert.match(view.card().textContent!, /AAPL|NOQUOTE/)
    assert.equal(view.card().querySelector('.empty-state'), null)
    await act(async () => response.resolve(quote('AAPL')))
    const missing = [...view.card().querySelectorAll('.watchlist-row')].find(row => row.textContent?.includes('NOQUOTE'))!
    assert.equal(missing.querySelector('.watchlist-price strong')?.textContent, '—')
    assert.equal(missing.querySelector('.watchlist-price span')?.textContent, '—')
    assert.equal(view.card().querySelector('[role="alert"]'), null)
  } finally { await view.close() }
})

for (const language of ['en', 'fr']) {
  test(`failed membership loads offer a working retry and never show a false empty state in ${language}`, async t => {
    await i18n.changeLanguage(language)
    let calls = 0
    const response = deferred<ReturnType<typeof member>[]>()
    t.mock.method(watchlistApi, 'getWatchlist', async () => {
      calls++
      if (calls <= 2) throw new WatchlistApiError(503, 'server_error')
      return response.promise
    })
    const view = await mount()
    try {
      const retry = () => view.card().querySelector('.watchlist-feedback button') as HTMLButtonElement
      assert.ok(view.card().querySelector('[role="alert"]'))
      assert.equal(retry().textContent, language === 'en' ? 'Retry' : 'Réessayer')
      assert.equal(view.card().querySelector('.empty-state'), null)
      await act(async () => retry().click())
      assert.ok(view.card().querySelector('[role="alert"]'), 'a second failure must remain visible')
      await act(async () => { retry().click(); retry().click() })
      assert.equal(calls, 3)
      assert.match(view.card().querySelector('[role="status"]')!.textContent!, language === 'en' ? /Loading watchlist/ : /Chargement/)
      await act(async () => response.resolve([member('MSFT')]))
      assert.match(view.card().textContent!, /MSFT/)
      assert.equal(view.card().querySelector('[role="alert"]'), null)
    } finally { await view.close() }
  })
}

test('only confirmed empty membership shows the empty message; unmatched filters have their own message', async t => {
  const get = t.mock.method(watchlistApi, 'getWatchlist', async () => [])
  let view = await mount()
  try { assert.match(view.card().querySelector('.empty-state')!.textContent!, /No watchlist items/) }
  finally { await view.close() }
  get.mock.restore()
  view = await mount()
  try {
    await act(async () => filter(view, 'does-not-exist'))
    assert.match(view.card().querySelector('.empty-state')!.textContent!, /No stocks match/)
    assert.doesNotMatch(view.card().textContent!, /No watchlist items/)
    await act(async () => filter(view, ' apple '))
    assert.match(view.card().textContent!, /AAPL/)
    assert.equal(view.card().querySelector('.empty-state'), null)
  } finally { await view.close() }
})

test('anonymous Dashboard offers sign in without fetching membership or showing an empty list', async t => {
  clearAuthSession()
  const get = t.mock.method(watchlistApi, 'getWatchlist', async () => [member('AAPL')])
  const view = await mount()
  try {
    assert.equal(get.mock.callCount(), 0)
    assert.ok(view.card().querySelector('a[href="/login"]'))
    assert.equal(view.card().querySelector('.watchlist-feedback button'), null)
    assert.equal(view.card().querySelector('.empty-state'), null)
  } finally { await view.close() }
})

test('logout immediately removes Dashboard watchlist rows and a later login loads the current account', async t => {
  t.mock.method(watchlistApi, 'getWatchlist', async () => [member(getAuthSession()?.user.id === 'account-a' ? 'AAPL' : 'MSFT')])
  const view = await mount()
  try {
    assert.match(view.card().textContent!, /AAPL/)
    await act(async () => clearAuthSession())
    assert.doesNotMatch(view.card().textContent!, /AAPL|Apple/)
    assert.ok(view.card().querySelector('a[href="/login"]'))
    await act(async () => saveAuthSession({ ...session, accessToken: 'account-b-token', user: { ...session.user, id: 'account-b' } }))
    assert.match(view.card().textContent!, /MSFT/)
    assert.doesNotMatch(view.card().textContent!, /AAPL/)
  } finally { await view.close() }
})

for (const status of [200, 401]) {
  test(`a late membership response (${status}) cannot replace another account's Dashboard list or session`, async t => {
    const response = deferred<ReturnType<typeof member>[]>()
    let signal!: AbortSignal
    t.mock.method(watchlistApi, 'getWatchlist', async incoming => {
      if (getAuthSession()?.user.id !== 'account-a') return [member('MSFT')]
      signal = incoming!
      const result = await response.promise
      if (status === 401) throw new WatchlistApiError(401, 'unauthorized')
      return result
    })
    const view = await mount()
    try {
      await act(async () => saveAuthSession({ ...session, accessToken: 'account-b-token', user: { ...session.user, id: 'account-b' } }))
      assert.equal(signal.aborted, true)
      await act(async () => response.resolve([member('AAPL')]))
      assert.match(view.card().textContent!, /MSFT/)
      assert.doesNotMatch(view.card().textContent!, /AAPL/)
      assert.equal(view.card().querySelector('[role="alert"]'), null)
      assert.equal(getAuthSession()?.accessToken, 'account-b-token')
    } finally { await view.close() }
  })
}

test('leaving the Dashboard removes refresh listeners and aborts a pending watchlist request', async t => {
  const response = deferred<ReturnType<typeof member>[]>()
  let signal!: AbortSignal
  const get = t.mock.method(watchlistApi, 'getWatchlist', incoming => { signal = incoming!; return response.promise })
  const view = await mount()
  await view.close()
  assert.equal(signal.aborted, true)
  await act(async () => {
    window.dispatchEvent(new dom.window.Event('focus'))
    window.dispatchEvent(new dom.window.PageTransitionEvent('pageshow', { persisted: true }))
    t.mock.getter(document, 'visibilityState', () => 'visible')
    document.dispatchEvent(new dom.window.Event('visibilitychange'))
    response.resolve([member('AAPL')])
  })
  assert.equal(get.mock.callCount(), 1)
})

for (const trigger of ['remount', 'focus', 'visibility', 'bfcache'] as const) {
  test(`Dashboard reloads persisted additions and removals on ${trigger}`, async t => {
    let persisted = [member('AAPL')]
    t.mock.method(watchlistApi, 'getWatchlist', async () => persisted)
    let view = await mount()
    try {
      assert.match(view.card().textContent!, /AAPL/)
      persisted = [member('MSFT')]
      if (trigger === 'remount') { await view.close(); view = await mount() }
      else await act(async () => {
        if (trigger === 'focus') window.dispatchEvent(new dom.window.Event('focus'))
        if (trigger === 'visibility') {
          t.mock.getter(document, 'visibilityState', () => 'visible')
          document.dispatchEvent(new dom.window.Event('visibilitychange'))
        }
        if (trigger === 'bfcache') window.dispatchEvent(new dom.window.PageTransitionEvent('pageshow', { persisted: true }))
      })
      assert.match(view.card().textContent!, /MSFT/)
      assert.doesNotMatch(view.card().textContent!, /AAPL/)
    } finally { await view.close() }
  })
}
