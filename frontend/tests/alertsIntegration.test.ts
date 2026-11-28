import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import { beforeEach, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import type { PriceAlert } from '../src/alerts/alertsData.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && /\.c?js$/.test(url) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/alerts' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { saveAuthSession } = await import('../src/auth/authStorage.ts')
const { default: AlertsPage } = await import('../src/alerts/AlertsPage.tsx')
const { createPriceAlertsApi, priceAlertsApi, PriceAlertApiError } = await import('../src/api/priceAlertsApi.ts')
const { parseAlertTarget } = await import('../src/alerts/alertsData.ts')

const alert: PriceAlert = {
  id: '12345678-0000-0000-0000-000000000001',
  symbol: 'AAPL',
  currency: 'USD',
  condition: 'above',
  targetPrice: '999999999999999.9999',
  status: 'active',
  triggeredPrice: null,
  triggeredAtUtc: null,
  createdAtUtc: '2026-11-12T00:00:00Z',
}

beforeEach(async () => {
  window.localStorage.clear()
  saveAuthSession({
    accessToken: 'test-token', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 60000).toISOString(),
    user: { id: 'test-user', displayName: 'Test User', email: 'test@example.com' },
  })
  await i18n.changeLanguage('en')
})

async function mount() {
  const container = document.createElement('div')
  document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(AlertsPage)))
  return {
    container,
    click: (selector: string) => act(async () => {
      const button = container.querySelector<HTMLButtonElement>(selector)
      assert.ok(button, selector)
      button.click()
    }),
    dispose: async () => {
      await act(async () => root.unmount())
      container.remove()
    },
  }
}

test('loads recorded triggers, displays exact prices and filters the persisted rows', async t => {
  const trigger: PriceAlert = {
    ...alert, id: '12345678-0000-0000-0000-000000000002', symbol: 'MSFT', status: 'triggered',
    triggeredPrice: '500.1234', triggeredAtUtc: '2026-11-13T12:00:00Z',
  }
  t.mock.method(priceAlertsApi, 'list', async () => [alert, trigger])
  const page = await mount()
  try {
    assert.match(page.container.textContent!, /999,999,999,999,999\.9999/)
    await page.click('.alert-tabs button:nth-child(3)')
    assert.equal(page.container.querySelectorAll('.alert-row').length, 1)
    const row = page.container.querySelector('.alert-row')!
    assert.match(row.textContent!, /MSFT.*500\.1234.*Triggered/)
    assert.equal(row.querySelector('time')?.getAttribute('datetime'), trigger.triggeredAtUtc)
    assert.equal(row.querySelectorAll('.alert-actions button').length, 1)
    await act(async () => { await i18n.changeLanguage('fr') })
    assert.match(row.textContent!, /500,1234/)
  } finally { await page.dispose() }
})

test('changes the active state on confirmation and deletes only after confirmation', async t => {
  t.mock.method(priceAlertsApi, 'list', async () => [alert])
  let confirm!: (value: PriceAlert) => void
  const disable = t.mock.method(priceAlertsApi, 'setEnabled',
    () => new Promise<PriceAlert>(resolve => { confirm = resolve }))
  const remove = t.mock.method(priceAlertsApi, 'remove', async () => {})
  const page = await mount()
  try {
    await page.click('[aria-label="Disable AAPL alert"]')
    assert.equal(disable.mock.calls[0].arguments[3], false)
    assert.ok(page.container.querySelector('.status-active'))
    assert.equal(page.container.querySelector('.status-disabled'), null)
    await act(async () => confirm({ ...alert, status: 'disabled' }))
    assert.ok(page.container.querySelector('.status-disabled'))
    assert.ok(page.container.querySelector('[aria-label="Enable AAPL alert"]'))
    await page.click('button[aria-label="Delete AAPL alert"]')
    assert.equal(remove.mock.callCount(), 0)
    await page.click('.alerts-delete .cancel-button')
    assert.equal(remove.mock.callCount(), 0)
    await page.click('button[aria-label="Delete AAPL alert"]')
    await page.click('.alerts-delete .modal-primary')
    assert.equal(remove.mock.calls[0].arguments[2], alert.id)
    assert.equal(page.container.querySelector('.alert-row'), null)
    assert.match(page.container.textContent!, /AAPL alert deleted/)
  } finally { await page.dispose() }
})

test('a failed load remains an error and refresh can recover it', async t => {
  let available = false
  t.mock.method(priceAlertsApi, 'list', async () => {
    if (!available) throw new PriceAlertApiError('offline')
    return [alert]
  })
  const page = await mount()
  try {
    assert.match(page.container.querySelector('[role="alert"]')!.textContent!, /Unable to load alerts/)
    assert.equal(page.container.querySelector('.empty-state'), null)
    available = true
    await page.click('.panel-heading button')
    assert.ok(page.container.querySelector('.alert-row'))
    assert.equal(page.container.querySelector('[role="alert"]'), null)
  } finally { await page.dispose() }
})

test('sends precise localized input as decimal text and uses the exact response field', async () => {
  const price = parseAlertTarget('999999999999999,9999')
  assert.equal(price, alert.targetPrice)
  assert.equal(parseAlertTarget('9999999999999999,99'), null)
  assert.equal(parseAlertTarget('0,00001'), null)
  const fetcher: typeof fetch = async (url, init) => {
    assert.equal(url, '/api/alerts')
    assert.equal(init?.method, 'POST')
    assert.equal(new Headers(init.headers).get('Authorization'), 'Bearer test-token')
    assert.equal(JSON.parse(init.body as string).targetPrice, price)
    return Response.json({
      ...alert, condition: 'Above', status: 'Active', targetPrice: 1000000000000000,
      targetPriceDecimal: price, triggeredPriceDecimal: null,
    }, { status: 201 })
  }
  const api = createPriceAlertsApi('', fetcher)
  const saved = await api.create('Bearer test-token', new AbortController().signal,
    { symbol: 'AAPL', condition: 'above', targetPrice: price! })
  assert.deepEqual(saved, alert)
})
