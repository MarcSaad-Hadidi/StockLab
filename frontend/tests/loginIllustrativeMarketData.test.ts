import assert from 'node:assert/strict'
import { test } from 'node:test'
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
const dom = new JSDOM('', { url: 'http://localhost/login' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document,
  FormData: dom.window.FormData, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { default: LoginPage } = await import('../src/login/LoginPage.tsx')

test('login keeps all four market samples explicitly illustrative across language changes', async t => {
  const fetch = t.mock.method(globalThis, 'fetch', async () => { throw new Error('Decorative samples must not fetch quotes') })
  const container = document.createElement('div')
  document.body.append(container)
  const root = createRoot(container)
  try {
    await act(async () => root.render(React.createElement(LoginPage)))
    for (const language of ['en', 'fr', 'en']) {
      await act(async () => { await i18n.changeLanguage(language) })
      const samples = [...container.querySelectorAll('.quote-card')]
      assert.deepEqual(samples.map(card => card.querySelector('strong')!.textContent), ['AAPL', 'TSLA', 'MSFT', 'NVDA'])
      for (const card of samples) {
        assert.equal(card.querySelector('small')!.textContent, language === 'fr'
          ? 'Illustration · Pas en temps réel' : 'Illustrative only · Not live')
        assert.ok(card.closest('[aria-hidden="true"]'), 'pure decoration stays out of the accessibility tree')
        assert.equal(card.querySelector('[aria-live], [role="status"], time, button, a'), null)
      }
      assert.equal(container.querySelectorAll('input').length, 2, 'login fields remain available')
      assert.equal(container.querySelector('button[type="submit"]')!.hasAttribute('disabled'), false)
    }
    assert.equal(fetch.mock.callCount(), 0)
  } finally {
    await act(async () => root.unmount())
    container.remove()
  }
})

test('login hides market samples where the form would obscure their disclosure', () => {
  const css = readFileSync(new URL('../src/login/login.css', import.meta.url), 'utf8')
  assert.match(css, /@media \(max-width: 1100px\)\s*\{\s*\.market-decoration\s*\{\s*display: none;/)
})
