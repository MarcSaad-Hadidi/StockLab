import assert from 'node:assert/strict'
import { test, type TestContext } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM, VirtualConsole } from 'jsdom'
import React, { act } from 'react'
import { googleAuthApi, createGoogleAuthApi } from '../src/api/googleAuthApi.ts'
import { authStorageKey } from '../src/auth/authStorage.ts'
import { authApi, AuthApiError } from '../src/api/authApi.ts'
import { profileApi, ProfileApiError } from '../src/api/profileApi.ts'
import type { GoogleCodeClientConfig } from '../src/auth/googleIdentitySdk.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'https://stocklab.example/login', virtualConsole: new VirtualConsole() })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document,
  FormData: dom.window.FormData, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { clearLoginLanguage, chooseLoginLanguage } = await import('../src/i18n/languagePreference.ts')
const { GoogleAuthButton } = await import('../src/components/GoogleAuthButton.tsx')
const { default: LoginPage } = await import('../src/login/LoginPage.tsx')
const { default: RegisterPage } = await import('../src/register/RegisterPage.tsx')
const session = { accessToken: 'stocklab-jwt', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account', displayName: 'Alex', email: 'alex@example.com', preferredLanguage: 'fr' } }
const json = (value: unknown, status = 200, headers = {}) => new Response(JSON.stringify(value), { status, headers })

function configured(t: TestContext) {
  t.mock.method(googleAuthApi, 'getConfig', async () => ({ enabled: true, clientId: 'test-client.apps.googleusercontent.com' } as const))
  const clients: GoogleCodeClientConfig[] = []
  let requests = 0
  // This fake is confined to tests. Runtime must load Google's official SDK.
  Object.assign(window, { google: { accounts: { oauth2: { initCodeClient(config: GoogleCodeClientConfig) {
    clients.push(config); return { requestCode() { requests++ } }
  } } } } })
  return { clients, requests: () => requests }
}
async function mount(language = 'en') {
  window.localStorage.clear(); clearLoginLanguage(); await i18n.changeLanguage(language)
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  const busy: boolean[] = []
  const onBusyChange = (value: boolean) => { busy.push(value) }
  await act(async () => root.render(React.createElement(GoogleAuthButton, { onBusyChange })))
  return { container, busy,
    button: () => container.querySelector<HTMLButtonElement>('.google-button')!,
    async click(selector = '.google-button') { await act(async () => container.querySelector<HTMLButtonElement>(selector)!.click()) },
    async password(value: string) {
      container.querySelector<HTMLInputElement>('[name="googleLinkPassword"]')!.value = value
      await act(async () => container.querySelector('form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })))
    },
    async close() { await act(async () => root.unmount()); container.remove() },
  }
}

test('disabled Google config is honest and does not load an SDK or create a session', async t => {
  delete (window as Window & { google?: unknown }).google
  t.mock.method(googleAuthApi, 'getConfig', async () => ({ enabled: false } as const))
  const view = await mount()
  try {
    assert.equal(view.button().disabled, true)
    assert.ok(view.container.textContent?.includes(i18n.t('googleAuth.errors.notConfigured')))
    assert.equal(document.querySelector('script'), null)
    assert.equal(window.localStorage.getItem(authStorageKey), null)
  } finally { await view.close() }
})

test('the popup opens synchronously from a prepared client and a code is exchanged only once', async t => {
  const sdk = configured(t)
  const calls: unknown[] = []
  t.mock.method(googleAuthApi, 'exchange', createGoogleAuthApi('', async (_url, init) => {
    calls.push(JSON.parse(String(init?.body))); return json(session)
  }).exchange)
  const view = await mount()
  try {
    assert.equal(sdk.clients.length, 1, 'client must be ready before the user gesture')
    assert.deepEqual({ ...sdk.clients[0], callback: undefined, error_callback: undefined }, {
      client_id: 'test-client.apps.googleusercontent.com', scope: 'openid email profile', ux_mode: 'popup',
      include_granted_scopes: false, select_account: true, callback: undefined, error_callback: undefined,
    })
    await view.click()
    assert.equal(sdk.requests(), 1)
    await act(async () => { sdk.clients[0].callback({ code: 'single-use' }); sdk.clients[0].callback({ code: 'single-use' }) })
    assert.deepEqual(calls, [{ code: 'single-use', language: 'en' }])
    assert.equal(JSON.parse(window.localStorage.getItem(authStorageKey)!).accessToken, 'stocklab-jwt')
    assert.equal(i18n.language, 'fr')
  } finally { await view.close() }
})

for (const language of ['en', 'fr']) for (const type of ['popup_closed', 'popup_failed_to_open', 'unknown'] as const) {
  test(`Google ${type} is actionable and retryable in ${language}`, async t => {
    const sdk = configured(t)
    const view = await mount(language)
    try {
      await view.click(); await act(async () => sdk.clients[0].error_callback({ type }))
      assert.equal(view.button().disabled, false)
      const key = type === 'popup_closed' ? 'cancelled' : type === 'popup_failed_to_open' ? 'popupBlocked' : 'unavailable'
      assert.ok(view.container.querySelector('[role="alert"]')!.textContent?.includes(i18n.t(`googleAuth.errors.${key}`)))
      assert.equal(view.busy.at(-1), false)
      assert.equal(window.localStorage.getItem(authStorageKey), null)
      await view.click(); assert.equal(sdk.requests(), 2)
      await act(async () => sdk.clients[0].callback({ code: 'stale' }))
      assert.equal(window.localStorage.getItem(authStorageKey), null)
    } finally { await view.close() }
  })
}

test('Google consent refusal and malformed success never create a session', async t => {
  const sdk = configured(t)
  t.mock.method(googleAuthApi, 'exchange', createGoogleAuthApi('', async () => json({ accessToken: 'wrong' })).exchange)
  const view = await mount()
  try {
    await view.click(); await act(async () => sdk.clients[0].callback({ error: 'access_denied' }))
    assert.ok(view.container.textContent?.includes(i18n.t('googleAuth.errors.refused')))
    await view.click(); await act(async () => sdk.clients.at(-1)!.callback({ code: 'code' }))
    assert.ok(view.container.textContent?.includes(i18n.t('googleAuth.errors.invalidResponse')))
    assert.equal(window.localStorage.getItem(authStorageKey), null)
  } finally { await view.close() }
})

test('email collision keeps a memory-only ticket; wrong password retries and expiry restarts Google', async t => {
  const sdk = configured(t)
  const ticket = 'memory-only-proof-ticket'
  const calls: unknown[] = []
  t.mock.method(googleAuthApi, 'exchange', createGoogleAuthApi('', async () => json({
    error: 'google_link_required', linkToken: ticket, expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
  }, 409)).exchange)
  t.mock.method(googleAuthApi, 'link', createGoogleAuthApi('', async (_url, init) => {
    calls.push(JSON.parse(String(init?.body))); return json({ error: calls.length === 1 ? 'invalid_credentials' : 'google_link_expired' }, calls.length === 1 ? 401 : 400)
  }).link)
  const view = await mount()
  try {
    await view.click(); await act(async () => sdk.clients[0].callback({ code: 'collision' }))
    assert.ok(view.container.textContent?.includes(i18n.t('googleAuth.link.description')))
    assert.equal(view.busy.at(-1), true)
    assert.equal(view.container.innerHTML.includes(ticket), false)
    assert.equal(JSON.stringify({ ...window.localStorage, ...window.sessionStorage }).includes(ticket), false)
    await view.password('wrong-password')
    assert.ok(view.container.textContent?.includes(i18n.t('googleAuth.errors.invalidPassword')))
    assert.ok(view.container.querySelector('form'))
    await view.password('retry-password')
    assert.deepEqual(calls, [{ linkToken: ticket, password: 'wrong-password', language: 'en' }, { linkToken: ticket, password: 'retry-password', language: 'en' }])
    assert.equal(view.container.querySelector('form'), null)
    assert.equal(view.button().disabled, false)
    assert.ok(view.container.textContent?.includes(i18n.t('googleAuth.errors.expired')))
    assert.equal(window.localStorage.getItem(authStorageKey), null)
  } finally { await view.close() }
})

test('a successful password-proof link uses the same StockLab session completion', async t => {
  const sdk = configured(t)
  t.mock.method(googleAuthApi, 'exchange', createGoogleAuthApi('', async () => json({
    error: 'google_link_required', linkToken: 'proof', expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
  }, 409)).exchange)
  t.mock.method(googleAuthApi, 'link', createGoogleAuthApi('', async () => json(session)).link)
  const view = await mount()
  try {
    await view.click(); await act(async () => sdk.clients[0].callback({ code: 'collision' }))
    await view.password('existing-stocklab-password')
    assert.equal(JSON.parse(window.localStorage.getItem(authStorageKey)!).user.id, 'account')
    assert.equal(i18n.language, 'fr')
  } finally { await view.close() }
})

test('cancellation and unmount ignore an in-flight exchange and late SDK callbacks', async t => {
  const sdk = configured(t)
  let finish!: (response: Response) => void
  let calls = 0
  t.mock.method(googleAuthApi, 'exchange', createGoogleAuthApi('', async () => { calls++; return new Promise(resolve => { finish = resolve }) }).exchange)
  const view = await mount()
  await view.click(); await act(async () => sdk.clients[0].callback({ code: 'code' }))
  await view.click('.google-auth-cancel')
  await act(async () => { finish(json(session)); sdk.clients[0].callback({ code: 'late' }) })
  assert.equal(window.localStorage.getItem(authStorageKey), null)
  await view.click(); await view.close()
  await act(async () => sdk.clients.at(-1)!.callback({ code: 'after-unmount' }))
  assert.equal(calls, 1)
  assert.equal(window.localStorage.getItem(authStorageKey), null)
})

test('a blocked browser session does not claim success', async t => {
  const sdk = configured(t)
  t.mock.method(googleAuthApi, 'exchange', createGoogleAuthApi('', async () => json(session)).exchange)
  const view = await mount()
  t.mock.method(dom.window.Storage.prototype, 'setItem', () => { throw new Error('blocked') })
  try {
    await view.click(); await act(async () => sdk.clients[0].callback({ code: 'code' }))
    assert.ok(view.container.textContent?.includes(i18n.t('login.errors.sessionStorage')))
    assert.equal(window.localStorage.getItem(authStorageKey), null)
  } finally { await view.close() }
})

test('backend configuration and SDK download failures both offer a working manual retry', async t => {
  const sdk = configured(t)
  let configurations = 0
  t.mock.method(googleAuthApi, 'getConfig', createGoogleAuthApi('', async () => {
    configurations++
    if (configurations === 1) throw new Error('backend unavailable')
    return json({ enabled: true, clientId: 'test-client.apps.googleusercontent.com' })
  }).getConfig)
  delete (window as Window & { google?: unknown }).google
  const view = await mount()
  try {
    assert.ok(view.container.textContent?.includes(i18n.t('googleAuth.errors.offline')))
    await view.click('.google-auth-retry')
    const script = document.querySelector('script')!
    assert.equal(script.src, 'https://accounts.google.com/gsi/client')
    await act(async () => script.dispatchEvent(new dom.window.Event('error')))
    assert.ok(view.container.textContent?.includes(i18n.t('googleAuth.errors.sdk')))
    configured(t)
    await view.click('.google-auth-retry')
    assert.equal(view.button().disabled, false)
    assert.equal(sdk.requests(), 0)
    assert.equal(window.localStorage.getItem(authStorageKey), null)
  } finally { await view.close() }
})

test('a Google 429 blocks manual and automatic repeat attempts until Retry-After expires', async t => {
  const sdk = configured(t)
  let now = Date.now()
  t.mock.method(Date, 'now', () => now)
  let calls = 0
  t.mock.method(googleAuthApi, 'exchange', createGoogleAuthApi('', async () => {
    calls++; return json({}, 429, { 'Retry-After': '3' })
  }).exchange)
  const view = await mount()
  try {
    await view.click(); await act(async () => sdk.clients[0].callback({ code: 'limited' }))
    assert.equal(view.button().disabled, true)
    assert.equal(view.container.querySelector('[role="timer"]')!.textContent, i18n.t('login.retryCountdown', { count: 3 }))
    await view.click(); assert.equal(sdk.requests(), 1)
    now += 3001
    await act(async () => window.dispatchEvent(new dom.window.Event('focus')))
    assert.equal(view.button().disabled, false)
    assert.equal(calls, 1, 'expiry must never resubmit a consumed authorization code')
    await view.click(); assert.equal(sdk.requests(), 2)
  } finally { await view.close() }
})

test('an expired in-memory link ticket is discarded on focus and cannot submit a password', async t => {
  const sdk = configured(t)
  let now = Date.now()
  t.mock.method(Date, 'now', () => now)
  t.mock.method(googleAuthApi, 'exchange', createGoogleAuthApi('', async () => json({
    error: 'google_link_required', linkToken: 'expiring', expiresAtUtc: new Date(now + 5000).toISOString(),
  }, 409)).exchange)
  const view = await mount()
  try {
    await view.click(); await act(async () => sdk.clients[0].callback({ code: 'collision' }))
    assert.ok(view.container.querySelector('form'))
    now += 5001
    await act(async () => window.dispatchEvent(new dom.window.Event('focus')))
    assert.equal(view.container.querySelector('form'), null)
    assert.ok(view.container.textContent?.includes(i18n.t('googleAuth.errors.expired')))
    assert.equal(view.busy.at(-1), false)
  } finally { await view.close() }
})

test('an explicit language choice is saved before Google creates the StockLab session', async t => {
  const sdk = configured(t)
  let savedLanguage = ''
  t.mock.method(googleAuthApi, 'exchange', createGoogleAuthApi('', async () => json(session)).exchange)
  t.mock.method(profileApi, 'updateLanguage', async language => {
    savedLanguage = language
    assert.equal(window.localStorage.getItem(authStorageKey), null)
    return { ...session.user, preferredLanguage: language, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z' }
  })
  const view = await mount()
  chooseLoginLanguage('en')
  try {
    await view.click(); await act(async () => sdk.clients[0].callback({ code: 'code' }))
    assert.equal(savedLanguage, 'en')
    assert.equal(JSON.parse(window.localStorage.getItem(authStorageKey)!).user.preferredLanguage, 'en')
    assert.equal(i18n.language, 'en')
  } finally { await view.close() }
})

test('a failed language save and a cancelled pending language save cannot create a session', async t => {
  const sdk = configured(t)
  t.mock.method(googleAuthApi, 'exchange', createGoogleAuthApi('', async () => json(session)).exchange)
  t.mock.method(profileApi, 'updateLanguage', async () => { throw new ProfileApiError(503, 'server_error') })
  const view = await mount()
  chooseLoginLanguage('en')
  try {
    await view.click(); await act(async () => sdk.clients[0].callback({ code: 'first' }))
    assert.ok(view.container.textContent?.includes(i18n.t('login.errors.languageSave')))
    assert.equal(window.localStorage.getItem(authStorageKey), null)
    let finish!: (result: Awaited<ReturnType<typeof profileApi.updateLanguage>>) => void
    t.mock.method(profileApi, 'updateLanguage', async () => new Promise(resolve => { finish = resolve }))
    await view.click(); await act(async () => sdk.clients.at(-1)!.callback({ code: 'second' }))
    await view.click('.google-auth-cancel')
    await act(async () => finish({ ...session.user, preferredLanguage: 'en', createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z' }))
    assert.equal(window.localStorage.getItem(authStorageKey), null)
  } finally { await view.close() }
})

for (const [name, Page] of [['Login', LoginPage], ['Register', RegisterPage]] as const) {
  test(`${name} prevents concurrent email and Google submissions and disables language selection during Google`, async t => {
    const sdk = configured(t)
    window.localStorage.clear(); clearLoginLanguage(); await i18n.changeLanguage('en')
    let emailCalls = 0
    let failEmail!: () => void
    const pendingEmail = async () => {
      emailCalls++
      await new Promise<void>(resolve => { failEmail = resolve })
      throw new AuthApiError(401, 'invalid_credentials', 'Invalid credentials')
    }
    t.mock.method(authApi, 'login', pendingEmail)
    t.mock.method(authApi, 'register', pendingEmail)
    const container = document.createElement('div'); document.body.append(container)
    const root = createRoot(container)
    await act(async () => root.render(React.createElement(Page)))
    const submit = async () => act(async () => container.querySelector('form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })))
    const google = () => container.querySelector<HTMLButtonElement>('.google-button')!
    for (const input of container.querySelectorAll<HTMLInputElement>('input')) input.value = input.name === 'email' ? 'test@example.com' : 'valid-password'
    try {
      await act(async () => google().click())
      assert.equal(container.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled, true)
      assert.equal(container.querySelector<HTMLButtonElement>('button[title="Français"]')!.disabled, true)
      await submit(); assert.equal(emailCalls, 0)
      await act(async () => sdk.clients[0].error_callback({ type: 'popup_closed' }))
      await submit(); assert.equal(emailCalls, 1)
      assert.equal(google().disabled, true)
      await act(async () => google().click()); assert.equal(sdk.requests(), 1)
      await act(async () => failEmail())
      assert.equal(google().disabled, false)
      await act(async () => google().click()); assert.equal(sdk.requests(), 2)
      assert.equal(container.querySelector('form [role="alert"]'), null, 'starting Google clears previous email errors')
    } finally { await act(async () => root.unmount()); container.remove() }
  })
}
