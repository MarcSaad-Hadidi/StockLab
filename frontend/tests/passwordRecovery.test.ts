import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { getAuthSession, saveAuthSession, clearAuthSession } from '../src/auth/authStorage.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/forgot-password' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, FormData: dom.window.FormData, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const recovery = await import('../src/api/passwordRecoveryApi.ts')
const forgot = await import('../src/recovery/ForgotPasswordPage.tsx')
const reset = await import('../src/recovery/ResetPasswordPage.tsx')
const links = await import('../src/recovery/resetLink.ts')
const userId = 'd4fd8828-1a30-46f7-86c0-8cbbfa644f66'
const token = 'ab'.repeat(32)
const session = { accessToken: 'signed-in-token', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z', user: { id: userId, displayName: 'Samira', email: 'person@example.com' } }
function response(status: number, error?: string, errors?: Record<string, string[]>, retryAfterSeconds?: number) {
  return new Response(status === 204 || status === 202 ? null : JSON.stringify({ error, errors, retryAfterSeconds }), { status })
}
async function mount(kind: 'forgot' | 'reset', language = 'en', fragment = `#userId=${userId}&token=${token}`, entry = false) {
  assert.ok(forgot && reset && links && recovery, 'recovery pages and API must exist')
  window.history.replaceState(null, '', kind === 'forgot' ? '/forgot-password' : `/reset-password${fragment}`)
  await i18n.changeLanguage(language)
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  const credentials = kind === 'reset' ? links.consumeResetLink() : null
  const ResetPage = entry ? reset.ResetPasswordEntry : reset.default
  assert.ok(ResetPage, 'the reset entry must handle same-document fragment navigation')
  await act(async () => root.render(React.createElement(React.StrictMode, {}, kind === 'forgot'
    ? React.createElement(forgot.default)
    : React.createElement(ResetPage, { initialLink: credentials }))))
  const input = (name: string) => container.querySelector<HTMLInputElement>(`input[name="${name}"]`)!
  return { container, input,
    async fill(name: string, value: string) { await act(async () => {
      Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(input(name), value)
      input(name).dispatchEvent(new dom.window.Event('input', { bubbles: true }))
    }) },
    async submit() { await act(async () => container.querySelector('form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }))) },
    async close() { await act(async () => root.unmount()); container.remove(); clearAuthSession() },
  }
}
async function validPassword(view: Awaited<ReturnType<typeof mount>>) {
  await view.fill('newPassword', 'new-password'); await view.fill('confirmPassword', 'new-password')
}
async function followResetLink(nextToken = token) {
  await act(async () => {
    window.history.replaceState(null, '', `/reset-password#userId=${userId}&token=${nextToken}`)
    window.dispatchEvent(new dom.window.HashChangeEvent('hashchange'))
  })
}

test('reset credentials are consumed from the fragment once and removed without browser storage', () => {
  assert.ok(links, 'the reset link reader must exist')
  const before = { local: { ...window.localStorage }, session: { ...window.sessionStorage } }
  window.history.replaceState(null, '', `/reset-password?view=reset#userId=${userId}&token=${token}`)
  assert.deepEqual(links.consumeResetLink(), { userId, token })
  assert.equal(window.location.href, 'http://localhost/reset-password?view=reset')
  assert.equal(links.consumeResetLink(), null)
  assert.deepEqual({ local: { ...window.localStorage }, session: { ...window.sessionStorage } }, before)
})

for (const language of ['en', 'fr']) {
  test(`forgot validates email and shows only a generic accepted message in ${language} without clearing auth`, async t => {
    assert.ok(recovery)
    saveAuthSession(session)
    const requests: unknown[] = []
    t.mock.method(recovery.passwordRecoveryApi, 'forgotPassword', recovery.createPasswordRecoveryApi('', async (_url, init) => {
      requests.push(JSON.parse(String(init?.body))); return response(202)
    }).forgotPassword)
    const view = await mount('forgot', language)
    try {
      await view.fill('email', 'invalid'); await view.submit()
      assert.equal(view.input('email').getAttribute('aria-invalid'), 'true')
      assert.equal(requests.length, 0)
      await view.fill('email', ' person@example.com ')
      assert.equal(view.input('email').getAttribute('aria-invalid'), 'false')
      await view.submit()
      assert.deepEqual(requests, [{ email: 'person@example.com', language }])
      assert.equal(view.container.querySelector('[role="status"]')!.textContent, i18n.t('recovery.forgot.accepted'))
      assert.match(view.container.querySelector('[role="status"]')!.textContent!, language === 'en' ? /if an account/i : /si un compte/i)
      assert.equal(getAuthSession()?.accessToken, session.accessToken)
      assert.equal(view.container.querySelector('a[href="/login"]')?.textContent, i18n.t('recovery.backToLogin'))
    } finally { await view.close() }
  })
}

test('pending recovery submission is disabled and guarded against duplicate submits', async t => {
  assert.ok(recovery)
  let finish!: (value: Response) => void
  let calls = 0
  t.mock.method(recovery.passwordRecoveryApi, 'forgotPassword', recovery.createPasswordRecoveryApi('', () => {
    calls++; return new Promise<Response>(resolve => { finish = resolve })
  }).forgotPassword)
  const view = await mount('forgot')
  try {
    await view.fill('email', 'person@example.com'); await view.submit(); await view.submit()
    assert.equal(calls, 1)
    assert.equal(view.container.querySelector('form')!.getAttribute('aria-busy'), 'true')
    assert.equal(view.input('email').disabled, true)
    assert.equal(view.container.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled, true)
    assert.equal(view.container.querySelector('[role="status"]'), null)
    await act(async () => finish(response(503, 'password_recovery_unavailable')))
    assert.equal(view.container.querySelector('[role="status"]'), null)
    assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('recovery.errors.unavailable'))
  } finally { await view.close() }
})

test('reset validates policy and confirmation, then posts the consumed secret and clears only its matching session on actual success', async t => {
  assert.ok(recovery)
  const requests: unknown[] = []
  t.mock.method(recovery.passwordRecoveryApi, 'resetPassword', recovery.createPasswordRecoveryApi('', async (_url, init) => {
    requests.push(JSON.parse(String(init?.body))); return response(204)
  }).resetPassword)
  saveAuthSession(session)
  const view = await mount('reset')
  try {
    assert.equal(window.location.hash, '')
    await view.fill('newPassword', 'short'); await view.fill('confirmPassword', 'different'); await view.submit()
    assert.equal(requests.length, 0)
    assert.equal(view.input('newPassword').getAttribute('aria-invalid'), 'true')
    assert.equal(view.input('confirmPassword').getAttribute('aria-invalid'), 'true')
    await validPassword(view); await view.submit()
    assert.deepEqual(requests, [{ userId, token, newPassword: 'new-password', confirmPassword: 'new-password' }])
    assert.equal(getAuthSession(), null)
    assert.equal(view.container.querySelector('[role="status"]')!.textContent, i18n.t('recovery.reset.success'))
    assert.ok(view.container.querySelector('a[href="/login"]'))
    assert.equal(view.container.querySelector('form'), null)
  } finally { await view.close() }
})

test('reset leaves a different signed-in account intact', async t => {
  assert.ok(recovery)
  t.mock.method(recovery.passwordRecoveryApi, 'resetPassword', recovery.createPasswordRecoveryApi('', async () => response(204)).resetPassword)
  saveAuthSession({ ...session, user: { ...session.user, id: 'c3fd8828-1a30-46f7-86c0-8cbbfa644f66' } })
  const view = await mount('reset')
  try { await validPassword(view); await view.submit(); assert.equal(getAuthSession()?.accessToken, session.accessToken) }
  finally { await view.close() }
})

test('a delayed reset success cannot clear a newer sign-in token for the recovered account', async t => {
  saveAuthSession(session)
  let finish!: (value: Response) => void
  t.mock.method(recovery.passwordRecoveryApi, 'resetPassword', recovery.createPasswordRecoveryApi('', () =>
    new Promise<Response>(resolve => { finish = resolve })).resetPassword)
  const view = await mount('reset')
  try {
    await validPassword(view); await view.submit()
    saveAuthSession({ ...session, accessToken: 'newly-signed-in-token' })
    await act(async () => finish(response(204)))
    assert.equal(getAuthSession()?.accessToken, 'newly-signed-in-token')
    assert.ok(view.container.querySelector('[role="status"]'))
  } finally { await view.close() }
})

test('reset rejects passwords longer than the backend policy without sending them', async t => {
  let calls = 0
  t.mock.method(recovery.passwordRecoveryApi, 'resetPassword', recovery.createPasswordRecoveryApi('', async () => { calls++; return response(204) }).resetPassword)
  const view = await mount('reset')
  try {
    await view.fill('newPassword', 'a'.repeat(1025)); await view.fill('confirmPassword', 'a'.repeat(1025)); await view.submit()
    assert.equal(calls, 0)
    assert.equal(view.input('newPassword').getAttribute('aria-invalid'), 'true')
    assert.equal(view.input('newPassword').closest('.field-group')!.querySelector('.field-error')!.textContent, i18n.t('recovery.errors.passwordLong'))
  } finally { await view.close() }
})

test('reset rejects a blank-only password while preserving spaces around a valid password', async t => {
  const requests: unknown[] = []
  t.mock.method(recovery.passwordRecoveryApi, 'resetPassword', recovery.createPasswordRecoveryApi('', async (_url, init) => {
    requests.push(JSON.parse(String(init?.body))); return response(204)
  }).resetPassword)
  const view = await mount('reset')
  try {
    await view.fill('newPassword', '        '); await view.fill('confirmPassword', '        '); await view.submit()
    assert.equal(requests.length, 0)
    assert.equal(view.input('newPassword').getAttribute('aria-invalid'), 'true')
    await view.fill('newPassword', '  new-password  '); await view.fill('confirmPassword', '  new-password  '); await view.submit()
    assert.deepEqual(requests, [{ userId, token, newPassword: '  new-password  ', confirmPassword: '  new-password  ' }])
  } finally { await view.close() }
})

for (const kind of ['forgot', 'reset'] as const) {
  test(`${kind} cannot show success or clear authentication after a misleading HTTP 200`, async t => {
    saveAuthSession(session)
    const method = kind === 'forgot' ? 'forgotPassword' : 'resetPassword'
    t.mock.method(recovery.passwordRecoveryApi, method, recovery.createPasswordRecoveryApi('', async () => response(200))[method])
    const view = await mount(kind)
    try {
      if (kind === 'forgot') await view.fill('email', 'person@example.com')
      else await validPassword(view)
      await view.submit()
      assert.equal(view.container.querySelector('[role="status"]'), null)
      assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('recovery.errors.server'))
      assert.equal(getAuthSession()?.accessToken, session.accessToken)
    } finally { await view.close() }
  })
}

for (const fragment of ['', `#userId=${userId}&token=broken`, `?userId=${userId}&token=${token}`]) {
  test(`missing or malformed reset fragment shows resend action without a form: ${fragment.slice(0, 24)}`, async () => {
    const view = await mount('reset', 'fr', fragment)
    try {
      assert.equal(view.container.querySelector('form'), null)
      assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('recovery.errors.invalidLink'))
      assert.ok(view.container.querySelector('a[href="/forgot-password"]'))
      assert.ok(!window.location.href.includes(token), 'even legacy query secrets must be scrubbed')
    } finally { await view.close() }
  })
}

test('used, expired or tampered server link failure becomes an invalid-link state with a resend action', async t => {
  assert.ok(recovery)
  t.mock.method(recovery.passwordRecoveryApi, 'resetPassword', recovery.createPasswordRecoveryApi('', async () => response(400, 'invalid_reset_token')).resetPassword)
  const view = await mount('reset')
  try {
    await validPassword(view); await view.submit()
    assert.equal(view.container.querySelector('form'), null)
    assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('recovery.errors.invalidLink'))
    assert.ok(view.container.querySelector('a[href="/forgot-password"]'))
  } finally { await view.close() }
})

test('following the same email link after success replaces the success state and lets the server reject its replay', async t => {
  let calls = 0
  t.mock.method(recovery.passwordRecoveryApi, 'resetPassword', recovery.createPasswordRecoveryApi('', async () =>
    ++calls === 1 ? response(204) : response(400, 'invalid_reset_token')).resetPassword)
  const view = await mount('reset', 'en', `#userId=${userId}&token=${token}`, true)
  try {
    await validPassword(view); await view.submit()
    assert.ok(view.container.querySelector('[role="status"]'))
    await followResetLink()
    assert.equal(window.location.hash, '')
    assert.equal(view.container.querySelector('[role="status"]'), null)
    assert.ok(view.container.querySelector('form'))
    await validPassword(view); await view.submit()
    assert.equal(calls, 2)
    assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('recovery.errors.invalidLink'))
  } finally { await view.close() }
})

test('a fresh email fragment opens a new form while the sanitized page shows an invalid link', async () => {
  const view = await mount('reset', 'fr', '', true)
  try {
    assert.ok(view.container.querySelector('[role="alert"]'))
    await followResetLink('cd'.repeat(32))
    assert.equal(window.location.hash, '')
    assert.equal(view.container.querySelector('[role="alert"]'), null)
    assert.ok(view.container.querySelector('form'))
  } finally { await view.close() }
})

test('a previous link request cannot report success over a newer fragment form', async t => {
  saveAuthSession(session)
  const requests: { token: string }[] = []
  let finish!: (value: Response) => void
  t.mock.method(recovery.passwordRecoveryApi, 'resetPassword', recovery.createPasswordRecoveryApi('', async (_url, init) => {
    requests.push(JSON.parse(String(init?.body)))
    return requests.length === 1 ? new Promise<Response>(resolve => { finish = resolve }) : response(204)
  }).resetPassword)
  const view = await mount('reset', 'en', `#userId=${userId}&token=${token}`, true)
  try {
    await validPassword(view); await view.submit()
    assert.equal(view.input('newPassword').disabled, true)
    await followResetLink('cd'.repeat(32))
    assert.equal(view.input('newPassword').disabled, false)
    assert.equal(view.input('newPassword').value, '')
    await act(async () => finish(response(204)))
    assert.equal(view.container.querySelector('[role="status"]'), null)
    assert.ok(view.container.querySelector('form'))
    assert.equal(getAuthSession()?.accessToken, session.accessToken, 'the superseded response must not alter the current form session')
    await validPassword(view); await view.submit()
    assert.deepEqual(requests.map(request => request.token), [token, 'cd'.repeat(32)])
    assert.ok(view.container.querySelector('[role="status"]'))
    assert.equal(getAuthSession(), null, 'the current successful reset must still clear its matching session')
  } finally { await view.close() }
})

test('server field messages stay translated and editing clears only the relevant stale validation errors', async t => {
  assert.ok(recovery)
  t.mock.method(recovery.passwordRecoveryApi, 'resetPassword', recovery.createPasswordRecoveryApi('', async () =>
    response(400, 'validation_error', { NewPassword: ['private password detail'], ConfirmPassword: ['private mismatch'] })).resetPassword)
  const view = await mount('reset', 'fr')
  try {
    await validPassword(view); await view.submit()
    assert.ok(!view.container.textContent!.includes('private'))
    assert.equal(view.input('newPassword').getAttribute('aria-invalid'), 'true')
    assert.equal(view.input('newPassword').closest('.field-group')!.querySelector('.field-error')!.textContent, i18n.t('recovery.errors.passwordInvalid'))
    await view.fill('confirmPassword', 'updated-password')
    assert.equal(view.input('confirmPassword').getAttribute('aria-invalid'), 'false')
    assert.equal(view.input('newPassword').getAttribute('aria-invalid'), 'true')
    await view.fill('newPassword', 'updated-password')
    assert.equal(view.container.querySelector('[role="alert"]'), null)
  } finally { await view.close() }
})

test('rate-limited recovery respects the retry countdown before enabling another request', async t => {
  assert.ok(recovery)
  t.mock.timers.enable({ apis: ['Date'] })
  t.mock.method(recovery.passwordRecoveryApi, 'forgotPassword', recovery.createPasswordRecoveryApi('', async () => response(429, 'too_many_requests', undefined, 2)).forgotPassword)
  const view = await mount('forgot')
  try {
    await view.fill('email', 'person@example.com'); await view.submit()
    assert.equal(view.container.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled, true)
    assert.match(view.container.querySelector('[role="timer"]')!.textContent!, /2/)
    t.mock.timers.tick(2000)
    await act(async () => window.dispatchEvent(new dom.window.Event('focus')))
    assert.equal(view.container.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled, false)
    assert.equal(view.container.querySelector('[role="status"]')!.textContent, i18n.t('recovery.retryReady'))
  } finally { await view.close() }
})
