import assert from 'node:assert/strict'
import { beforeEach, test, type TestContext } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { createPortfolioApi, portfolioApi } from '../src/api/portfolioApi.ts'
import { tradingApi, type ExecuteTradeRequest } from '../src/api/tradingApi.ts'
import { watchlistApi } from '../src/api/watchlistApi.ts'
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
function portfolio(cashBalance = '12345.678901234567', currency = 'CAD') {
  return Response.json({ cashBalance, currency, initialCapital: '20000', investedValue: '0', totalValue: cashBalance, positions: [] })
}
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
beforeEach(async t => {
  window.localStorage.clear(); saveAuthSession(session)
  await i18n.changeLanguage('en')
  t.mock.method(marketDataApi, 'quote', async symbol => ({ symbol, name: symbol, exchange: 'NASDAQ', currency: 'USD', price: 100,
    priceDecimal: '100', change: null, changePercent: null, volume: null, asOfUtc: '2026-10-26T12:00:00Z', open: null,
    high: null, low: null, previousClose: null, averageVolume: null, isMarketOpen: false, fiftyTwoWeek: null }))
  t.mock.method(marketDataApi, 'history', async () => { throw new Error('test history unavailable') })
  t.mock.method(watchlistApi, 'getWatchlist', async () => [])
})
type Request = { signal: AbortSignal; authorization: string }
async function mount(t: TestContext, fetcher: (request: Request) => Promise<Response> = async () => portfolio()) {
  const requests: Request[] = []
  t.mock.method(portfolioApi, 'getPortfolio', createPortfolioApi('', async (url, init) => {
    assert.equal(url, '/api/portfolio')
    const request = { signal: init!.signal as AbortSignal, authorization: (init!.headers as Record<string, string>).Authorization }
    requests.push(request)
    return fetcher(request)
  }).getPortfolio)
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  const render = async (requestedSymbol: string) => {
    await act(async () => root.render(React.createElement(StockDetailsPage, { requestedSymbol, onBack() {} })))
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 25)) })
  }
  await render('AAPL')
  return { container, requests, render,
    cash: () => container.querySelector('.stock-cash-row')!.textContent!,
    feedback: () => container.querySelector('.stock-cash-feedback'),
    retry: () => container.querySelector<HTMLButtonElement>('.stock-cash-feedback button')!,
    async close() { await act(async () => root.unmount()); container.remove() } }
}

for (const [language, expected] of [['en', 'CA$12,345.678901234567'], ['fr', '12\u202f345,678901234567']]) {
  test(`initial cash is loaded without a trade and uses the portfolio currency in ${language}`, async t => {
    await i18n.changeLanguage(language)
    const execute = t.mock.method(tradingApi, 'executeTrade', async () => { throw new Error('no trade expected') })
    const view = await mount(t)
    try {
      assert.ok(view.cash().includes(expected), view.cash())
      assert.doesNotMatch(view.cash(), /—/)
      assert.equal(view.requests.length, 1)
      assert.equal(view.requests[0].authorization, 'Bearer account-a-token')
      assert.equal(execute.mock.callCount(), 0)
      await view.render('MSFT')
      assert.equal(view.requests.length, 1, 'changing symbols must not reset this account’s cash')
      assert.ok(view.cash().includes(expected))
    } finally { await view.close() }
  })
}

test('cash loading is visible, and an actual zero balance remains available', async t => {
  const response = deferred<Response>()
  const view = await mount(t, () => response.promise)
  try {
    assert.match(view.cash(), /Loading/)
    await act(async () => response.resolve(portfolio('0', 'USD')))
    assert.match(view.cash(), /\$0\.00/)
    assert.doesNotMatch(view.cash(), /Loading|—/)
  } finally { await view.close() }
})

for (const [failure, expected] of [
  ['offline', /Check your connection/], ['server', /Unable to load your portfolio/],
  ['missing', /could not be found/], ['invalid', /invalid response/],
] as const) {
  test(`${failure} portfolio failures show an error and retry recovers without a trade`, async t => {
    let attempts = 0
    const response = deferred<Response>()
    const view = await mount(t, async () => {
      if (++attempts > 1) return response.promise
      if (failure === 'offline') throw new TypeError('unreachable')
      return failure === 'invalid' ? Response.json({ cashBalance: '999' })
        : Response.json({ error: failure === 'missing' ? 'portfolio_not_found' : 'server_error' }, { status: failure === 'missing' ? 404 : 503 })
    })
    try {
      assert.match(view.feedback()!.querySelector('[role="alert"]')!.textContent!, expected)
      assert.match(view.cash(), /—/)
      await act(async () => { view.retry().click(); view.retry().click() })
      assert.equal(view.requests.length, 2, 'deduplicate retry clicks')
      assert.match(view.cash(), /Loading/)
      assert.equal(view.feedback()?.querySelector('[role="alert"]') ?? null, null)
      await act(async () => response.resolve(portfolio('321', 'EUR')))
      assert.match(view.cash(), /€321\.00/)
      assert.equal(view.feedback(), null)
    } finally { await view.close() }
  })
}

test('a rejected session offers sign in instead of a futile portfolio retry', async t => {
  const view = await mount(t, async () => Response.json({ error: 'unauthorized' }, { status: 401 }))
  try {
    assert.match(view.cash(), /—/)
    assert.ok(view.feedback()!.querySelector('a[href="/login"]'))
    assert.equal(view.feedback()!.querySelector('button'), null)
  } finally { await view.close() }
})

test('anonymous visitors never load a portfolio', async t => {
  clearAuthSession()
  const view = await mount(t)
  try { assert.equal(view.requests.length, 0); assert.match(view.cash(), /—/) }
  finally { await view.close() }
})

for (const lateStatus of [200, 401]) {
  test(`a late account A response (${lateStatus}) cannot change account B cash or session`, async t => {
    const response = deferred<Response>()
    const view = await mount(t, async ({ authorization }) => authorization.endsWith('a-token') ? response.promise : portfolio('777', 'EUR'))
    try {
      await act(async () => saveAuthSession({ ...session, accessToken: 'b-token', user: { ...session.user, id: 'account-b' } }))
      assert.equal(view.requests[0].signal.aborted, true)
      assert.match(view.cash(), /€777\.00/)
      await act(async () => response.resolve(lateStatus === 200 ? portfolio('123') : Response.json({}, { status: 401 })))
      assert.match(view.cash(), /€777\.00/)
      assert.doesNotMatch(view.cash(), /123/)
      assert.equal(view.feedback(), null)
      assert.equal(getAuthSession()?.accessToken, 'b-token')
    } finally { await view.close() }
  })
}

test('same-account token rotation clears old cash and loads with the new authorization', async t => {
  const response = deferred<Response>()
  const view = await mount(t, async ({ authorization }) => authorization.endsWith('a-token') ? portfolio('123') : response.promise)
  try {
    assert.match(view.cash(), /123/)
    await act(async () => saveAuthSession({ ...session, accessToken: 'rotated-token' }))
    assert.match(view.cash(), /Loading/)
    assert.doesNotMatch(view.cash(), /123/)
    assert.equal(view.requests[1].authorization, 'Bearer rotated-token')
    await act(async () => response.resolve(portfolio('456')))
    assert.match(view.cash(), /456/)
  } finally { await view.close() }
})

test('logout clears loaded cash immediately', async t => {
  const view = await mount(t)
  try {
    assert.match(view.cash(), /12,345/)
    await act(async () => clearAuthSession())
    assert.match(view.cash(), /—/)
    assert.doesNotMatch(view.cash(), /12,345/)
    assert.equal(view.requests.length, 1)
  } finally { await view.close() }
})

test('unmount aborts the initial portfolio request', async t => {
  const response = deferred<Response>()
  const view = await mount(t, () => response.promise)
  await view.close()
  assert.equal(view.requests[0].signal.aborted, true)
  await act(async () => response.resolve(portfolio()))
})

for (const lateStatus of [200, 503]) {
  test(`the executed order cash wins over a delayed initial portfolio response (${lateStatus})`, async t => {
    const response = deferred<Response>()
    t.mock.method(tradingApi, 'executeTrade', async (order: ExecuteTradeRequest) => ({ transactionId: 'settled',
      orderId: order.orderId, side: order.side, symbol: order.symbol, quantity: order.quantity,
      executionPrice: '100', totalAmount: '1000', cashBalance: '99999.999999999999', holdingQuantity: '10',
      averageCost: '100', executedAtUtc: '2026-10-26T12:00:00Z' }))
    const view = await mount(t, () => response.promise)
    try {
      await act(async () => view.container.querySelector('form.stock-trade-card')!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })))
      await act(async () => view.container.querySelector<HTMLButtonElement>('.stock-modal-actions .stock-primary-button')!.click())
      assert.match(view.cash(), /99,999\.999999999999/)
      await act(async () => response.resolve(lateStatus === 200 ? portfolio('20000', 'USD') : Response.json({}, { status: 503 })))
      assert.match(view.cash(), /99,999\.999999999999/)
      assert.equal(view.feedback(), null)
    } finally { await view.close() }
  })
}
