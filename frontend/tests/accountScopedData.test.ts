import assert from 'node:assert/strict'
import { test, type TestContext } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { createPortfolioApi, portfolioApi } from '../src/api/portfolioApi.ts'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { authStorageKey, saveAuthSession, clearAuthSession, getAuthSession, type AuthSession } from '../src/auth/authStorage.ts'
import { usePortfolioData } from '../src/portfolio/usePortfolioData.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/transactions' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
await i18n.changeLanguage('en')
const { TransactionsPage } = await import('../src/transactions/TransactionsPage.tsx')
const { TopBar } = await import('../src/components/layout/TopBar.tsx')

const accountA: AuthSession = { accessToken: 'token-a', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Alice Adams', email: 'alice@example.com' } }
const accountB: AuthSession = { ...accountA, accessToken: 'token-b',
  user: { id: 'account-b', displayName: 'Bob Brown', email: 'bob@example.com' } }
const base = { cashBalance: 1000, investedValue: 0, totalValue: 1000, initialCapital: 1000, currency: 'CAD', positions: [] }
const tradeA = { id: 'trade-a', symbol: 'AAA', side: 'BUY', quantity: 1, executionPrice: 100,
  totalAmount: 100, executedAtUtc: '2026-01-01T00:00:00Z' }
const tradeB = { ...tradeA, id: 'trade-b', symbol: 'BBB', totalAmount: 200 }
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
function apiFixture(t: TestContext, handler?: (url: string, token: string, init?: RequestInit) => Promise<Response>) {
  const calls: Array<{ url: string; token: string; signal?: AbortSignal | null }> = []
  const api = createPortfolioApi('', async (input, init) => {
    const url = String(input)
    const token = (init?.headers as Record<string, string>).Authorization
    calls.push({ url, token, signal: init?.signal })
    if (handler) return handler(url, token, init)
    const isA = token === 'Bearer token-a'
    if (url === '/api/portfolio') return json({ ...base, cashBalance: isA ? 1111 : 2222 })
    if (url.includes('/history')) {
      const page = Number(new URL(url, 'http://localhost').searchParams.get('page'))
      return json({ items: [isA ? tradeA : tradeB], page, pageSize: 10, totalCount: 21, currency: 'CAD',
        summary: { totalTrades: 21, totalInvested: isA ? 1111 : 2222, totalProceeds: 0 } })
    }
    return json([isA ? tradeA : tradeB])
  })
  t.mock.method(portfolioApi, 'getPortfolio', api.getPortfolio)
  t.mock.method(portfolioApi, 'getRecentTransactions', api.getRecentTransactions)
  t.mock.method(portfolioApi, 'getTransactionHistory', api.getTransactionHistory)
  return calls
}
function seed() { window.localStorage.clear(); saveAuthSession(accountA) }
async function change(session: AuthSession | null, trigger = 'storage') {
  await act(async () => {
    if (trigger === 'same-tab') { if (session) saveAuthSession(session); else clearAuthSession(); return }
    if (session) window.localStorage.setItem(authStorageKey, JSON.stringify(session))
    else window.localStorage.clear()
    if (trigger === 'visibility') {
      Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
      document.dispatchEvent(new dom.window.Event('visibilitychange'))
    } else if (trigger === 'focus') window.dispatchEvent(new dom.window.Event('focus'))
    else window.dispatchEvent(new dom.window.StorageEvent('storage', { key: session ? authStorageKey : null }))
  })
}
async function mount(component: React.ReactElement) {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(component))
  return { container, async close() { await act(async () => root.unmount()); container.remove() } }
}
async function settle() { await act(async () => { await new Promise(resolve => setTimeout(resolve, 180)) }) }
function PortfolioProbe() {
  const state = usePortfolioData()
  return React.createElement(React.Fragment, null,
    React.createElement(TopBar, { title: 'Portfolio', onMenuOpen() {} }),
    React.createElement('output', null, JSON.stringify(state.data)),
    React.createElement('span', { 'data-loading': state.isLoading }, state.error?.code))
}

for (const trigger of ['storage', 'focus', 'visibility', 'same-tab']) {
  test(`portfolio and recent activity follow account changes on ${trigger} without exposing old data`, async t => {
    seed()
    const pending = deferred<Response>()
    apiFixture(t, async (url, token) => {
      if (url === '/api/portfolio') return token === 'Bearer token-a' ? json({ ...base, cashBalance: 1111 }) : pending.promise
      return json([token === 'Bearer token-a' ? tradeA : tradeB])
    })
    const view = await mount(React.createElement(PortfolioProbe))
    try {
      assert.match(view.container.querySelector('output')!.textContent!, /1111/)
      await change(accountB, trigger)
      assert.equal(view.container.querySelector('.app-topbar-avatar')!.textContent, 'BB')
      assert.equal(view.container.querySelector('output')!.textContent, 'null')
      assert.equal(view.container.querySelector('[data-loading]')!.getAttribute('data-loading'), 'true')
      await act(async () => pending.resolve(json({ ...base, cashBalance: 2222 })))
      assert.match(view.container.querySelector('output')!.textContent!, /2222.*trade-b/)
      assert.doesNotMatch(view.container.querySelector('output')!.textContent!, /1111|trade-a/)
    } finally { await view.close() }
  })
}

test('late old-account portfolio responses cannot fetch activity with the new account token', async t => {
  seed()
  const old = deferred<Response>()
  const calls = apiFixture(t, async (url, token) => {
    if (url === '/api/portfolio') return token === 'Bearer token-a' ? old.promise : json({ ...base, cashBalance: 2222 })
    return json([token === 'Bearer token-a' ? tradeA : tradeB])
  })
  const view = await mount(React.createElement(PortfolioProbe))
  try {
    await change(accountB)
    await act(async () => old.resolve(json({ ...base, cashBalance: 1111 })))
    assert.match(view.container.querySelector('output')!.textContent!, /2222.*trade-b/)
    assert.equal(calls.filter(call => call.url.includes('/transactions')).length, 1)
    assert.equal(calls[0].signal?.aborted, true)
  } finally { await view.close() }
})

test('old-account quote completion cannot republish positions after account replacement', async t => {
  seed()
  const quote = deferred<Awaited<ReturnType<typeof marketDataApi.quote>>>()
  apiFixture(t, async (url, token) => url === '/api/portfolio'
    ? json(token === 'Bearer token-a' ? { ...base, positions: [{ symbol: 'AAA', quantity: 1, averageCost: 100 }] } : { ...base, cashBalance: 2222 })
    : json([token === 'Bearer token-a' ? tradeA : tradeB]))
  t.mock.method(marketDataApi, 'quote', () => quote.promise)
  const view = await mount(React.createElement(PortfolioProbe))
  try {
    await change(accountB)
    await act(async () => quote.resolve({ symbol: 'AAA', name: 'Alice holding', price: 100, changePercent: 0, currency: 'CAD' }))
    assert.match(view.container.querySelector('output')!.textContent!, /2222/)
    assert.doesNotMatch(view.container.querySelector('output')!.textContent!, /AAA|Alice holding|trade-a/)
  } finally { await view.close() }
})

test('transactions clear old rows and totals immediately, reset pagination and ignore late old errors', async t => {
  seed()
  const old = deferred<Response>()
  const next = deferred<Response>()
  const calls = apiFixture(t, async (url, token) => {
    const page = Number(new URL(url, 'http://localhost').searchParams.get('page'))
    if (token === 'Bearer token-b') return next.promise
    if (page === 2) return old.promise
    return json({ items: [tradeA], page: 1, pageSize: 10, totalCount: 21, currency: 'CAD',
      summary: { totalTrades: 21, totalInvested: 1111, totalProceeds: 0 } })
  })
  const view = await mount(React.createElement(TransactionsPage))
  try {
    await settle()
    assert.match(view.container.querySelector('tbody')!.textContent!, /AAA/)
    await act(async () => (view.container.querySelector('[aria-label="Next page"]') as HTMLButtonElement).click())
    await settle()
    await change(accountB)
    assert.equal(view.container.querySelector('.app-topbar-avatar')!.textContent, 'BB')
    assert.doesNotMatch(view.container.querySelector('tbody')!.textContent!, /AAA/)
    assert.doesNotMatch(view.container.querySelector('.summary-grid')!.textContent!, /1,111/)
    assert.equal(view.container.querySelector('.table-footer'), null)
    await settle()
    assert.match(calls.at(-1)!.url, /page=1&/)
    await act(async () => next.resolve(json({ items: [tradeB], page: 1, pageSize: 10, totalCount: 1, currency: 'CAD',
      summary: { totalTrades: 1, totalInvested: 2222, totalProceeds: 0 } })))
    await act(async () => old.resolve(json({ error: 'unauthorized' }, 401)))
    assert.match(view.container.querySelector('tbody')!.textContent!, /BBB/)
    assert.match(view.container.querySelector('.summary-grid')!.textContent!, /2,222/)
    assert.doesNotMatch(view.container.textContent!, /Unable to load transactions/)
  } finally { await view.close() }
})

test('same-account token rotation refreshes portfolio and history with the current token', async t => {
  seed()
  const calls = apiFixture(t)
  const portfolio = await mount(React.createElement(PortfolioProbe))
  const transactions = await mount(React.createElement(TransactionsPage))
  try {
    await settle()
    await change({ ...accountA, accessToken: 'token-new' })
    await settle()
    assert.match(portfolio.container.querySelector('output')!.textContent!, /2222/)
    assert.match(transactions.container.querySelector('tbody')!.textContent!, /BBB/)
    assert.ok(calls.some(call => call.url.includes('/history') && call.token === 'Bearer token-new'))
    assert.equal(getAuthSession()?.accessToken, 'token-new')
  } finally { await portfolio.close(); await transactions.close() }
})

test('logout hides all financial data, stops requests and a new login loads automatically', async t => {
  seed()
  const calls = apiFixture(t)
  const portfolio = await mount(React.createElement(PortfolioProbe))
  const transactions = await mount(React.createElement(TransactionsPage))
  try {
    await settle()
    const count = calls.length
    await change(null)
    await settle()
    assert.equal(portfolio.container.querySelector('output')!.textContent, 'null')
    assert.equal(portfolio.container.querySelector('[data-loading]')!.getAttribute('data-loading'), 'false')
    assert.doesNotMatch(transactions.container.querySelector('tbody')!.textContent!, /AAA/)
    assert.equal(transactions.container.querySelector('.table-footer'), null)
    assert.equal(calls.length, count)
    await change(accountB, 'same-tab')
    await settle()
    assert.match(portfolio.container.querySelector('output')!.textContent!, /2222/)
    assert.match(transactions.container.querySelector('tbody')!.textContent!, /BBB/)
  } finally { await portfolio.close(); await transactions.close() }
})

test('session expiry hides portfolio, activity and history while the pages stay mounted', async t => {
  let now = Date.parse('2026-10-04T12:00:00Z')
  t.mock.method(Date, 'now', () => now)
  const expiryCallbacks: Array<() => void> = []
  const originalTimeout = window.setTimeout.bind(window)
  t.mock.method(window, 'setTimeout', (callback, milliseconds, ...args) => {
    if (milliseconds === 5000 && typeof callback === 'function') expiryCallbacks.push(callback as () => void)
    return originalTimeout(callback, milliseconds, ...args)
  })
  window.localStorage.clear()
  saveAuthSession({ ...accountA, expiresAtUtc: new Date(now + 5000).toISOString() })
  const calls = apiFixture(t)
  const portfolio = await mount(React.createElement(PortfolioProbe))
  const transactions = await mount(React.createElement(TransactionsPage))
  try {
    await settle()
    assert.match(portfolio.container.querySelector('output')!.textContent!, /1111/)
    assert.match(transactions.container.querySelector('tbody')!.textContent!, /AAA/)
    const count = calls.length
    assert.ok(expiryCallbacks.length > 0)
    now += 5000
    await act(async () => expiryCallbacks.forEach(callback => callback()))
    assert.equal(portfolio.container.querySelector('output')!.textContent, 'null')
    assert.equal(portfolio.container.querySelector('[data-loading]')!.getAttribute('data-loading'), 'false')
    assert.doesNotMatch(transactions.container.querySelector('tbody')!.textContent!, /AAA/)
    assert.equal(transactions.container.querySelector('.table-footer'), null)
    assert.match(transactions.container.textContent!, /Please sign in again/)
    assert.equal(calls.length, count)
  } finally { await portfolio.close(); await transactions.close() }
})

test('late old-account history success cannot overwrite the current account totals or rows', async t => {
  seed()
  const old = deferred<Response>()
  apiFixture(t, async (_url, token) => token === 'Bearer token-a' ? old.promise : json({
    items: [tradeB], page: 1, pageSize: 10, totalCount: 1, currency: 'CAD',
    summary: { totalTrades: 1, totalInvested: 2222, totalProceeds: 0 } }))
  const view = await mount(React.createElement(TransactionsPage))
  try {
    await settle()
    await change(accountB)
    await settle()
    await act(async () => old.resolve(json({ items: [tradeA], page: 1, pageSize: 10, totalCount: 1, currency: 'CAD',
      summary: { totalTrades: 1, totalInvested: 1111, totalProceeds: 0 } })))
    assert.match(view.container.querySelector('tbody')!.textContent!, /BBB/)
    assert.doesNotMatch(view.container.querySelector('tbody')!.textContent!, /AAA/)
    assert.match(view.container.querySelector('.summary-grid')!.textContent!, /2,222/)
  } finally { await view.close() }
})
