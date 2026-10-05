import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { portfolioApi, type TransactionHistoryQuery, type TransactionHistoryResponse } from '../src/api/portfolioApi.ts'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { usePortfolioData, type PortfolioDataState } from '../src/portfolio/usePortfolioData.ts'
import { saveAuthSession } from '../src/auth/authStorage.ts'

registerHooks({
  load(url, context, nextLoad) {
    if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
    if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
      return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
    return nextLoad(url, context)
  },
})
const dom = new JSDOM('', { url: 'http://localhost/transactions' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
await i18n.changeLanguage('en')
beforeEach(() => {
  window.localStorage.clear()
  saveAuthSession({ accessToken: 'portfolio-test-token', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z',
    user: { id: 'portfolio-test-user', displayName: 'Test Account', email: 'portfolio@example.com' } })
})

const base = { cashBalance: 1000, initialCapital: 1000, investedValue: 0, totalValue: 1000, currency: 'CAD', positions: [] }
const trade = { id: 'trade-1', side: 'BUY' as const, symbol: 'AAPL', quantity: 0.25, executionPrice: 1.2345,
  totalAmount: 0.3086, executedAtUtc: '2026-01-01T23:59:59Z' }

async function mount(component: React.ReactElement) {
  const container = document.createElement('div')
  document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(component))
  return { container, async close() { await act(async () => root.unmount()); container.remove() } }
}
async function settle() { await act(async () => { await new Promise(resolve => setTimeout(resolve, 200)) }) }

test('more than twenty positions retain available quotes without inventing aggregate valuation', async (t) => {
  const positions = Array.from({ length: 25 }, (_, i) => ({ symbol: `S${i}`, quantity: 2, averageCost: 100 }))
  t.mock.method(portfolioApi, 'getPortfolio', async () => ({ ...base, positions, investedValue: 5000, totalValue: 6000 }))
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  const calls: string[] = []
  t.mock.method(marketDataApi, 'quote', async (symbol: string) => {
    calls.push(symbol)
    if (symbol === 'S2') throw new Error('quote unavailable')
    return { symbol, name: symbol, price: 150, changePercent: 3, currency: symbol === 'S1' ? 'USD' : 'CAD' }
  })
  let state: PortfolioDataState | undefined
  function Probe() { state = usePortfolioData(); return null }
  const view = await mount(React.createElement(Probe))
  try {
    assert.equal(calls.length, 20)
    const data = state?.data
    assert.ok(data)
    assert.equal(data.positions.length, 25)
    assert.equal(data.positions[0].currentPrice, 150)
    assert.equal(data.positions[0].marketValue, 300)
    assert.equal(data.positions[0].pnl, 100)
    assert.equal(data.positions[0].dailyChangePercent, 3)
    for (const i of [1, 2, 24]) assert.equal(data.positions[i].currentPrice, null)
    assert.equal(data.investedValue, 5000)
    assert.equal(data.totalValue, null)
    assert.equal(data.pnl, null)
    assert.equal(state?.quoteLimit, 20)
    assert.equal(state?.quoteLoadFailed, true)
    assert.ok(data.positions.every(position => position.weight === null))
  } finally { await view.close() }
})

test('a fully sold portfolio retains realized gains and all-time return', async (t) => {
  t.mock.method(portfolioApi, 'getPortfolio', async () => ({ ...base, cashBalance: 1100, totalValue: 1100 }))
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [])
  let state: PortfolioDataState | undefined
  function Probe() { state = usePortfolioData(); return null }
  const view = await mount(React.createElement(Probe))
  try { assert.equal(state?.data?.pnl, 100); assert.equal(state?.data?.returnPercent, 10) }
  finally { await view.close() }
})

test('transactions use server counts, pagination, currency and eight decimal quantities', async (t) => {
  const calls: TransactionHistoryQuery[] = []
  t.mock.method(portfolioApi, 'getTransactionHistory', async (query: TransactionHistoryQuery): Promise<TransactionHistoryResponse> => {
    calls.push(query)
    return { items: [{ ...trade, id: `trade-${query.page}`, quantity: query.page === 1 ? 0.00000001 : 1.75 }],
      page: query.page, pageSize: 10, totalCount: 61, currency: 'CAD',
      summary: { totalTrades: 61, totalInvested: 12000, totalProceeds: 15000 } }
  })
  const { TransactionsPage } = await import('../src/transactions/TransactionsPage.tsx')
  const view = await mount(React.createElement(TransactionsPage))
  try {
    await settle()
    assert.match(view.container.querySelector('.summary-grid')!.textContent!, /CA\$12,000\.00/)
    assert.match(view.container.querySelector('.table-footer')!.textContent!, /of 61 transactions/)
    assert.match(view.container.querySelector('tbody')!.textContent!, /0\.00000001/)
    assert.match(view.container.querySelector('tbody')!.textContent!, /CA\$1\.2345/)
    await act(async () => (view.container.querySelector('[aria-label="Next page"]') as HTMLButtonElement).click())
    await settle()
    assert.equal(calls.at(-1)?.page, 2)
    assert.match(view.container.querySelector('tbody')!.textContent!, /1\.75000000/)
    assert.match(view.container.querySelector('.summary-grid')!.textContent!, /CA\$15,000\.00/)
    await act(async () => (view.container.querySelector('.action-filter-sell') as HTMLButtonElement).click())
    await settle()
    assert.equal(calls.at(-1)?.page, 1)
    assert.equal(calls.at(-1)?.side, 'SELL')
  } finally { await view.close() }
})

test('failed history requests do not masquerade as an empty account with zero totals', async (t) => {
  t.mock.method(portfolioApi, 'getTransactionHistory', async () => { throw new Error('offline') })
  const { TransactionsPage } = await import('../src/transactions/TransactionsPage.tsx')
  const view = await mount(React.createElement(TransactionsPage))
  try {
    await settle()
    assert.match(view.container.textContent!, /Unable to load transactions/)
    assert.doesNotMatch(view.container.querySelector('.summary-grid')!.textContent!, /\$0\.00/)
    assert.equal(view.container.querySelector('.table-footer'), null)
  } finally { await view.close() }
})

test('dashboard preserves distinct transactions for the same symbol and time', async (t) => {
  t.mock.method(portfolioApi, 'getPortfolio', async () => base)
  t.mock.method(portfolioApi, 'getRecentTransactions', async () => [trade, { ...trade, id: 'trade-2', totalAmount: 50 }])
  const errors: string[] = []
  t.mock.method(console, 'error', (...args: unknown[]) => { errors.push(args.join(' ')) })
  const { DashboardPage } = await import('../src/dashboard/DashboardPage.tsx')
  const view = await mount(React.createElement(DashboardPage))
  try {
    assert.equal(view.container.querySelectorAll('.transaction-list .transaction-row').length, 2)
    assert.ok(errors.every(error => !/same key|unique.*key/i.test(error)), errors.join('\n'))
  } finally { await view.close() }
})
