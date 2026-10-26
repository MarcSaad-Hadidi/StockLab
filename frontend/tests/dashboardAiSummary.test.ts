import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { clearAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'
import { performance } from './fixtures/aiTrader.ts'
import { aiTraderApi, createAiTraderApi } from '../src/api/aiTraderApi.ts'

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
const page = await import('../src/dashboard/DashboardPage.tsx')
const session = { accessToken: 'summary-a', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Account A', email: 'a@example.test' } }
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }
beforeEach(async t => {
  window.localStorage.clear(); saveAuthSession(session)
  Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
  await i18n.changeLanguage('en')
  t.mock.method(globalThis, 'fetch', async () => json(performance))
  const api = createAiTraderApi('', (...args) => globalThis.fetch(...args))
  t.mock.method(aiTraderApi, 'getPerformance', api.getPerformance)
})
async function mount() {
  assert.equal(typeof page.DashboardAiSummary, 'function', 'Dashboard must expose the connected AI summary')
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(page.DashboardAiSummary)))
  return { container, card: () => container.querySelector('[aria-labelledby="ai-trader-title"]')!,
    refresh: () => container.querySelector('[data-refresh-ai-summary]') as HTMLButtonElement,
    async close() { await act(async () => root.unmount()); container.remove() } }
}

for (const language of ['en', 'fr']) {
  test(`summary loads only performance and formats actual fraction/decimal metrics in ${language}`, async t => {
    const paths: string[] = []
    t.mock.method(globalThis, 'fetch', async (input, init) => {
      paths.push(String(input)); assert.equal((init?.headers as Record<string, string>).Authorization, 'Bearer summary-a')
      return json({ ...performance, totalReturn: -.025, totalPnL: '-2500.1234567890123456789', winRate: .625, executedTradeCount: 12 })
    })
    await i18n.changeLanguage(language)
    const view = await mount()
    try {
      assert.deepEqual(paths, ['/api/ai-trader/performance'])
      const metrics = [...view.card().querySelectorAll('.ai-stat strong')].map(node => node.textContent)
      assert.equal(metrics.length, 4)
      assert.match(metrics[0]!, language === 'en' ? /-2\.50%/ : /-2,50\s*%/)
      assert.match(metrics[1]!, language === 'en' ? /-\$2,500\.1234567890123456789/ : /-2\s*500,1234567890123456789\s*\$US/)
      assert.match(metrics[2]!, language === 'en' ? /62\.5%/ : /62,5\s*%/)
      assert.equal(metrics[3], '12')
      assert.match(view.card().textContent!, /UTC/)
      assert.equal(view.card().querySelector('.status-badge'), null, 'availability must not imply running')
      assert.equal(view.card().querySelector('a')?.getAttribute('href'), '/ai-trader')
    } finally { await view.close() }
  })
}

test('loading differs from a genuine zero-trade portfolio and unknown win rate', async t => {
  const response = deferred<Response>()
  t.mock.method(globalThis, 'fetch', () => response.promise)
  const view = await mount()
  try {
    assert.equal(view.card().getAttribute('aria-busy'), 'true')
    assert.match(view.card().textContent!, /Loading/)
    assert.equal(view.card().querySelector('.ai-stat'), null)
    await act(async () => response.resolve(json({ ...performance, totalReturn: 0, totalPnL: '0', winRate: null,
      executedTradeCount: 0, tradeStatisticsComplete: false, unvaluedLegacySellCount: 1 })))
    const metrics = [...view.card().querySelectorAll('.ai-stat strong')].map(node => node.textContent)
    assert.deepEqual(metrics, ['+0.00%', '+$0.00', '—', '0'])
    assert.equal(view.card().getAttribute('aria-busy'), 'false')
    assert.match(view.card().textContent!, /No trades yet/)
    assert.match(view.card().textContent!, /legacy/i)
  } finally { await view.close() }
})

test('missing portfolio and unavailable providers are explicit and retry refreshes actual values', async t => {
  let status = 404
  t.mock.method(globalThis, 'fetch', async () => status === 200 ? json(performance)
    : json({ error: status === 404 ? 'ai_trader_not_initialized' : 'market_data_provider_unavailable' }, status))
  const view = await mount()
  try {
    assert.match(view.card().querySelector('[role="alert"]')!.textContent!, /not initialized/i)
    assert.equal(view.card().querySelector('.ai-stat'), null)
    status = 503
    await act(async () => (view.card().querySelector('[role="alert"] button') as HTMLButtonElement).click())
    assert.ok(view.card().querySelector('[role="alert"]'))
    assert.equal(view.card().querySelector('.ai-stat'), null)
    status = 200
    await act(async () => (view.card().querySelector('[role="alert"] button') as HTMLButtonElement).click())
    assert.match(view.card().textContent!, /\+8\.00%/)
  } finally { await view.close() }
})

test('focus, visible return, persisted pageshow and manual action revalidate without overlapping reads', async t => {
  let requests = 0
  let next: ReturnType<typeof deferred<Response>> | undefined
  t.mock.method(globalThis, 'fetch', async () => { requests++; return next ? next.promise : json(performance) })
  const view = await mount()
  try {
    next = deferred<Response>()
    await act(async () => { window.dispatchEvent(new dom.window.Event('focus')); document.dispatchEvent(new dom.window.Event('visibilitychange')) })
    assert.equal(requests, 2)
    assert.equal(view.card().querySelector('.ai-stat'), null)
    await act(async () => next!.resolve(json({ ...performance, totalReturn: .12 })))
    assert.match(view.card().textContent!, /\+12\.00%/)
    next = undefined
    await act(async () => view.refresh().click())
    assert.equal(requests, 3)
    await act(async () => window.dispatchEvent(new dom.window.PageTransitionEvent('pageshow', { persisted: true })))
    assert.equal(requests, 4)
  } finally { await view.close() }
})

test('session replacement, logout and rejected authorization cannot expose stale summary values', async t => {
  const old = deferred<Response>()
  const pending: AbortSignal[] = []
  t.mock.method(globalThis, 'fetch', async (_, init) => {
    pending.push(init!.signal as AbortSignal)
    return (init?.headers as Record<string, string>).Authorization === 'Bearer summary-a' ? old.promise : json({ ...performance, totalReturn: .03 })
  })
  const view = await mount()
  try {
    await act(async () => saveAuthSession({ ...session, accessToken: 'summary-b', user: { ...session.user, id: 'account-b' } }))
    assert.ok(pending[0].aborted)
    assert.match(view.card().textContent!, /\+3\.00%/)
    await act(async () => old.resolve(json(performance)))
    assert.doesNotMatch(view.card().textContent!, /\+8\.00%/)
    await act(async () => clearAuthSession())
    assert.equal(view.card().querySelector('.ai-stat'), null)
    assert.match(view.card().textContent!, /sign in/i)
  } finally { await view.close() }
})

test('401 fails closed and does not repeatedly retry a revoked loader', async t => {
  let requests = 0
  t.mock.method(globalThis, 'fetch', async () => { requests++; return json({}, 401) })
  const view = await mount()
  try {
    assert.equal(view.card().querySelector('.ai-stat'), null)
    assert.match(view.card().textContent!, /sign in/i)
    await act(async () => window.dispatchEvent(new dom.window.Event('focus')))
    assert.equal(requests, 1)
  } finally { await view.close() }
})

test('an unsuccessful refresh removes stale metrics and rejects malformed success payloads', async t => {
  let result = json(performance)
  t.mock.method(globalThis, 'fetch', async () => result)
  const view = await mount()
  try {
    assert.match(view.card().textContent!, /\+8\.00%/)
    result = json({}, 503)
    await act(async () => view.refresh().click())
    assert.equal(view.card().querySelector('.ai-stat'), null)
    assert.ok(view.card().querySelector('[role="alert"]'))
    result = json({ ...performance, totalReturn: '8%' })
    await act(async () => (view.card().querySelector('[role="alert"] button') as HTMLButtonElement).click())
    assert.equal(view.card().querySelector('.ai-stat'), null)
    assert.ok(view.card().querySelector('[role="alert"]'))
  } finally { await view.close() }
})

test('idle session expiry removes metrics without waiting for focus or a network response', async t => {
  let now = Date.parse('2026-10-26T12:00:00Z')
  let nextId = 0
  const timers = new Map<number, { due: number; run: () => void }>()
  t.mock.method(Date, 'now', () => now)
  t.mock.method(window, 'setTimeout', (callback, delay = 0) => {
    const id = ++nextId
    timers.set(id, { due: now + delay, run: callback as () => void })
    return id
  })
  t.mock.method(window, 'clearTimeout', id => { timers.delete(id!) })
  saveAuthSession({ ...session, expiresAtUtc: new Date(now + 5000).toISOString() })
  const view = await mount()
  try {
    assert.match(view.card().textContent!, /\+8\.00%/)
    now += 5000
    await act(async () => { for (const [id, timer] of [...timers]) if (timer.due <= now) { timers.delete(id); timer.run() } })
    assert.equal(view.card().querySelector('.ai-stat'), null)
    assert.match(view.card().textContent!, /sign in/i)
  } finally { await view.close() }
})
