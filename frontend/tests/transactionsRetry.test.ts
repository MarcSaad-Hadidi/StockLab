import assert from 'node:assert/strict'
import { beforeEach, test, type TestContext } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { createPortfolioApi, portfolioApi } from '../src/api/portfolioApi.ts'
import { clearAuthSession, getAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'

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
const { TransactionsPage } = await import('../src/transactions/TransactionsPage.tsx')
const session = { accessToken: 'token-a', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Account A', email: 'a@example.test' } }
beforeEach(async () => { window.localStorage.clear(); saveAuthSession(session); await i18n.changeLanguage('en') })
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
function history(symbol = 'AAA', page = 1, totalCount = 1) {
  return Response.json({ items: totalCount === 0 ? [] : [{ id: `trade-${symbol}`, symbol, side: 'BUY', quantity: '0.99999999', executionPrice: '123.4567',
    totalAmount: '123.456698765433', executedAtUtc: '2026-10-26T12:00:00Z' }], page, pageSize: 10, totalCount, currency: 'CAD',
  summary: { totalTrades: totalCount, totalInvested: '123.456698765433', totalProceeds: '0' } })
}
type Request = { url: string; signal: AbortSignal; authorization: string }
async function mount(t: TestContext, handler: (request: Request, index: number) => Promise<Response>) {
  let now = Date.parse('2026-10-26T12:00:00Z')
  let nextId = 0
  const timers = new Map<number, { due: number; run: () => void }>()
  t.mock.method(Date, 'now', () => now)
  t.mock.method(window, 'setTimeout', (callback, delay = 0) => {
    const id = ++nextId; timers.set(id, { due: now + delay, run: callback as () => void }); return id
  })
  t.mock.method(window, 'clearTimeout', id => { timers.delete(id!) })
  const requests: Request[] = []
  t.mock.method(portfolioApi, 'getTransactionHistory', createPortfolioApi('', async (url, init) => {
    const request = { url: String(url), signal: init!.signal as AbortSignal, authorization: (init!.headers as Record<string, string>).Authorization }
    requests.push(request)
    return handler(request, requests.length)
  }).getTransactionHistory)
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(TransactionsPage)))
  const tick = async () => {
    now += 150
    await act(async () => { for (const [id, timer] of [...timers]) if (timer.due <= now) { timers.delete(id); timer.run() } })
  }
  const panel = () => container.querySelector('.transactions-panel')!
  return { container, requests, timers, panel, tick,
    retry: () => panel().querySelector<HTMLButtonElement>('[data-retry-transactions]'),
    error: () => panel().querySelector('[role="alert"]'),
    async clickRetry() { assert.ok(this.retry(), 'a failed load must offer Retry'); await act(async () => this.retry()!.click()) },
    async fill(label: string, value: string) {
      const input = container.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)!
      assert.ok(input)
      await act(async () => {
        Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(input, value)
        input.dispatchEvent(new dom.window.Event('input', { bubbles: true }))
      })
    },
    async close() { await act(async () => root.unmount()); container.remove() },
  }
}

for (const language of ['en', 'fr']) {
  test(`Retry clears the old error and restores real rows, totals and pagination in ${language}`, async t => {
    await i18n.changeLanguage(language)
    const pending = deferred<Response>()
    const view = await mount(t, async (_request, index) => index === 1 ? new Response(null, { status: 503 }) : pending.promise)
    try {
      await view.tick()
      assert.match(view.panel().textContent!, language === 'en' ? /Unable to load transactions/ : /Impossible de charger les transactions/)
      assert.ok(view.retry(), 'a failed load must offer Retry')
      assert.equal(view.retry()!.textContent, language === 'en' ? 'Retry' : 'Réessayer')
      assert.equal(view.retry()!.closest('.table-scroll'), null, 'the recovery action must not scroll away with wide columns')
      await act(async () => { view.retry()!.click(); view.retry()!.click() })
      assert.equal(view.error(), null)
      assert.equal(view.panel().getAttribute('aria-busy'), 'true')
      await view.tick()
      assert.equal(view.requests.length, 2, 'rapid clicks submit only one retry')
      await act(async () => pending.resolve(history()))
      assert.equal(view.error(), null)
      assert.equal(view.retry(), null)
      assert.equal(view.panel().getAttribute('aria-busy'), 'false')
      assert.match(view.container.querySelector('tbody')!.textContent!, /AAA/)
      assert.match(view.container.querySelector('.summary-grid')!.textContent!, /123[.,]456698765433/)
      assert.ok(view.container.querySelector('.table-footer'))
      assert.equal(window.location.pathname, '/transactions', 'recovery never reloads or navigates the document')
    } finally { await view.close() }
  })
}

test('a repeated retry failure remains recoverable and never displays an empty success', async t => {
  const view = await mount(t, async (_request, index) => index < 3 ? new Response(null, { status: 503 }) : history())
  try {
    await view.tick(); await view.clickRetry(); await view.tick()
    assert.match(view.error()!.textContent!, /Unable to load transactions/)
    assert.ok(view.retry())
    assert.equal(view.container.querySelector('.table-footer'), null)
    await view.clickRetry(); await view.tick()
    assert.match(view.container.querySelector('tbody')!.textContent!, /AAA/)
    assert.equal(view.error(), null)
    assert.equal(view.requests.length, 3)
  } finally { await view.close() }
})

for (const failure of ['offline', 'invalid response']) {
  test(`${failure} is recoverable using the same Retry action`, async t => {
    const view = await mount(t, async (_request, index) => {
      if (index > 1) return history()
      if (failure === 'offline') throw new TypeError('offline')
      return Response.json({ items: [] })
    })
    try { await view.tick(); await view.clickRetry(); await view.tick(); assert.match(view.container.querySelector('tbody')!.textContent!, /AAA/) }
    finally { await view.close() }
  })
}

test('initial loading and a confirmed empty history never offer Retry', async t => {
  const pending = deferred<Response>()
  const view = await mount(t, () => pending.promise)
  try {
    assert.equal(view.retry(), null)
    await view.tick()
    assert.equal(view.retry(), null)
    await act(async () => pending.resolve(history('AAA', 1, 0)))
    assert.match(view.panel().textContent!, /No transactions found/)
    assert.equal(view.retry(), null)
    assert.equal(view.error(), null)
  } finally { await view.close() }
})

test('Retry preserves the current search, date range, side and failed page', async t => {
  let failPageTwo = true
  const view = await mount(t, async ({ url }) => {
    const page = Number(new URL(url, 'http://localhost').searchParams.get('page'))
    if (page === 2 && failPageTwo) return new Response(null, { status: 503 })
    return history('AAA', page, 21)
  })
  try {
    await view.tick()
    await view.fill('Search transactions by symbol', 'AAA')
    await view.fill('Filter from date', '2026-10-01'); await view.fill('Filter to date', '2026-10-26')
    await act(async () => view.container.querySelector<HTMLButtonElement>('.action-filter-buy')!.click())
    await view.tick()
    await act(async () => view.container.querySelector<HTMLButtonElement>('[aria-label="Next page"]')!.click())
    await view.tick()
    const failed = view.requests.at(-1)!
    assert.equal(failed.url, '/api/portfolio/transactions/history?page=2&pageSize=10&search=AAA&side=BUY&from=2026-10-01&to=2026-10-26')
    failPageTwo = false
    await view.clickRetry(); await view.tick()
    assert.equal(view.requests.at(-1)!.url, failed.url)
    assert.equal(view.requests.at(-1)!.authorization, 'Bearer token-a')
    assert.equal(view.container.querySelector('.pagination [aria-current="page"]')!.textContent, '2')
  } finally { await view.close() }
})

test('an invalid date range clears the old server error and never sends a futile retry', async t => {
  const view = await mount(t, async () => new Response(null, { status: 503 }))
  try {
    await view.tick()
    await view.fill('Filter from date', '2026-10-26'); await view.fill('Filter to date', '2026-10-01')
    await view.tick()
    assert.match(view.panel().textContent!, /end date must be on or after/)
    assert.equal(view.retry(), null)
    assert.equal(view.error(), null)
    assert.equal(view.requests.length, 1)
  } finally { await view.close() }
})

for (const missing of [false, true]) {
  test(`${missing ? 'missing' : 'rejected'} authentication offers sign in instead of Retry`, async t => {
    if (missing) clearAuthSession()
    const view = await mount(t, async () => new Response(null, { status: 401 }))
    try {
      await view.tick()
      assert.match(view.panel().textContent!, /Please sign in again/)
      assert.ok(view.panel().querySelector('a[href="/login"]'))
      assert.equal(view.retry(), null)
      assert.equal(view.requests.length, missing ? 0 : 1)
    } finally { await view.close() }
  })
}

for (const change of ['logout', 'account replacement', 'token rotation', 'filter']) {
  test(`a pending retry cannot republish stale rows after ${change}`, async t => {
    const pending = deferred<Response>()
    const view = await mount(t, async (_request, index) => index === 1 ? new Response(null, { status: 503 }) : index === 2 ? pending.promise : history('BBB'))
    try {
      await view.tick(); await view.clickRetry(); await view.tick()
      const retryRequest = view.requests[1]
      if (change === 'filter') await view.fill('Search transactions by symbol', 'BBB')
      else await act(async () => {
        if (change === 'logout') clearAuthSession()
        else saveAuthSession({ ...session, accessToken: 'token-b', user: { ...session.user, id: change === 'account replacement' ? 'account-b' : session.user.id } })
      })
      assert.equal(retryRequest.signal.aborted, true)
      await view.tick()
      await act(async () => pending.resolve(history('AAA')))
      assert.doesNotMatch(view.container.textContent!, /trade-AAA|AAA/)
      if (change === 'logout') assert.equal(view.requests.length, 2)
      else {
        assert.match(view.container.querySelector('tbody')!.textContent!, /BBB/)
        assert.equal(view.requests.at(-1)!.authorization, change === 'filter' ? 'Bearer token-a' : 'Bearer token-b')
        assert.equal(getAuthSession()?.accessToken, change === 'filter' ? 'token-a' : 'token-b')
      }
    } finally { await view.close() }
  })
}

test('unmount aborts an in-flight retry and clears all page timers', async t => {
  const pending = deferred<Response>()
  const view = await mount(t, async (_request, index) => index === 1 ? new Response(null, { status: 503 }) : pending.promise)
  await view.tick(); await view.clickRetry(); await view.tick()
  await view.close()
  assert.equal(view.requests[1].signal.aborted, true)
  assert.equal(view.timers.size, 0)
  await act(async () => pending.resolve(history()))
  assert.equal(view.container.textContent, '')
})
