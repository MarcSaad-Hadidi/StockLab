import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { createPortfolioApi, portfolioApi } from '../src/api/portfolioApi.ts'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { saveAuthSession } from '../src/auth/authStorage.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/portfolio' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { default: PortfolioPage } = await import('../src/portfolio/PortfolioPage.tsx')
const session = { accessToken: 'token-a', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Alice Adams', email: 'alice@example.com' } }
const base = { cashBalance: 1111, initialCapital: 1000, investedValue: 100, totalValue: 1211, currency: 'CAD',
  positions: [{ symbol: 'AAA', quantity: 1, averageCost: 100 }, { symbol: 'BBB', quantity: 2, averageCost: 50 }] }
const quote = (symbol: string) => ({ symbol, name: symbol, price: 150, changePercent: 3, currency: 'CAD' })
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status })
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
beforeEach(async () => {
  window.localStorage.clear(); saveAuthSession(session)
  await i18n.changeLanguage('en')
})
async function mount() {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(PortfolioPage)))
  return { container, metrics: () => container.querySelector('.metrics-grid')!,
    feedback: () => container.querySelector('.portfolio-feedback')!,
    async retry() { await act(async () => (container.querySelector('[data-retry-portfolio]') as HTMLButtonElement).click()) },
    async close() { await act(async () => root.unmount()); container.remove() } }
}

for (const failure of ['server', 'network', 'invalid-json', 'invalid-data', 'invalid-currency', 'not-found']) {
  test(`portfolio ${failure} failure is explicit and cannot appear as an empty or zero-valued account`, async t => {
    const api = createPortfolioApi('', async () => {
      if (failure === 'network') throw new TypeError('unreachable')
      if (failure === 'invalid-json') return new Response('not-json')
      if (failure === 'invalid-data') return json({ cashBalance: 999999 })
      if (failure === 'invalid-currency') return json({ ...base, currency: 'invalid' })
      return json({ error: 'private backend detail' }, failure === 'not-found' ? 404 : 503)
    })
    t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
    const view = await mount()
    try {
      assert.ok(view.feedback().querySelector('[role="alert"]') || view.feedback().getAttribute('role') === 'alert')
      assert.equal(view.feedback().querySelector('[data-retry-portfolio]')?.textContent, 'Retry')
      assert.doesNotMatch(view.container.textContent!, /private backend detail|No positions yet|Positions \(0\)|not connected/)
      assert.doesNotMatch(view.metrics().textContent!, /\$0\.00|999,999/)
    } finally { await view.close() }
  })
}

test('loading stays distinct from a confirmed empty portfolio', async t => {
  const response = deferred<Response>()
  const api = createPortfolioApi('', () => response.promise)
  t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  const view = await mount()
  try {
    assert.match(view.feedback().textContent!, /Loading your portfolio/)
    assert.doesNotMatch(view.container.textContent!, /No positions yet|Positions \(0\)/)
    await act(async () => response.resolve(json({ ...base, positions: [], investedValue: 0 })))
    assert.match(view.container.textContent!, /No positions yet/)
    assert.match(view.metrics().textContent!, /CA\$1,111\.00/)
    assert.equal(view.container.querySelector('.portfolio-feedback'), null)
  } finally { await view.close() }
})

test('partial quote failure retains cash, positions and successful prices without invented totals; retry recovers', async t => {
  t.mock.method(portfolioApi, 'getPortfolio', async () => base)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  let fail = true
  t.mock.method(marketDataApi, 'quote', async (symbol: string) => {
    if (symbol === 'BBB' && fail) throw new Error('provider failure')
    return quote(symbol)
  })
  const view = await mount()
  try {
    assert.match(view.feedback().textContent!, /Some prices are unavailable/)
    assert.match(view.metrics().textContent!, /CA\$1,111\.00/)
    const rows = view.container.querySelectorAll('tbody tr')
    assert.match(rows[0].textContent!, /AAA.*CA\$150\.00/)
    assert.match(rows[1].textContent!, /BBB.*CA\$50\.00.*—/)
    assert.equal(view.container.querySelector('.metric-card strong')?.textContent, '—')
    fail = false; await view.retry()
    assert.equal(view.container.querySelector('.portfolio-feedback'), null)
    assert.match(view.container.querySelector('.metric-card strong')!.textContent!, /CA\$1,561\.00/)
    assert.match(view.container.querySelector('tbody')!.textContent!, /BBB.*CA\$300\.00/)
  } finally { await view.close() }
})

test('retry disables duplicate clicks and recovers a backend failure', async t => {
  let calls = 0
  const response = deferred<Response>()
  const api = createPortfolioApi('', () => ++calls === 1 ? Promise.resolve(json({}, 503)) : response.promise)
  t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  t.mock.method(marketDataApi, 'quote', async symbol => quote(symbol))
  const view = await mount()
  try {
    const retry = view.container.querySelector('[data-retry-portfolio]') as HTMLButtonElement
    await act(async () => { retry.click(); retry.click() })
    assert.equal(calls, 2)
    assert.equal((view.container.querySelector('[data-retry-portfolio]') as HTMLButtonElement).disabled, true)
    await act(async () => response.resolve(json(base)))
    assert.equal(view.container.querySelector('.portfolio-feedback'), null)
    assert.match(view.metrics().textContent!, /CA\$1,111\.00/)
  } finally { await view.close() }
})

test('a failed portfolio retry preserves the prior data and identifies it as last successful data', async t => {
  let calls = 0
  const response = deferred<Response>()
  const api = createPortfolioApi('', () => ++calls === 1 ? Promise.resolve(json(base)) : response.promise)
  t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  t.mock.method(marketDataApi, 'quote', async symbol => {
    if (symbol === 'BBB') throw new Error('provider unavailable')
    return quote(symbol)
  })
  const view = await mount()
  try {
    await view.retry()
    assert.match(view.metrics().textContent!, /CA\$1,111\.00/)
    await act(async () => response.resolve(json({}, 503)))
    assert.match(view.feedback().textContent!, /Unable to load your portfolio/)
    assert.match(view.feedback().textContent!, /last successful load/)
    assert.match(view.metrics().textContent!, /CA\$1,111\.00/)
    assert.match(view.container.querySelector('tbody')!.textContent!, /AAA.*CA\$150\.00.*BBB/)
  } finally { await view.close() }
})

test('unauthorized portfolio loads offer sign-in instead of retry', async t => {
  const api = createPortfolioApi('', async () => json({}, 401))
  t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
  const view = await mount()
  try {
    assert.match(view.feedback().textContent!, /Please sign in again/)
    assert.equal(view.feedback().querySelector('a')?.getAttribute('href'), '/login')
    assert.equal(view.container.querySelector('[data-retry-portfolio]'), null)
  } finally { await view.close() }
})

test('a rejected session during retry removes previously loaded financial data', async t => {
  let calls = 0
  const api = createPortfolioApi('', async () => ++calls === 1 ? json(base) : json({}, 401))
  t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  t.mock.method(marketDataApi, 'quote', async () => { throw new Error('quote unavailable') })
  const view = await mount()
  try {
    assert.match(view.metrics().textContent!, /CA\$1,111\.00/)
    await view.retry()
    assert.match(view.feedback().textContent!, /Please sign in again/)
    assert.doesNotMatch(view.container.textContent!, /1,111|AAA|BBB|last successful load/)
    assert.equal(view.container.querySelector('[data-retry-portfolio]'), null)
  } finally { await view.close() }
})

test('quotes in another currency do not produce an invented valuation', async t => {
  t.mock.method(portfolioApi, 'getPortfolio', async () => base)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  t.mock.method(marketDataApi, 'quote', async symbol => ({ ...quote(symbol), currency: 'USD' }))
  const view = await mount()
  try {
    assert.match(view.feedback().textContent!, /Some prices are unavailable/)
    assert.match(view.metrics().textContent!, /CA\$1,111\.00/)
    assert.equal(view.container.querySelector('.metric-card strong')?.textContent, '—')
    assert.doesNotMatch(view.container.querySelector('tbody')!.textContent!, /150\.00/)
    assert.equal(view.container.querySelectorAll('[data-retry-portfolio]').length, 0)
  } finally { await view.close() }
})

for (const language of ['en', 'fr']) {
  test(`the quote cap explains incomplete valuation without a futile retry in ${language}`, async t => {
    await i18n.changeLanguage(language)
    const positions = Array.from({ length: 25 }, (_, i) => ({ symbol: `S${i}`, quantity: 1, averageCost: 100 }))
    t.mock.method(portfolioApi, 'getPortfolio', async () => ({ ...base, positions }))
    t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
    const calls: string[] = []
    t.mock.method(marketDataApi, 'quote', async symbol => { calls.push(symbol); return quote(symbol) })
    const view = await mount()
    try {
      assert.equal(calls.length, 20)
      assert.equal(view.container.querySelectorAll('tbody tr').length, 25)
      assert.match(view.feedback().textContent!, language === 'fr' ? /limité à 20 positions/ : /limited to 20 positions/)
      assert.doesNotMatch(view.feedback().textContent!, /Retry|Réessayer|try again|réessayer/)
      assert.equal(view.container.querySelectorAll('[data-retry-portfolio]').length, 0)
      assert.match(view.metrics().textContent!, language === 'fr' ? /1\s*111,00/ : /1,111\.00/)
      assert.equal(view.container.querySelector('.metric-card strong')?.textContent, '—')
      assert.match(view.container.querySelectorAll('tbody tr')[24].textContent!, /S24.*—/)
    } finally { await view.close() }
  })
}

test('a recoverable quote failure above the cap can retry and then leaves only the limit notice', async t => {
  const positions = Array.from({ length: 25 }, (_, i) => ({ symbol: `S${i}`, quantity: 1, averageCost: 100 }))
  t.mock.method(portfolioApi, 'getPortfolio', async () => ({ ...base, positions }))
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  let fail = true
  const calls: string[] = []
  t.mock.method(marketDataApi, 'quote', async symbol => {
    calls.push(symbol)
    if (fail && symbol === 'S2') throw new Error('provider unavailable')
    return quote(symbol)
  })
  const view = await mount()
  try {
    assert.match(view.feedback().textContent!, /limited to 20 positions/)
    assert.equal(view.container.querySelector('[data-retry-portfolio]')?.textContent, 'Retry')
    fail = false; await view.retry()
    assert.equal(calls.length, 40)
    assert.ok(calls.every(symbol => Number(symbol.slice(1)) < 20))
    assert.equal(view.container.querySelectorAll('[data-retry-portfolio]').length, 0)
    assert.match(view.feedback().textContent!, /limited to 20 positions/)
    assert.doesNotMatch(view.feedback().textContent!, /try again/)
    assert.match(view.container.querySelectorAll('tbody tr')[2].textContent!, /S2.*CA\$150\.00/)
    assert.equal(view.container.querySelector('.metric-card strong')?.textContent, '—')
  } finally { await view.close() }
})

test('exactly twenty successfully quoted positions have a complete valuation and no limit notice', async t => {
  const positions = Array.from({ length: 20 }, (_, i) => ({ symbol: `S${i}`, quantity: 1, averageCost: 100 }))
  t.mock.method(portfolioApi, 'getPortfolio', async () => ({ ...base, positions }))
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  t.mock.method(marketDataApi, 'quote', async symbol => quote(symbol))
  const view = await mount()
  try {
    assert.equal(view.container.querySelector('.portfolio-feedback'), null)
    assert.equal(view.container.querySelector('.metric-card strong')?.textContent, 'CA$4,111.00')
  } finally { await view.close() }
})

test('late retry responses cannot expose the previous account data or errors', async t => {
  const response = deferred<Response>()
  let calls = 0
  const api = createPortfolioApi('', async () => ++calls === 1 ? json({}, 503) : calls === 2 ? response.promise
    : json({ ...base, cashBalance: 2222, positions: [], investedValue: 0 }))
  t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  const view = await mount()
  try {
    await view.retry()
    await act(async () => saveAuthSession({ ...session, accessToken: 'token-b', user: { ...session.user, id: 'account-b' } }))
    assert.match(view.metrics().textContent!, /CA\$2,222\.00/)
    await act(async () => response.resolve(json(base)))
    assert.match(view.metrics().textContent!, /CA\$2,222\.00/)
    assert.doesNotMatch(view.container.textContent!, /AAA|BBB|1,111/)
  } finally { await view.close() }
})

test('portfolio feedback and retry are translated to French', async t => {
  await i18n.changeLanguage('fr')
  const api = createPortfolioApi('', async () => json({}, 503))
  t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
  const view = await mount()
  try {
    assert.match(view.feedback().textContent!, /Impossible de charger votre portefeuille/)
    assert.equal(view.feedback().querySelector('[data-retry-portfolio]')?.textContent, 'Réessayer')
  } finally { await view.close() }
})
