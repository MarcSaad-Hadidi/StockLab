import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { createPortfolioApi, portfolioApi } from '../src/api/portfolioApi.ts'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { watchlistApi } from '../src/api/watchlistApi.ts'
import { saveAuthSession } from '../src/auth/authStorage.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/dashboard', pretendToBeVisual: true })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { DashboardPage } = await import('../src/dashboard/DashboardPage.tsx')
const session = { accessToken: 'dashboard-a', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Alice Adams', email: 'alice@example.test' } }
const base = { cashBalance: '1111', initialCapital: '1000', investedValue: '0', totalValue: '1111', currency: 'CAD', positions: [] }
const holding = { symbol: 'AAPL', quantity: '2', averageCost: '100' }
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status })
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
beforeEach(async t => {
  window.localStorage.clear(); saveAuthSession(session)
  await i18n.changeLanguage('en')
  t.mock.method(globalThis, 'fetch', async () => json({}, 503))
  t.mock.method(portfolioApi, 'getPortfolio', async () => base)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  t.mock.method(portfolioApi, 'getPerformanceHistory', async range => ({ range, currency: 'CAD', fromUtc: null,
    toUtc: new Date().toISOString(), sampleCount: 0, resolution: 'observation', points: [],
    firstRecordedAtUtc: null, lastRecordedAtUtc: null }))
  t.mock.method(watchlistApi, 'getWatchlist', async () => [])
  t.mock.method(marketDataApi, 'quote', async () => ({ symbol: 'AAPL', name: 'Apple Inc.', exchange: 'NASDAQ',
    price: 140.25, priceDecimal: '140.25', change: -3, changePercent: -2.5, currency: 'CAD', volume: null,
    asOfUtc: '2026-11-18T12:00:00Z', receivedAtUtc: '2026-11-18T12:00:00Z', source: 'Mock',
    open: null, high: null, low: null, previousClose: null, averageVolume: null, isMarketOpen: null, fiftyTwoWeek: null }))
})
async function mount() {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(DashboardPage)))
  return { container, positions: () => container.querySelector('[aria-labelledby="positions-title"]')!,
    async close() { await act(async () => root.unmount()); container.remove() } }
}

for (const language of ['en', 'fr']) {
  test(`a confirmed empty portfolio keeps real capital and offers Market in ${language}`, async () => {
    await i18n.changeLanguage(language)
    const view = await mount()
    try {
      assert.ok(view.positions().textContent!.includes(i18n.t('portfolio.emptyPositions')))
      assert.equal(view.positions().querySelector('a[href="/market"]')?.textContent, i18n.t('dashboard.exploreMarket'))
      assert.equal(view.positions().querySelector('[role="alert"]'), null)
      assert.equal(view.positions().querySelector('table'), null)
      assert.equal(view.positions().getAttribute('aria-busy'), 'false')
      assert.ok(!view.positions().textContent!.includes(i18n.t('businessData.backendPending')))
      const metrics = view.container.querySelector('.metrics-grid')!.textContent!
      assert.equal(metrics.match(language === 'en' ? /CA\$1,111\.00/g : /1\s*111,00\s*\$/g)?.length, 2)
      assert.match(metrics, language === 'en' ? /CA\$111\.00/ : /111,00\s*\$/)
    } finally { await view.close() }
  })
}

test('a pending first load is not an empty portfolio or a disconnected service', async t => {
  const response = deferred<typeof base>()
  t.mock.method(portfolioApi, 'getPortfolio', () => response.promise)
  const view = await mount()
  try {
    assert.equal(view.positions().getAttribute('aria-busy'), 'true')
    assert.match(view.positions().querySelector('[role="status"]')!.textContent!, /Loading your portfolio/)
    assert.doesNotMatch(view.positions().textContent!, /No positions|not connected/)
    assert.equal(view.positions().querySelector('a[href="/market"]'), null)
    await act(async () => response.resolve(base))
    assert.match(view.positions().textContent!, /No positions yet/)
  } finally { await view.close() }
})

for (const failure of ['offline', 'server_error', 'invalid_response']) {
  test(`a real ${failure} failure offers retry without claiming the portfolio is empty`, async t => {
    const api = createPortfolioApi('', async () => {
      if (failure === 'offline') throw new TypeError('network unavailable')
      return failure === 'invalid_response' ? json({}) : json({ error: 'server_error' }, 503)
    })
    t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
    const view = await mount()
    try {
      assert.equal(view.positions().querySelector('[role="alert"] p')?.textContent, i18n.t(`portfolio.errors.${failure}`))
      assert.equal(view.positions().querySelector('[data-retry-portfolio]')?.textContent, 'Retry')
      assert.doesNotMatch(view.positions().textContent!, /No positions|not connected/)
      assert.equal(view.positions().querySelector('a[href="/market"]'), null)
    } finally { await view.close() }
  })
}

test('retry replaces an error with the successful empty state and prevents duplicate loads', async t => {
  const response = deferred<Response>()
  let calls = 0
  const api = createPortfolioApi('', async () => ++calls === 1 ? json({}, 503) : response.promise)
  t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
  const view = await mount()
  try {
    const retry = view.positions().querySelector('[data-retry-portfolio]') as HTMLButtonElement
    assert.ok(retry)
    await act(async () => { retry.click(); retry.click() })
    assert.equal(calls, 2)
    assert.ok((view.positions().querySelector('[data-retry-portfolio]') as HTMLButtonElement).disabled)
    assert.equal(view.positions().querySelector('[role="alert"]'), null)
    assert.match(view.positions().textContent!, /Loading your portfolio/)
    assert.doesNotMatch(view.positions().textContent!, /No positions|Unable to load/)
    await act(async () => response.resolve(json(base)))
    assert.match(view.positions().textContent!, /No positions yet/)
    assert.equal(view.positions().querySelector('[data-retry-portfolio]'), null)
  } finally { await view.close() }
})

test('an unauthorized load asks for sign-in instead of retrying or showing empty holdings', async t => {
  const api = createPortfolioApi('', async () => json({}, 401))
  t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
  const view = await mount()
  try {
    assert.ok(view.positions().querySelector('[role="alert"]'))
    assert.equal(view.positions().querySelector('a[href="/login"]')?.textContent, 'Sign in')
    assert.equal(view.positions().querySelector('[data-retry-portfolio]'), null)
    assert.doesNotMatch(view.positions().textContent!, /No positions|not connected/)
  } finally { await view.close() }
})

for (const quotesAvailable of [true, false]) {
  test(`existing holdings remain visible when quotes are ${quotesAvailable ? 'available' : 'unavailable'}`, async t => {
    t.mock.method(portfolioApi, 'getPortfolio', async () => ({ ...base, positions: [holding] }))
    if (!quotesAvailable) t.mock.method(marketDataApi, 'quote', async () => { throw new Error('quote unavailable') })
    const view = await mount()
    try {
      assert.match(view.positions().querySelector('tbody')!.textContent!, /AAPL/)
      assert.doesNotMatch(view.positions().textContent!, /No positions|not connected/)
      assert.equal(view.positions().querySelector('a[href="/market"]'), null)
      if (quotesAvailable) assert.match(view.positions().textContent!, /CA\$280\.50/)
    } finally { await view.close() }
  })
}

for (const hasHoldings of [false, true]) {
  test(`a failed refresh retains ${hasHoldings ? 'holdings and' : 'empty snapshot'} cash with a stale-data explanation`, async t => {
    const response = deferred<Response>()
    let calls = 0
    const api = createPortfolioApi('', async () => ++calls === 1
      ? json({ ...base, positions: hasHoldings ? [holding] : [] }) : response.promise)
    t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
    const view = await mount()
    try {
      await act(async () => {
        window.dispatchEvent(new dom.window.Event('focus'))
        await new Promise(resolve => setTimeout(resolve, 150))
      })
      assert.equal(calls, 2)
      assert.match(view.positions().textContent!, /Loading your portfolio/)
      assert.match(view.positions().textContent!, /last successful load/)
      assert.doesNotMatch(view.positions().textContent!, /No positions/)
      await act(async () => response.resolve(json({}, 503)))
      assert.match(view.positions().querySelector('[role="alert"]')!.textContent!, /Unable to load your portfolio/)
      assert.match(view.positions().textContent!, /last successful load/)
      assert.match(view.container.querySelector('.metrics-grid')!.textContent!, /CA\$1,111\.00/)
      assert.doesNotMatch(view.positions().textContent!, /No positions|not connected/)
      if (hasHoldings) assert.match(view.positions().querySelector('tbody')!.textContent!, /AAPL/)
    } finally { await view.close() }
  })
}
