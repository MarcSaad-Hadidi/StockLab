import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { saveAuthSession, getAuthSession, clearAuthSession, authStorageKey } from '../src/auth/authStorage.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/profile/' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, FormData: dom.window.FormData, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
await i18n.changeLanguage('en')
const session = { accessToken: 'test-token', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z', user: { id: 'd4fd8828-1a30-46f7-86c0-8cbbfa644f66', displayName: 'Test User', email: 'test@example.com' } }

test('password form waits for the server and reports failure without a success message', async (t) => {
  saveAuthSession(session)
  const { profileApi } = await import('../src/api/profileApi.ts')
  const { portfolioApi } = await import('../src/api/portfolioApi.ts')
  t.mock.method(profileApi, 'getProfile', async () => ({ ...session.user, createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-01T00:00:00Z' }))
  t.mock.method(portfolioApi, 'getPortfolio', async () => ({ cashBalance: '10000', initialCapital: '10000', investedValue: '0', totalValue: '10000', currency: 'CAD', positions: [] }))
  let resolve!: (value: Response) => void
  const requests: { url: string; options?: RequestInit }[] = []
  t.mock.method(globalThis, 'fetch', (url: string, options?: RequestInit) => {
    requests.push({ url, options }); return new Promise<Response>(ok => { resolve = ok })
  })
  const { default: Page } = await import('../src/profile/ProfilePage.tsx')
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  try {
    await act(async () => root.render(React.createElement(Page)))
    const change = Array.from(container.querySelectorAll('button')).find(button => button.textContent?.trim() === 'Change Password')!
    await act(async () => change.click())
    const input = (id: string, value: string) => { (container.querySelector(`#${id}`) as HTMLInputElement).value = value }
    input('current-password', 'old-password'); input('new-password', 'new-password'); input('confirm-password', 'new-password')
    await act(async () => container.querySelector('[role="dialog"] form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })))
    assert.equal(requests.length, 1)
    assert.equal(requests[0].url, '/api/auth/password')
    assert.equal(requests[0].options?.method, 'PUT')
    assert.deepEqual(JSON.parse(requests[0].options?.body as string), { currentPassword: 'old-password', newPassword: 'new-password', confirmPassword: 'new-password' })
    assert.ok(container.querySelector('[role="dialog"]'))
    assert.ok((container.querySelector('#current-password') as HTMLInputElement).disabled)
    await act(async () => container.querySelector('[role="dialog"] form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })))
    assert.equal(requests.length, 1, 'a pending request must not be duplicated')
    assert.equal(container.querySelector('.profile-saved'), null)
    await act(async () => resolve(new Response(JSON.stringify({ error: 'invalid_current_password' }), { status: 400 })))
    assert.match(container.querySelector('[role="alert"]')!.textContent!, /current password is incorrect/i)
    assert.equal(container.querySelector('.profile-saved'), null)
    assert.equal(container.querySelector('#current-password')!.getAttribute('aria-invalid'), 'true')
    const current = container.querySelector('#current-password') as HTMLInputElement
    Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(current, 'correct-current-password')
    await act(async () => current.dispatchEvent(new dom.window.Event('input', { bubbles: true })))
    assert.equal(container.querySelector('[role="alert"]'), null)
    assert.equal(current.getAttribute('aria-invalid'), 'false')
    await act(async () => container.querySelector('[role="dialog"] form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })))
    await act(async () => resolve(new Response(null, { status: 204 })))
    assert.equal(container.querySelector('[role="dialog"]'), null)
    assert.match(container.querySelector('.profile-saved')!.textContent!, /Your password has been changed/)
    assert.equal(getAuthSession()?.accessToken, session.accessToken)
  } finally { await act(async () => root.unmount()); container.remove() }
})

const { createPasswordApi, PasswordApiError } = await import('../src/api/passwordApi.ts')
const { PasswordModal } = await import('../src/profile/PasswordModal.tsx')
const payload = { currentPassword: 'old-password', newPassword: 'new-password', confirmPassword: 'new-password' }

for (const [status, body, code] of [
  [400, { error: 'invalid_current_password' }, 'invalid_current_password'],
  [400, { errors: { NewPassword: ['private backend detail'] } }, 'validation_error'],
  [401, {}, 'unauthorized'], [404, {}, 'user_not_found'], [409, {}, 'password_update_conflict'],
  [429, {}, 'rate_limited'], [500, {}, 'server_error'], [200, { success: true }, 'invalid_response'],
] as const) {
  test(`password API rejects ${status} with ${code} and never uses unsafe backend messages`, async () => {
    saveAuthSession(session)
    const api = createPasswordApi('http://test.local/', async () => new Response(JSON.stringify(body), { status }))
    await assert.rejects(api.changePassword(payload), error => error instanceof PasswordApiError && error.code === code && !error.message.includes('private backend detail'))
  })
}
test('password API rejects missing authentication without sending a request and distinguishes offline errors', async () => {
  clearAuthSession()
  let calls = 0
  const api = createPasswordApi('', async () => { calls++; throw new Error('private network detail') })
  await assert.rejects(api.changePassword(payload), error => error instanceof PasswordApiError && error.code === 'unauthorized')
  assert.equal(calls, 0)
  saveAuthSession(session)
  await assert.rejects(api.changePassword(payload), error => error instanceof PasswordApiError && error.code === 'offline')
})
async function modal(onSave: () => void = () => {}, onClose: () => void = () => {}) {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(PasswordModal, { onSave, onClose })))
  const set = (name: string, value: string) => { (container.querySelector(`[name="${name}"]`) as HTMLInputElement).value = value }
  set('current-password', payload.currentPassword); set('new-password', payload.newPassword); set('confirm-password', payload.confirmPassword)
  return { container, set, submit: () => container.querySelector('form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })),
    async close() { await act(async () => root.unmount()); container.remove() } }
}
for (const [field, value] of [['current-password', ''], ['new-password', 'short'], ['confirm-password', 'mismatched-password'], ['new-password', 'old-password']] as const) {
  test(`invalid ${field} never sends a request or reports success`, async t => {
    saveAuthSession(session)
    const fetch = t.mock.method(globalThis, 'fetch', async () => new Response(null, { status: 204 }))
    let saved = false
    const view = await modal(() => { saved = true })
    try {
      view.set(field, value)
      if (value === 'old-password') view.set('confirm-password', value)
      await act(async () => view.submit())
      assert.ok(view.container.querySelector('[role="alert"]'))
      assert.equal(fetch.mock.callCount(), 0)
      assert.equal(saved, false)
    } finally { await view.close() }
  })
}
for (const change of ['logout', 'replacement', 'rotation'] as const) {
  for (const outcome of ['success', 'error'] as const) {
    test(`${change} during a password request ignores a late ${outcome}`, async t => {
      saveAuthSession(session)
      let resolve!: (value: Response) => void
      t.mock.method(globalThis, 'fetch', () => new Promise<Response>(ok => { resolve = ok }))
      let saved = false
      const view = await modal(() => { saved = true })
      try {
        await act(async () => view.submit())
        if (change === 'logout') clearAuthSession()
        else saveAuthSession({ ...session, accessToken: 'new-token', user: change === 'replacement' ? { ...session.user, id: 'other-user' } : session.user })
        await act(async () => {
          window.dispatchEvent(new dom.window.StorageEvent('storage', { key: authStorageKey }))
          resolve(outcome === 'success' ? new Response(null, { status: 204 }) : new Response('{}', { status: 401 }))
        })
        assert.equal(saved, false)
        assert.match(view.container.querySelector('[role="alert"]')!.textContent!, /session changed/i)
        assert.equal((view.container.querySelector('#current-password') as HTMLInputElement).value, '')
        if (change !== 'logout') assert.equal(getAuthSession()?.accessToken, 'new-token')
      } finally { await view.close() }
    })
  }
}

test('closing an unmounted password form aborts the pending request and ignores its result', async t => {
  saveAuthSession(session)
  let signal: AbortSignal | undefined
  let resolve!: (value: Response) => void
  t.mock.method(globalThis, 'fetch', (_url, options?: RequestInit) => {
    signal = options?.signal ?? undefined
    return new Promise<Response>(ok => { resolve = ok })
  })
  let saved = false
  const view = await modal(() => { saved = true })
  await act(async () => view.submit())
  await view.close()
  assert.equal(signal?.aborted, true)
  await act(async () => resolve(new Response(null, { status: 204 })))
  assert.equal(saved, false)
})
