import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { createPortfolioApi, portfolioApi } from '../src/api/portfolioApi.ts'
import { authStorageKey, getAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'

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
const session = { accessToken: 'token-a', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Alice Adams', email: 'alice@example.com' } }
const base = { cashBalance: '1111', initialCapital: '1000', investedValue: '0', totalValue: '1111', currency: 'CAD', positions: [] }
const trade = { id: 'trade-a', symbol: 'AAA', side: 'BUY', quantity: 1, executionPrice: 100,
  totalAmount: '100', executedAtUtc: '2026-01-01T00:00:00Z' }
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
  await act(async () => root.render(React.createElement(DashboardPage)))
  return { container, feed: () => container.querySelector('[aria-labelledby="transactions-title"]')!,
    async close() { await act(async () => root.unmount()); container.remove() } }
}

for (const failure of ['server', 'network', 'invalid-json']) {
  test(`recent activity distinguishes ${failure} failure from a confirmed empty response`, async t => {
    t.mock.method(portfolioApi, 'getPortfolio', async () => base)
    const api = createPortfolioApi('', async () => {
      if (failure === 'network') throw new TypeError('network unavailable')
      if (failure === 'invalid-json') return new Response('not-json')
      return json({ error: 'server_error', details: 'private backend detail' }, 503)
    })
    t.mock.method(portfolioApi, 'getRecentTransactions', api.getRecentTransactions)
    const view = await mount()
    try {
      assert.match(view.container.querySelector('.metrics-grid')!.textContent!, /CA\$1,111\.00/)
      assert.match(view.feed().textContent!, /Unable to load recent transactions/)
      assert.doesNotMatch(view.feed().textContent!, /No transactions|private backend detail|not connected/)
      assert.ok(view.feed().querySelector('[role="alert"]'))
      assert.equal(view.feed().querySelector('button[data-retry-transactions]')?.textContent, 'Retry')
    } finally { await view.close() }
  })
}

test('slow recent activity does not block portfolio values or pretend there are no transactions', async t => {
  t.mock.method(portfolioApi, 'getPortfolio', async () => base)
  const response = deferred<Response>()
  const api = createPortfolioApi('', () => response.promise)
  t.mock.method(portfolioApi, 'getRecentTransactions', api.getRecentTransactions)
  const view = await mount()
  try {
    assert.match(view.container.querySelector('.metrics-grid')!.textContent!, /CA\$1,111\.00/)
    assert.match(view.feed().textContent!, /Loading recent transactions/)
    assert.doesNotMatch(view.feed().textContent!, /No transactions/)
    await act(async () => response.resolve(json([])))
    assert.match(view.feed().textContent!, /No transactions yet/)
    assert.doesNotMatch(view.feed().textContent!, /Unable to load|not connected|Loading recent/)
    assert.equal(view.feed().querySelector('button[data-retry-transactions]'), null)
  } finally { await view.close() }
})

test('retry loads only the recent feed, disables duplicate clicks and preserves valid portfolio data', async t => {
  let portfolioCalls = 0
  t.mock.method(portfolioApi, 'getPortfolio', async () => { portfolioCalls += 1; return base })
  let activityCalls = 0
  const response = deferred<Response>()
  const api = createPortfolioApi('', async () => ++activityCalls === 1 ? json({}, 503) : response.promise)
  t.mock.method(portfolioApi, 'getRecentTransactions', api.getRecentTransactions)
  const view = await mount()
  try {
    const button = view.feed().querySelector('button[data-retry-transactions]') as HTMLButtonElement
    assert.ok(button)
    await act(async () => { button.click(); button.click() })
    assert.equal(activityCalls, 2)
    assert.equal(portfolioCalls, 1)
    assert.match(view.container.querySelector('.metrics-grid')!.textContent!, /CA\$1,111\.00/)
    assert.match(view.feed().textContent!, /Loading recent transactions/)
    assert.ok((view.feed().querySelector('button[data-retry-transactions]') as HTMLButtonElement).disabled)
    assert.doesNotMatch(view.feed().textContent!, /No transactions|Unable to load/)
    await act(async () => response.resolve(json([trade])))
    assert.match(view.feed().textContent!, /AAA/)
    assert.equal(view.feed().querySelector('[role="alert"]'), null)
    assert.equal(view.feed().querySelector('button[data-retry-transactions]'), null)
    assert.equal(portfolioCalls, 1)
  } finally { await view.close() }
})

test('unauthorized activity explains sign-in and offers no blind retry', async t => {
  t.mock.method(portfolioApi, 'getPortfolio', async () => base)
  const api = createPortfolioApi('', async () => json({}, 401))
  t.mock.method(portfolioApi, 'getRecentTransactions', api.getRecentTransactions)
  const view = await mount()
  try {
    assert.match(view.feed().textContent!, /Please sign in again/)
    assert.equal(view.feed().querySelector('a[href="/login"]')?.textContent, 'Sign in')
    assert.equal(view.feed().querySelector('button[data-retry-transactions]'), null)
    assert.doesNotMatch(view.feed().textContent!, /No transactions/)
  } finally { await view.close() }
})

for (const responseStatus of [200, 401]) {
  test(`a late retry response (${responseStatus}) cannot replace another account's activity or session`, async t => {
    t.mock.method(portfolioApi, 'getPortfolio', async () => ({ ...base, cashBalance: getAuthSession()?.user.id === 'account-a' ? '1111' : '2222' }))
    const response = deferred<Response>()
    let callsA = 0
    const api = createPortfolioApi('', async (_input, init) => {
      const token = (init?.headers as Record<string, string>).Authorization
      if (token === 'Bearer token-b') return json([{ ...trade, id: 'trade-b', symbol: 'BBB' }])
      return ++callsA === 1 ? json({}, 503) : response.promise
    })
    t.mock.method(portfolioApi, 'getRecentTransactions', api.getRecentTransactions)
    const view = await mount()
    try {
      await act(async () => (view.feed().querySelector('button[data-retry-transactions]') as HTMLButtonElement).click())
      await act(async () => {
        window.localStorage.setItem(authStorageKey, JSON.stringify({ ...session, accessToken: 'token-b', user: { ...session.user, id: 'account-b', displayName: 'Bob Brown' } }))
        window.dispatchEvent(new dom.window.StorageEvent('storage', { key: authStorageKey }))
      })
      await act(async () => response.resolve(json(responseStatus === 200 ? [trade] : {}, responseStatus)))
      assert.match(view.feed().textContent!, /BBB/)
      assert.doesNotMatch(view.feed().textContent!, /AAA|Please sign in again|Unable to load/)
      assert.match(view.container.querySelector('.metrics-grid')!.textContent!, /CA\$2,222\.00/)
      assert.equal(getAuthSession()?.user.id, 'account-b')
    } finally { await view.close() }
  })
}

test('French recent-activity errors and retry remain translated', async t => {
  await i18n.changeLanguage('fr')
  t.mock.method(portfolioApi, 'getPortfolio', async () => base)
  const api = createPortfolioApi('', async () => json({}, 503))
  t.mock.method(portfolioApi, 'getRecentTransactions', api.getRecentTransactions)
  const view = await mount()
  try {
    assert.match(view.feed().textContent!, /Impossible de charger les transactions récentes/)
    assert.equal(view.feed().querySelector('button[data-retry-transactions]')?.textContent, 'Réessayer')
    assert.doesNotMatch(view.feed().textContent!, /Aucune transaction|Unable to load/)
  } finally { await view.close() }
})
