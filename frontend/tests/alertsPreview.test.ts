import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/alerts' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { default: AlertsPage } = await import('../src/alerts/AlertsPage.tsx')
const { initialAlerts } = await import('../src/alerts/alertsData.ts')
beforeEach(() => { window.localStorage.clear(); initialAlerts.length = 0 })

for (const language of ['en', 'fr']) {
  test(`unconnected alerts are a clearly explained, non-submittable preview in ${language}`, async t => {
    await i18n.changeLanguage(language)
    const fetch = t.mock.method(globalThis, 'fetch', async () => { throw new Error('Preview must not make API requests') })
    const container = document.createElement('div'); document.body.append(container)
    const root = createRoot(container)
    try {
      await act(async () => root.render(React.createElement(AlertsPage)))
      const notice = container.querySelector('#alerts-unavailable-description')!
      assert.match(notice.textContent!, language === 'en' ? /read-only preview/ : /lecture seule/)
      assert.match(notice.textContent!, language === 'en' ? /cannot be created, changed or saved/ : /impossible de créer, modifier ou enregistrer/)
      const preview = container.querySelector('#create-alert-form')!
      const controls = [...preview.querySelectorAll<HTMLInputElement | HTMLButtonElement | HTMLSelectElement>('.alert-preview-fields input, .alert-preview-fields button, .alert-preview-fields select')]
      assert.ok(controls.length >= 7)
      for (const control of controls) {
        assert.ok(control.matches(':disabled'), `${control.outerHTML} must be natively disabled`)
        control.focus()
        assert.notEqual(document.activeElement, control)
        await act(async () => { control.click(); control.click() })
      }
      assert.equal(preview.querySelector('form'), null, 'no implicit Enter submission path')
      assert.equal(container.querySelector('button[type="submit"]'), null)
      assert.ok(container.querySelector('.alerts-heading button:disabled'))
      assert.ok(container.querySelector('.pagination select:disabled'))
      assert.equal(container.querySelector('input:checked'), null, 'no notification channel is presented as active')
      assert.equal(container.querySelector('[role="dialog"]'), null)
      assert.equal(container.querySelector('.toast'), null)
      assert.equal(fetch.mock.callCount(), 0)
      assert.ok(container.querySelector('a[href="/market"]'), 'real market navigation remains available')
      assert.ok(container.querySelector('.how-alerts-card')?.textContent?.trim())
      const tabs = [...container.querySelectorAll<HTMLButtonElement>('[role="tab"]')]
      await act(async () => tabs[1].click())
      assert.equal(tabs[1].getAttribute('aria-selected'), 'true', 'read-only filters remain usable')
      assert.ok(container.querySelector('.empty-state'))
    } finally { await act(async () => root.unmount()); container.remove() }
  })
}

test('even a preview row cannot be edited, toggled or deleted without persistence', async () => {
  await i18n.changeLanguage('en')
  initialAlerts.push({ id: 'preview', symbol: 'TEST', name: 'Test fixture', exchange: 'TEST', condition: 'above', targetPrice: 12, lastPrice: 10, status: 'active', createdAt: '2026-11-12T00:00:00Z' })
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  try {
    await act(async () => root.render(React.createElement(AlertsPage)))
    const actions = [...container.querySelectorAll<HTMLButtonElement>('.alert-actions button')]
    assert.equal(actions.length, 3)
    for (const button of actions) {
      assert.equal(button.disabled, true)
      await act(async () => { button.click(); button.click() })
    }
    assert.equal(initialAlerts.length, 1)
    assert.equal(initialAlerts[0].status, 'active')
    assert.equal(container.querySelector('[role="dialog"]'), null)
    assert.equal(container.querySelector('.toast'), null)
  } finally { await act(async () => root.unmount()); container.remove(); initialAlerts.length = 0 }
})
