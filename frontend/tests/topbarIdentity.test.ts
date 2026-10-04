import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { authStorageKey, saveAuthSession, clearAuthSession } from '../src/auth/authStorage.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/profile/' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
await i18n.changeLanguage('en')
const { TopBar } = await import('../src/components/layout/TopBar.tsx')
const session = { accessToken: 'test-token', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z', user: { id: 'test-id', displayName: 'Samira Martin', email: 'samira@example.com' } }
async function mount() {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(TopBar, { title: 'Profile', onMenuOpen: () => {} })))
  return { container, avatar: () => container.querySelector('.app-topbar-avatar')!, async close() { await act(async () => root.unmount()); container.remove() } }
}

test('TopBar reads the authenticated identity and follows profile edits, login and logout in the same tab', async () => {
  window.localStorage.clear(); saveAuthSession(session)
  const view = await mount()
  try {
    assert.equal(view.avatar().textContent, 'SM')
    await act(async () => saveAuthSession({ ...session, user: { ...session.user, displayName: 'Ghaith Hadidi', email: 'ghaith@example.com' } }))
    assert.equal(view.avatar().textContent, 'GH')
    assert.match(view.container.querySelector('.app-topbar-account')!.getAttribute('title')!, /Ghaith Hadidi/)
    await act(async () => clearAuthSession())
    assert.equal(view.avatar().textContent, '')
    assert.ok(view.avatar().querySelector('svg'))
    await act(async () => saveAuthSession(session))
    assert.equal(view.avatar().textContent, 'SM')
  } finally { await view.close() }
})

for (const [displayName, expected] of [
  ['  Samira   Martin  ', 'SM'], ['Ghaith', 'G'], ['Jean-Luc Picard', 'JP'], ['Ada Mary Lovelace', 'AL'],
  ['Élodie Laurent', 'ÉL'], ['E\u0301lodie Laurent', 'ÉL'], ['李 雷', '李雷'], ['123 !!!', ''],
] as const) {
  test(`TopBar derives initials from ${displayName}`, async () => {
    window.localStorage.clear(); saveAuthSession({ ...session, user: { ...session.user, displayName } })
    const view = await mount()
    try { assert.equal(view.avatar().textContent, expected); if (!expected) assert.ok(view.avatar().querySelector('svg')) }
    finally { await view.close() }
  })
}
for (const trigger of ['storage', 'focus', 'visibility'] as const) {
  test(`TopBar refreshes external account changes on ${trigger}`, async () => {
    window.localStorage.clear(); saveAuthSession(session)
    const view = await mount()
    try {
      window.localStorage.setItem(authStorageKey, JSON.stringify({ ...session, user: { ...session.user, id: 'other-id', displayName: 'Other Account', email: 'other@example.com' } }))
      await act(async () => {
        if (trigger === 'storage') window.dispatchEvent(new dom.window.StorageEvent('storage', { key: authStorageKey }))
        else if (trigger === 'focus') window.dispatchEvent(new dom.window.Event('focus'))
        else {
          Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
          document.dispatchEvent(new dom.window.Event('visibilitychange'))
        }
      })
      assert.equal(view.avatar().textContent, 'OA')
      window.localStorage.clear()
      await act(async () => window.dispatchEvent(new dom.window.StorageEvent('storage', { key: null })))
      assert.equal(view.avatar().textContent, '')
      assert.ok(view.avatar().querySelector('svg'))
    } finally { await view.close() }
  })
}
for (const raw of [null, '{}', 'not-json', JSON.stringify({ ...session, user: { ...session.user, displayName: '' } }), JSON.stringify({ ...session, expiresAtUtc: '2000-01-01T00:00:00Z' })]) {
  test(`TopBar uses a neutral icon with absent, invalid or expired identity: ${raw}`, async () => {
    window.localStorage.clear()
    if (raw) window.localStorage.setItem(authStorageKey, raw)
    const view = await mount()
    try { assert.equal(view.avatar().textContent, ''); assert.ok(view.avatar().querySelector('svg')) }
    finally { await view.close() }
  })
}
test('TopBar drops an expired identity while the tab stays open and cancels the expiry timer on unmount', async t => {
  window.localStorage.clear()
  let now = Date.parse('2026-10-04T12:00:00Z')
  t.mock.method(Date, 'now', () => now)
  let timeout: (() => void) | undefined
  let delay: number | undefined
  const clear = t.mock.method(window, 'clearTimeout', () => {})
  t.mock.method(window, 'setTimeout', (callback, milliseconds) => { if (typeof callback === 'function') timeout = callback; delay = milliseconds; return 77 })
  saveAuthSession({ ...session, expiresAtUtc: new Date(now + 5000).toISOString() })
  const view = await mount()
  try {
    assert.equal(view.avatar().textContent, 'SM')
    assert.equal(delay, 5000)
    now += 5000
    await act(async () => timeout!())
    assert.equal(view.avatar().textContent, '')
    assert.ok(view.avatar().querySelector('svg'))
  } finally { await view.close() }
  assert.ok(clear.mock.calls.some(call => call.arguments[0] === 77))
})
test('a blocked or failed auth write cannot leave a previous identity visible', async t => {
  window.localStorage.clear(); saveAuthSession(session)
  const view = await mount()
  try {
    t.mock.method(dom.window.Storage.prototype, 'setItem', () => { throw new Error('storage blocked') })
    await act(async () => { assert.equal(saveAuthSession({ ...session, accessToken: 'new-token' }), false) })
    assert.equal(view.avatar().textContent, '')
  } finally { await view.close() }
})
