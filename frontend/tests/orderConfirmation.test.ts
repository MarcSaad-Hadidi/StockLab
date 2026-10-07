import assert from 'node:assert/strict'
import { test, type TestContext } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { authStorageKey, clearAuthSession, saveAuthSession, type AuthSession } from '../src/auth/authStorage.ts'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { createTradingApi, tradingApi, type ExecuteTradeRequest } from '../src/api/tradingApi.ts'

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
await i18n.changeLanguage('en')
const { StockDetailsPage } = await import('../src/market/StockDetailsPage.tsx')
const accountA: AuthSession = { accessToken: 'test-token-a', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Alice Account', email: 'alice@example.com' } }
const accountB: AuthSession = { ...accountA, accessToken: 'test-token-b', user: { id: 'account-b', displayName: 'Bob Account', email: 'bob@example.com' } }
type SentOrder = { authorization: string; order: ExecuteTradeRequest; signal: AbortSignal }
function result(order: ExecuteTradeRequest, cashBalance = 99000) {
  return Response.json({ transactionId: 'test-transaction', orderId: order.orderId, side: order.side, symbol: order.symbol,
    quantity: order.quantity, executionPrice: 100, totalAmount: (order.quantity * 100).toFixed(12), cashBalance: String(cashBalance),
    holdingQuantity: order.quantity, averageCost: 100, executedAtUtc: '2026-10-04T12:00:00Z' })
}
async function mount(t: TestContext, fetcher?: (sent: SentOrder) => Promise<Response>, price = 100) {
  window.localStorage.clear(); saveAuthSession(accountA)
  t.mock.method(marketDataApi, 'quote', async (symbol: string) => ({ symbol, name: 'Audit Stock', exchange: 'NASDAQ', currency: 'USD',
    price, change: null, changePercent: null, volume: null, asOfUtc: '2026-10-04T12:00:00Z', open: null,
    high: null, low: null, previousClose: null, averageVolume: null, isMarketOpen: false, fiftyTwoWeek: null }))
  t.mock.method(marketDataApi, 'history', async () => { throw new Error('test history unavailable') })
  const sent: SentOrder[] = []
  t.mock.method(globalThis, 'fetch', async (_url: string, init: RequestInit) => {
    const request = { authorization: (init.headers as Record<string, string>).Authorization,
      order: JSON.parse(String(init.body)) as ExecuteTradeRequest, signal: init.signal as AbortSignal }
    sent.push(request)
    return fetcher ? fetcher(request) : result(request.order)
  })
  t.mock.method(tradingApi, 'executeTrade', createTradingApi('', globalThis.fetch).executeTrade)
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(StockDetailsPage, { requestedSymbol: 'AAPL', onBack() {} })))
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 25)) })
  return { container, sent,
    dialog: () => container.querySelector('[role="dialog"]'),
    prepare: async () => { await act(async () => container.querySelector('form.stock-trade-card')!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }))) },
    confirm: () => container.querySelector<HTMLButtonElement>('.stock-modal-actions .stock-primary-button')!,
    async close() { await act(async () => root.unmount()); container.remove() },
  }
}

test('an unchanged session sends the prepared order once with its original token', async t => {
  const view = await mount(t)
  try {
    await view.prepare()
    assert.ok(view.dialog())
    await act(async () => { view.confirm().click(); view.confirm().click() })
    assert.equal(view.sent.length, 1)
    assert.equal(view.sent[0].authorization, 'Bearer test-token-a')
    assert.equal(Boolean(view.dialog()), false)
    assert.match(view.container.textContent!, /executed successfully/)
  } finally { await view.close() }
})

for (const language of ['en', 'fr']) {
  for (const [price, quantity, total, displayed] of [
    [1.2345, .5, '0.61725', '0.61725'],
    [.0001, .00000001, '0.000000000001', '0.000000000001'],
  ] as const) {
    test(`ticket, confirmation and returned cash keep fractional amounts in ${language}: ${total}`, async t => {
      await i18n.changeLanguage(language)
      const cashBalance = '99999.999999999999'
      const view = await mount(t, async ({ order }) => Response.json({ transactionId: 'exact-trade',
        ...order, executionPrice: price, totalAmount: total, cashBalance, holdingQuantity: quantity,
        averageCost: price, executedAtUtc: '2026-10-07T12:00:00Z' }), price)
      try {
        await act(async () => {
          const input = view.container.querySelector<HTMLInputElement>('#stock-quantity')!
          Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(input, String(quantity))
          input.dispatchEvent(new dom.window.Event('input', { bubbles: true }))
        })
        const expected = language === 'fr' ? displayed.replace('.', ',') : displayed
        assert.ok(view.container.querySelector('.stock-trade-summary')!.textContent!.includes(expected))
        await view.prepare()
        assert.ok(view.dialog()!.textContent!.includes(expected))
        await act(async () => view.confirm().click())
        assert.equal(view.sent[0].order.quantity, quantity)
        assert.equal(view.dialog(), null)
        const renderedCash = view.container.querySelector('.stock-cash-row')!.textContent!
        assert.ok(renderedCash.includes(language === 'fr' ? '99\u202f999,999999999999' : '99,999.999999999999'))
      } finally { await view.close(); await i18n.changeLanguage('en') }
    })
  }
}

for (const trigger of ['same-tab', 'storage', 'focus', 'visibility', 'logout', 'clear', 'token'] as const) {
  test(`a prepared order is cancelled on ${trigger} session changes`, async t => {
    const view = await mount(t)
    try {
      await view.prepare()
      assert.ok(view.dialog())
      await act(async () => {
        if (trigger === 'same-tab') saveAuthSession(accountB)
        else if (trigger === 'logout') clearAuthSession()
        else {
          if (trigger === 'clear') window.localStorage.clear()
          else window.localStorage.setItem(authStorageKey, JSON.stringify(trigger === 'token' ? { ...accountA, accessToken: 'rotated-token' } : accountB))
          if (trigger === 'focus') window.dispatchEvent(new window.Event('focus'))
          else if (trigger === 'visibility') {
            Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
            document.dispatchEvent(new window.Event('visibilitychange'))
          } else window.dispatchEvent(new window.StorageEvent('storage', { key: trigger === 'clear' ? null : authStorageKey }))
        }
      })
      assert.equal(Boolean(view.dialog()), false)
      assert.equal(view.sent.length, 0)
      assert.match(view.container.textContent!, /session.*changed.*prepare/i)
      if (trigger === 'same-tab') {
        await view.prepare()
        await act(async () => view.confirm().click())
        assert.equal(view.sent.length, 1)
        assert.equal(view.sent[0].authorization, 'Bearer test-token-b')
      }
    } finally { await view.close() }
  })
}

test('confirmation rechecks storage even before a session event reaches the page', async t => {
  const view = await mount(t)
  try {
    await view.prepare()
    window.localStorage.setItem(authStorageKey, JSON.stringify(accountB))
    await act(async () => view.confirm().click())
    assert.equal(view.sent.length, 0)
    assert.equal(Boolean(view.dialog()), false)
    assert.match(view.container.textContent!, /session.*changed.*prepare/i)
  } finally { await view.close() }
})

test('profile identity edits preserve the confirmation when account and token are unchanged', async t => {
  const view = await mount(t)
  try {
    await view.prepare()
    await act(async () => saveAuthSession({ ...accountA, user: { ...accountA.user, displayName: 'Updated Name', email: 'updated@example.com' } }))
    assert.ok(view.dialog())
    await act(async () => view.confirm().click())
    assert.equal(view.sent.length, 1)
    assert.equal(view.sent[0].authorization, 'Bearer test-token-a')
  } finally { await view.close() }
})

test('a rejected order keeps its confirmation and can be retried without a false success', async t => {
  let attempts = 0
  const view = await mount(t, request => Promise.resolve(++attempts === 1
    ? Response.json({ error: 'insufficient_cash' }, { status: 422 }) : result(request.order)))
  try {
    await view.prepare()
    await act(async () => view.confirm().click())
    assert.ok(view.dialog())
    assert.equal(view.confirm().disabled, false)
    assert.match(view.dialog()!.textContent!, /not enough available cash/i)
    assert.doesNotMatch(view.container.textContent!, /executed successfully/)
    await act(async () => view.confirm().click())
    assert.equal(view.sent.length, 2)
    assert.equal(view.sent[0].order.orderId, view.sent[1].order.orderId)
    assert.equal(Boolean(view.dialog()), false)
    assert.match(view.container.textContent!, /executed successfully/)
  } finally { await view.close() }
})

test('a late response from account A cannot close or update account B confirmation', async t => {
  const releases: Array<() => void> = []
  const view = await mount(t, request => new Promise(resolve => releases.push(() => resolve(result(request.order, request.authorization.endsWith('-a') ? 123 : 456)))))
  try {
    await view.prepare()
    await act(async () => view.confirm().click())
    assert.equal(view.sent.length, 1)
    await act(async () => saveAuthSession(accountB))
    assert.equal(view.sent[0].signal.aborted, true)
    assert.equal(Boolean(view.dialog()), false)
    await view.prepare()
    await act(async () => view.confirm().click())
    assert.equal(view.sent.length, 2)
    assert.equal(view.sent[1].authorization, 'Bearer test-token-b')
    await act(async () => releases[0]())
    assert.ok(view.dialog())
    assert.equal(view.confirm().disabled, true)
    assert.doesNotMatch(view.container.querySelector('.stock-cash-row')!.textContent!, /123/)
    await act(async () => releases[1]())
    assert.equal(Boolean(view.dialog()), false)
    assert.match(view.container.querySelector('.stock-cash-row')!.textContent!, /456/)
  } finally { await view.close() }
})

test('token rotation after the trade commits retains the original request until its response arrives', async t => {
  let release: (() => void) | undefined
  const view = await mount(t, request => new Promise(resolve => { release = () => resolve(result(request.order)) }))
  try {
    await view.prepare()
    await act(async () => view.confirm().click())
    await act(async () => saveAuthSession({ ...accountA, accessToken: 'rotated-token' }))
    assert.ok(view.dialog())
    assert.equal(view.sent[0].signal.aborted, false)
    assert.equal(view.confirm().disabled, true)
    assert.doesNotMatch(view.dialog()!.textContent!, /may already have executed/i)
    await view.prepare()
    assert.equal(view.sent.length, 1)
    await act(async () => release!())
    assert.equal(Boolean(view.dialog()), false)
    assert.match(view.container.textContent!, /executed successfully/)
    assert.equal(view.sent.length, 1)
  } finally { await view.close() }
})

for (const [code, message] of [
  ['insufficient_cash', /not enough available cash/i],
  ['limit_not_reached', /does not meet your limit price/i],
] as const) {
  for (const notify of [true, false]) {
    test(`${code} stays definitive after same-account rotation ${notify ? 'with' : 'without'} a session event`, async t => {
      let release: (() => void) | undefined
      let attempts = 0
      const view = await mount(t, request => ++attempts === 1
        ? new Promise(resolve => { release = () => resolve(Response.json({ error: code }, { status: 422 })) })
        : Promise.resolve(result(request.order)))
      try {
        await view.prepare()
        await act(async () => view.confirm().click())
        const rotated = { ...accountA, accessToken: 'rotated-token' }
        if (notify) await act(async () => saveAuthSession(rotated))
        else window.localStorage.setItem(authStorageKey, JSON.stringify(rotated))
        await act(async () => release!())
        assert.ok(view.dialog())
        assert.match(view.dialog()!.textContent!, message)
        assert.doesNotMatch(view.container.textContent!, /may already have executed|executed successfully/i)
        assert.equal(view.confirm().disabled, false)
        await act(async () => saveAuthSession({ ...rotated, accessToken: 'rotated-again' }))
        assert.match(view.dialog()!.textContent!, message)
        assert.doesNotMatch(view.dialog()!.textContent!, /may already have executed/i)
        await act(async () => view.confirm().click())
        assert.equal(view.sent.length, 2)
        assert.deepEqual(view.sent[1].order, view.sent[0].order)
        assert.equal(view.sent[1].authorization, 'Bearer rotated-again')
      } finally { await view.close() }
    })
  }
}

test('a definitive rejection can be dismissed after rotation without an uncertain-order warning', async t => {
  const view = await mount(t, () => Promise.resolve(Response.json({ error: 'insufficient_cash' }, { status: 422 })))
  try {
    await view.prepare()
    await act(async () => view.confirm().click())
    await act(async () => saveAuthSession({ ...accountA, accessToken: 'rotated-token' }))
    await act(async () => view.container.querySelector<HTMLButtonElement>('.stock-secondary-button')!.click())
    await view.prepare()
    assert.doesNotMatch(view.dialog()!.textContent!, /may already have executed/i)
    assert.equal(view.sent.length, 1)
  } finally { await view.close() }
})

test('the local authorization guard cancels an unsubmitted order without claiming uncertain execution', async t => {
  const view = await mount(t)
  const executeTrade = tradingApi.executeTrade
  t.mock.method(tradingApi, 'executeTrade', (...args: Parameters<typeof tradingApi.executeTrade>) => {
    window.localStorage.setItem(authStorageKey, JSON.stringify({ ...accountA, accessToken: 'rotated-token' }))
    return executeTrade(...args)
  })
  try {
    await view.prepare()
    await act(async () => view.confirm().click())
    assert.equal(view.sent.length, 0)
    assert.equal(Boolean(view.dialog()), false)
    assert.match(view.container.textContent!, /session.*changed.*prepare/i)
    assert.doesNotMatch(view.container.textContent!, /may already have executed/i)
  } finally { await view.close() }
})

for (const lostResponse of [false, true]) {
  test(`a retry stopped before fetching ${lostResponse ? 'preserves an earlier lost outcome' : 'keeps an earlier rejection definitive'}`, async t => {
    const view = await mount(t, () => lostResponse ? Promise.reject(new Error('response lost'))
      : Promise.resolve(Response.json({ error: 'insufficient_cash' }, { status: 422 })))
    try {
      await view.prepare()
      await act(async () => view.confirm().click())
      const executeTrade = tradingApi.executeTrade
      t.mock.method(tradingApi, 'executeTrade', (...args: Parameters<typeof tradingApi.executeTrade>) => {
        window.localStorage.setItem(authStorageKey, JSON.stringify(accountB))
        return executeTrade(...args)
      })
      await act(async () => view.confirm().click())
      assert.equal(view.sent.length, 1)
      assert.equal(Boolean(view.dialog()), false)
      if (lostResponse) assert.match(view.container.textContent!, /may already have executed/i)
      else assert.doesNotMatch(view.container.textContent!, /may already have executed/i)
    } finally { await view.close() }
  })
}

test('a lost committed response can be retried after token rotation using the same order ID and terms', async t => {
  let rejectFirst: (() => void) | undefined
  let executions = 0
  const committed = new Set<string>()
  const view = await mount(t, request => {
    if (!committed.has(request.order.orderId)) { committed.add(request.order.orderId); executions++ }
    if (request.authorization === 'Bearer test-token-a') return new Promise((_, reject) => { rejectFirst = () => reject(new Error('response lost')) })
    return Promise.resolve(result(request.order))
  })
  try {
    await view.prepare()
    await act(async () => view.confirm().click())
    await act(async () => saveAuthSession({ ...accountA, accessToken: 'rotated-token' }))
    await act(async () => rejectFirst!())
    assert.ok(view.dialog())
    assert.equal(view.confirm().disabled, false)
    assert.match(view.dialog()!.textContent!, /same order.*original reference/i)
    await act(async () => { view.confirm().click(); view.confirm().click() })
    assert.equal(view.sent.length, 2)
    assert.deepEqual(view.sent[1].order, view.sent[0].order)
    assert.equal(view.sent[1].authorization, 'Bearer rotated-token')
    assert.equal(executions, 1)
    assert.equal(Boolean(view.dialog()), false)
  } finally { await view.close() }
})

test('confirmation preserves a submitted order when the rotated session event has not arrived yet', async t => {
  let attempts = 0
  const view = await mount(t, request => ++attempts === 1
    ? Promise.reject(new Error('response lost')) : Promise.resolve(result(request.order)))
  try {
    await view.prepare()
    await act(async () => view.confirm().click())
    window.localStorage.setItem(authStorageKey, JSON.stringify({ ...accountA, accessToken: 'rotated-token' }))
    await act(async () => view.confirm().click())
    assert.ok(view.dialog())
    assert.equal(view.sent.length, 1)
    await act(async () => view.confirm().click())
    assert.deepEqual(view.sent[1].order, view.sent[0].order)
    assert.equal(view.sent[1].authorization, 'Bearer rotated-token')
  } finally { await view.close() }
})

test('changing accounts after submission warns that the original trade may already have executed', async t => {
  let release: (() => void) | undefined
  const view = await mount(t, request => new Promise(resolve => { release = () => resolve(result(request.order)) }))
  try {
    await view.prepare()
    await act(async () => view.confirm().click())
    await act(async () => saveAuthSession(accountB))
    assert.equal(Boolean(view.dialog()), false)
    assert.match(view.container.textContent!, /may already have executed.*transactions.*another order/i)
    assert.doesNotMatch(view.container.textContent!, /prepare your order again/i)
    await act(async () => release!())
    assert.doesNotMatch(view.container.textContent!, /executed successfully/)
  } finally { await view.close() }
})

test('dismissing an uncertain submitted order keeps the warning when preparing a different order', async t => {
  const view = await mount(t, () => Promise.reject(new Error('response lost')))
  try {
    await view.prepare()
    await act(async () => view.confirm().click())
    await act(async () => saveAuthSession({ ...accountA, accessToken: 'rotated-token' }))
    await act(async () => view.container.querySelector<HTMLButtonElement>('.stock-secondary-button')!.click())
    assert.match(view.container.textContent!, /may already have executed.*transactions.*another order/i)
    await view.prepare()
    assert.match(view.dialog()!.textContent!, /may already have executed.*transactions.*another order/i)
    assert.equal(view.sent.length, 1)
  } finally { await view.close() }
})

test('session expiry closes the confirmation while the page stays open', async t => {
  let now = Date.parse('2026-10-04T12:00:00Z')
  t.mock.method(Date, 'now', () => now)
  const timers = new Map<number, () => void>()
  let timerId = 0
  t.mock.method(window, 'setTimeout', (callback: () => void) => { timers.set(++timerId, callback); return timerId })
  t.mock.method(window, 'clearTimeout', (id: number) => { timers.delete(id) })
  const view = await mount(t)
  try {
    await act(async () => saveAuthSession({ ...accountA, expiresAtUtc: new Date(now + 5000).toISOString() }))
    await view.prepare()
    assert.ok(view.dialog())
    now += 5000
    await act(async () => { for (const callback of [...timers.values()]) callback() })
    assert.equal(Boolean(view.dialog()), false)
    assert.equal(view.sent.length, 0)
    assert.equal(view.container.querySelector<HTMLButtonElement>('.stock-trade-submit')!.disabled, true)
  } finally { await view.close() }
})

test('unmount aborts an order request and ignores its late response', async t => {
  let release: (() => void) | undefined
  const view = await mount(t, request => new Promise(resolve => { release = () => resolve(result(request.order)) }))
  await view.prepare()
  await act(async () => view.confirm().click())
  await view.close()
  assert.equal(view.sent[0].signal.aborted, true)
  await act(async () => release!())
})
