import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { watchlistApi, WatchlistApiError } from '../src/api/watchlistApi.ts'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { createMarketDataApi } from '../src/api/marketDataApi.ts'
import { saveAuthSession, getAuthSession } from '../src/auth/authStorage.ts'
import { useWatchlistData } from '../src/watchlist/useWatchlistData.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/watchlist' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
await i18n.changeLanguage('en')

const session = { accessToken: 'test-token', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'user-a', displayName: 'User A', email: 'a@example.test' } }
const aapl = { symbol: 'AAPL', createdAtUtc: '2026-10-04T12:00:00Z' }
const msft = { symbol: 'MSFT', createdAtUtc: '2026-10-03T12:00:00Z' }
const quote = { symbol: 'AAPL', price: 200, priceDecimal: '200', change: 4, changePercent: 2, volume: null, currency: 'CAD',
  asOfUtc: '2026-10-04T12:00:00Z', name: 'Apple Inc.', exchange: 'NASDAQ', open: null, high: null, low: null,
  previousClose: null, averageVolume: null, isMarketOpen: null, fiftyTwoWeek: null }
function seed() { window.localStorage.clear(); saveAuthSession(session) }
async function mount(component: React.ReactElement) {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(component))
  return { container, async close() { await act(async () => root.unmount()); container.remove() } }
}
async function probe(enrich = true) {
  let state!: ReturnType<typeof useWatchlistData>
  function Probe() { state = useWatchlistData(enrich); return null }
  const view = await mount(React.createElement(Probe))
  return { ...view, state: () => state }
}

test('membership is fetched on each mount and missing quotes keep all persisted symbols', async t => {
  seed()
  let reads = 0
  t.mock.method(watchlistApi, 'getWatchlist', async () => { reads++; return [aapl, { ...msft, symbol: 'NVDA' }] })
  t.mock.method(marketDataApi, 'quote', async symbol => { if (symbol === 'NVDA') throw new Error('provider offline'); return quote })
  for (let run = 0; run < 2; run++) {
    const view = await probe()
    try {
      assert.equal(view.state().loading, false)
      assert.deepEqual(view.state().items.map(item => item.symbol), ['AAPL', 'NVDA'])
      assert.equal(view.state().items[0].currency, 'CAD')
      assert.equal(view.state().items[0].name, 'Apple Inc.')
      assert.equal(view.state().items[1].price, null)
      assert.equal(view.state().items[1].changePercent, null)
    } finally { await view.close() }
  }
  assert.equal(reads, 2)
  assert.deepEqual(Object.keys(window.localStorage), ['stocklab-auth'])
})

test('enrichment runs at most five quotes at once', async t => {
  seed()
  t.mock.method(watchlistApi, 'getWatchlist', async () => Array.from({ length: 12 }, (_, n) => ({ ...aapl, symbol: `SYM${n}` })))
  let active = 0; let peak = 0
  t.mock.method(marketDataApi, 'quote', async symbol => {
    active++; peak = Math.max(peak, active)
    await new Promise(resolve => setTimeout(resolve, 1))
    active--; return { ...quote, symbol }
  })
  const view = await probe()
  try {
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 40)) })
    assert.equal(view.state().items.length, 12)
    assert.equal(view.state().loading, false)
    assert.ok(peak <= 5)
    assert.ok(peak > 1)
  } finally { await view.close() }
})

test('Market membership loads without requesting quotes', async t => {
  seed()
  t.mock.method(watchlistApi, 'getWatchlist', async () => [aapl])
  t.mock.method(marketDataApi, 'quote', async () => { assert.fail('membership must not request quotes') })
  const view = await probe(false)
  try { assert.deepEqual([...view.state().favorites], ['AAPL']) } finally { await view.close() }
})

test('remove waits for backend success and blocks duplicate clicks and reload races', async t => {
  seed()
  t.mock.method(watchlistApi, 'getWatchlist', async () => [aapl, msft])
  let finish!: () => void; let deletes = 0
  t.mock.method(watchlistApi, 'removeFromWatchlist', () => { deletes++; return new Promise<void>(resolve => { finish = resolve }) })
  const view = await probe(false)
  try {
    let pending!: Promise<boolean>
    await act(async () => { pending = view.state().remove('AAPL'); assert.equal(await view.state().remove('AAPL'), false); view.state().reload() })
    assert.equal(deletes, 1)
    assert.equal(view.state().pendingSymbols.has('AAPL'), true)
    assert.equal(view.state().items.length, 2)
    await act(async () => { finish(); assert.equal(await pending, true) })
    assert.deepEqual([...view.state().favorites], ['MSFT'])
    assert.equal(view.state().pendingSymbols.size, 0)
  } finally { await view.close() }
})

test('failed mutations retain membership and expose a controlled error', async t => {
  seed()
  t.mock.method(watchlistApi, 'getWatchlist', async () => [aapl])
  t.mock.method(watchlistApi, 'removeFromWatchlist', async () => { throw new WatchlistApiError(500, 'server_error') })
  t.mock.method(watchlistApi, 'addToWatchlist', async () => { throw new WatchlistApiError(0, 'offline') })
  const view = await probe(false)
  try {
    await act(async () => { assert.equal(await view.state().remove('AAPL'), false) })
    assert.equal(view.state().favorites.has('AAPL'), true)
    assert.equal(view.state().mutationError, 'server_error')
    await act(async () => { assert.equal(await view.state().add('MSFT'), false) })
    assert.deepEqual([...view.state().favorites], ['AAPL'])
    assert.equal(view.state().mutationError, 'offline')
  } finally { await view.close() }
})

test('successful addition uses the server item and duplicate conflicts reconcile membership', async t => {
  seed()
  let persisted = [aapl]
  t.mock.method(watchlistApi, 'getWatchlist', async () => persisted)
  t.mock.method(watchlistApi, 'addToWatchlist', async symbol => {
    if (symbol === 'NVDA') { persisted = [...persisted, { ...msft, symbol }]; throw new WatchlistApiError(409, 'already_exists') }
    return msft
  })
  const view = await probe(false)
  try {
    await act(async () => { assert.equal(await view.state().add('MSFT'), true) })
    assert.equal(view.state().items.find(item => item.symbol === 'MSFT')?.createdAtUtc, msft.createdAtUtc)
    await act(async () => { assert.equal(await view.state().add('NVDA'), true) })
    assert.equal(view.state().favorites.has('NVDA'), true)
    assert.equal(view.state().favorites.has('MSFT'), true, 'reconciliation must not discard another completed addition')
  } finally { await view.close() }
})

test('unauthorized loads and mutations clear the session and never look empty', async t => {
  seed()
  let unauthorized = false
  t.mock.method(watchlistApi, 'getWatchlist', async () => { if (unauthorized) throw new WatchlistApiError(401, 'unauthorized'); return [aapl] })
  t.mock.method(watchlistApi, 'removeFromWatchlist', async () => { throw new WatchlistApiError(401, 'unauthorized') })
  const first = await probe(false)
  try {
    await act(async () => { assert.equal(await first.state().remove('AAPL'), false) })
    assert.equal(getAuthSession(), null)
    assert.equal(first.state().error, 'unauthorized')
    assert.equal(first.state().items.length, 0)
  } finally { await first.close() }
  seed(); unauthorized = true
  const second = await probe(false)
  try { assert.equal(second.state().error, 'unauthorized'); assert.equal(getAuthSession(), null) }
  finally { await second.close() }
})

test('late responses from user A cannot replace user B membership or clear their session', async t => {
  seed()
  let finish!: (items: typeof aapl[]) => void
  let reads = 0
  t.mock.method(watchlistApi, 'getWatchlist', () => { reads++; return reads === 1 ? new Promise(resolve => { finish = resolve }) : Promise.resolve([msft]) })
  const view = await probe(false)
  try {
    saveAuthSession({ ...session, accessToken: 'user-b-token', user: { ...session.user, id: 'user-b' } })
    await act(async () => { window.dispatchEvent(new dom.window.Event('focus')); finish([aapl]) })
    assert.deepEqual([...view.state().favorites], ['MSFT'])
    assert.equal(getAuthSession()?.user.id, 'user-b')
  } finally { await view.close() }
})

test('unmount aborts the membership request', async t => {
  seed()
  let signal: AbortSignal | undefined
  t.mock.method(watchlistApi, 'getWatchlist', incoming => { signal = incoming; return new Promise(() => {}) })
  const view = await probe(false)
  await view.close()
  assert.equal(signal?.aborted, true)
})

test('Watchlist renders persisted rows, partial quotes, currency and real dates for sorting', async t => {
  seed()
  t.mock.method(watchlistApi, 'getWatchlist', async () => [msft, aapl])
  t.mock.method(marketDataApi, 'quote', async symbol => { if (symbol === 'MSFT') throw new Error('offline'); return quote })
  const { default: Page } = await import('../src/watchlist/WatchlistPage.tsx')
  const view = await mount(React.createElement(Page))
  try {
    const rows = view.container.querySelectorAll('.watchlist-row')
    assert.equal(rows.length, 2)
    assert.equal(rows[0].querySelector('.watchlist-symbol strong')?.textContent, 'AAPL')
    assert.match(rows[0].textContent!, /CA\$200\.00/)
    assert.equal(rows[1].querySelector('.watchlist-price strong')?.textContent, '—')
    assert.equal(view.container.querySelectorAll('.summary-card strong')[3].textContent, '+2.00%')
    assert.equal(view.container.querySelector('a.primary-button')?.getAttribute('href'), '/market')
    assert.ok((view.container.querySelector('.alert-button') as HTMLButtonElement).disabled)
    assert.doesNotMatch(view.container.textContent!, /backend.*pending|backend connection/i)
    const select = view.container.querySelector('select[aria-label="Sort watchlist"]') as HTMLSelectElement
    await act(async () => { select.value = 'price'; select.dispatchEvent(new dom.window.Event('change', { bubbles: true })) })
    assert.equal(view.container.querySelector('.watchlist-symbol strong')?.textContent, 'AAPL')
  } finally { await view.close() }
})

test('Watchlist Refresh prices fetches fresh quotes within the cache lifetime on every click', async t => {
  seed()
  const now = Date.now()
  t.mock.method(Date, 'now', () => now)
  let price = 200
  let quoteRequests = 0
  const client = createMarketDataApi('', async () => {
    quoteRequests++
    return Response.json({ ...quote, price })
  })
  await client.quote('AAPL', new AbortController().signal)
  t.mock.method(watchlistApi, 'getWatchlist', async () => [aapl])
  t.mock.method(marketDataApi, 'quote', client.quote)
  const { default: Page } = await import('../src/watchlist/WatchlistPage.tsx')
  const view = await mount(React.createElement(Page))
  try {
    assert.equal(quoteRequests, 1, 'ordinary page loading reuses the cached quote')
    assert.equal(view.container.querySelector('.watchlist-price strong')?.textContent, 'CA$200.00')
    for (const nextPrice of [225, 250]) {
      price = nextPrice
      const previousRequests = quoteRequests
      await act(async () => (view.container.querySelector('.panel-action') as HTMLButtonElement).click())
      assert.equal(quoteRequests, previousRequests + 1, 'each explicit refresh must reach the backend before the cache expires')
      assert.equal(view.container.querySelector('.watchlist-price strong')?.textContent, `CA$${nextPrice}.00`)
      assert.equal((await client.quote('AAPL', new AbortController().signal)).price, nextPrice)
      assert.equal(quoteRequests, previousRequests + 1, 'ordinary consumers reuse the refreshed cache entry')
    }
  } finally { await view.close() }
})

test('Watchlist remove disables the affected button, keeps rows on failure and only toasts success', async t => {
  seed()
  t.mock.method(watchlistApi, 'getWatchlist', async () => [aapl])
  t.mock.method(marketDataApi, 'quote', async () => quote)
  let finish!: (value?: unknown) => void
  t.mock.method(watchlistApi, 'removeFromWatchlist', () => new Promise<void>((_resolve, reject) => { finish = reject }))
  const { default: Page } = await import('../src/watchlist/WatchlistPage.tsx')
  const view = await mount(React.createElement(Page))
  try {
    const remove = view.container.querySelector('.remove-button') as HTMLButtonElement
    assert.ok(remove)
    await act(async () => remove.click())
    assert.equal(remove.disabled, true)
    assert.equal(view.container.querySelectorAll('.watchlist-row').length, 1)
    await act(async () => finish(new WatchlistApiError(500, 'server_error')))
    assert.equal(view.container.querySelectorAll('.watchlist-row').length, 1)
    assert.match(view.container.querySelector('[role="alert"]')!.textContent!, /try again/i)
    assert.equal(view.container.querySelector('.toast')!.textContent, '')
    t.mock.method(watchlistApi, 'removeFromWatchlist', async () => {})
    await act(async () => remove.click())
    assert.equal(view.container.querySelectorAll('.watchlist-row').length, 0)
    assert.match(view.container.querySelector('.toast')!.textContent!, /AAPL removed/)
  } finally { await view.close() }
})

test('Watchlist distinguishes loading, failed loading, retry and confirmed empty membership', async t => {
  seed()
  let finish!: (items: typeof aapl[]) => void
  let fail!: (error: unknown) => void
  t.mock.method(watchlistApi, 'getWatchlist', () => new Promise((resolve, reject) => { finish = resolve; fail = reject }))
  const { default: Page } = await import('../src/watchlist/WatchlistPage.tsx')
  const view = await mount(React.createElement(Page))
  try {
    assert.equal(view.container.querySelector('[aria-busy="true"]') !== null, true)
    assert.doesNotMatch(view.container.textContent!, /No watchlist items/)
    assert.equal(view.container.querySelector('.summary-card strong')?.textContent, '—')
    await act(async () => fail(new WatchlistApiError(0, 'offline')))
    assert.match(view.container.querySelector('[role="alert"]')!.textContent!, /connection/i)
    assert.doesNotMatch(view.container.textContent!, /No watchlist items/)
    const retry = Array.from(view.container.querySelectorAll('button')).find(button => button.textContent === 'Retry')!
    await act(async () => retry.click())
    await act(async () => finish([]))
    assert.match(view.container.textContent!, /No watchlist items/)
  } finally { await view.close() }
})

test('Watchlist expired sessions offer sign in without a misleading empty state', async t => {
  seed()
  t.mock.method(watchlistApi, 'getWatchlist', async () => { throw new WatchlistApiError(401, 'unauthorized') })
  const { default: Page } = await import('../src/watchlist/WatchlistPage.tsx')
  const view = await mount(React.createElement(Page))
  try {
    assert.ok(view.container.querySelector('a[href="/login"]'))
    assert.doesNotMatch(view.container.textContent!, /No watchlist items/)
  } finally { await view.close() }
})

test('unsupported quote currency cannot crash the Watchlist or invent a dollar price', async t => {
  seed()
  t.mock.method(watchlistApi, 'getWatchlist', async () => [aapl])
  t.mock.method(marketDataApi, 'quote', async () => ({ ...quote, currency: 'US Dollars' }))
  const { default: Page } = await import('../src/watchlist/WatchlistPage.tsx')
  const view = await mount(React.createElement(Page))
  try {
    assert.equal(view.container.querySelectorAll('.watchlist-row').length, 1)
    assert.equal(view.container.querySelector('.watchlist-price strong')?.textContent, '—')
    assert.equal(view.container.querySelector('.watchlist-change strong')?.textContent, '—')
  } finally { await view.close() }
})

test('Market stars reflect backend membership and toggle only after POST or DELETE success', async t => {
  seed()
  t.mock.method(watchlistApi, 'getWatchlist', async () => [aapl])
  t.mock.method(marketDataApi, 'quote', async symbol => ({ ...quote, symbol }))
  t.mock.method(marketDataApi, 'movers', async () => ({ lastUpdated: null, gainers: [], losers: [], mostActive: [] }))
  let finish!: (item: typeof msft) => void
  let posted = ''
  t.mock.method(watchlistApi, 'addToWatchlist', symbol => { posted = symbol; return new Promise(resolve => { finish = resolve }) })
  let removed = ''
  t.mock.method(watchlistApi, 'removeFromWatchlist', async symbol => { removed = symbol })
  const { MarketPage } = await import('../src/market/MarketPage.tsx')
  const view = await mount(React.createElement(MarketPage, { onOpenStock() {} }))
  try {
    const apple = view.container.querySelector('.market-favorite-button[aria-label*="AAPL"]') as HTMLButtonElement
    const microsoft = view.container.querySelector('.market-favorite-button[aria-label*="MSFT"]') as HTMLButtonElement
    assert.ok(apple && microsoft)
    assert.equal(apple.getAttribute('aria-pressed'), 'true')
    assert.equal(microsoft.getAttribute('aria-pressed'), 'false')
    await act(async () => microsoft.click())
    assert.equal(posted, 'MSFT')
    assert.equal(microsoft.disabled, true)
    assert.equal(microsoft.getAttribute('aria-pressed'), 'false')
    await act(async () => finish(msft))
    assert.equal(microsoft.getAttribute('aria-pressed'), 'true')
    assert.match(view.container.querySelector('[role="status"].watchlist-feedback')!.textContent!, /MSFT added/)
    await act(async () => apple.click())
    assert.equal(removed, 'AAPL')
    assert.equal(apple.getAttribute('aria-pressed'), 'false')
    saveAuthSession({ ...session, accessToken: 'user-b-token', user: { ...session.user, id: 'user-b' } })
    await act(async () => window.dispatchEvent(new dom.window.Event('focus')))
    assert.equal(view.container.querySelector('[role="status"].watchlist-feedback') === null, true, 'another user must not see the previous account confirmation')
  } finally { await view.close() }
})
