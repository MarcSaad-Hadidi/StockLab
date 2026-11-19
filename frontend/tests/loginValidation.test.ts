import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { authApi, createAuthApi } from '../src/api/authApi.ts'
import { googleAuthApi } from '../src/api/googleAuthApi.ts'

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
beforeEach(t => { t.mock.method(googleAuthApi, 'getConfig', async () => ({ enabled: false } as const)) })
const rejected = (status = 400, errors: Record<string, string[]> = {}) => new Response(JSON.stringify({ errors }),
  { status, headers: { 'Retry-After': '60' } })
async function mount(language = 'en') {
  await i18n.changeLanguage(language)
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(LoginPage)))
  const input = (name: string) => container.querySelector<HTMLInputElement>(`input[name="${name}"]`)!
  const change = (name: string, value: string) => {
    Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(input(name), value)
    input(name).dispatchEvent(new dom.window.Event('input', { bubbles: true }))
  }
  return { container, input, change,
    async fill(name: string, value: string) { await act(async () => change(name, value)) },
    async submit() { await act(async () => {
      container.querySelector('form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }))
    }) },
    async close() { await act(async () => root.unmount()); container.remove() },
  }
}
async function fillValid(view: Awaited<ReturnType<typeof mount>>) {
  await view.fill('email', 'account@example.test'); await view.fill('password', 'test-password')
}

for (const language of ['en', 'fr']) test(`Login clears only the edited field and resubmits current values in ${language}`, async t => {
  const requests: unknown[] = []
  t.mock.method(authApi, 'login', createAuthApi('', async (_url, init) => {
    requests.push(JSON.parse(String(init?.body))); return rejected(400, { Password: ['Rejected.'] })
  }).login)
  const view = await mount(language)
  try {
    await view.submit()
    assert.equal(view.input('email').getAttribute('aria-invalid'), 'true')
    assert.equal(view.input('password').getAttribute('aria-invalid'), 'true')
    await view.fill('email', 'invalid-address')
    assert.equal(view.input('email').getAttribute('aria-invalid'), 'false')
    assert.ok(!view.input('email').closest('.field-group')!.querySelector('.field-error'))
    assert.equal(view.input('password').getAttribute('aria-invalid'), 'true')
    await view.submit()
    assert.equal(view.input('email').closest('.field-group')!.querySelector('.field-error')!.textContent,
      i18n.t('login.errors.emailInvalid'))
    assert.equal(requests.length, 0)
    await fillValid(view)
    assert.equal(view.container.querySelectorAll('.field-error').length, 0)
    await view.submit()
    assert.deepEqual(requests, [{ email: 'account@example.test', password: 'test-password' }])
    assert.equal(view.input('email').getAttribute('aria-invalid'), 'false')
    assert.equal(view.input('password').getAttribute('aria-invalid'), 'true')
  } finally { await view.close() }
})

test('Login retains other server field errors and removes their summary after the final correction', async t => {
  t.mock.method(authApi, 'login', createAuthApi('', async () => rejected(400,
    { Email: ['Rejected.'], Password: ['Rejected.'] })).login)
  const view = await mount()
  try {
    await fillValid(view); await view.submit()
    await view.fill('email', 'updated@example.test')
    assert.equal(view.input('email').getAttribute('aria-invalid'), 'false')
    assert.equal(view.input('password').getAttribute('aria-invalid'), 'true')
    assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('login.errors.invalidRequest'))
    await view.fill('password', 'updated-password')
    assert.equal(view.input('password').getAttribute('aria-invalid'), 'false')
    assert.ok(!view.container.querySelector('[role="alert"]'))
  } finally { await view.close() }
})

test('correcting both Login fields within one batch cannot restore either previous error', async t => {
  t.mock.method(authApi, 'login', createAuthApi('', async () => rejected(400,
    { Email: ['Rejected.'], Password: ['Rejected.'] })).login)
  const view = await mount()
  try {
    await fillValid(view); await view.submit()
    await act(async () => { view.change('email', 'updated@example.test'); view.change('password', 'updated-password') })
    assert.equal(view.container.querySelectorAll('.field-error').length, 0)
    assert.ok(!view.container.querySelector('[role="alert"]'))
  } finally { await view.close() }
})

for (const field of ['email', 'password']) {
  test(`editing Login ${field} removes the old credential rejection`, async t => {
    t.mock.method(authApi, 'login', createAuthApi('', async () => rejected(401)).login)
    const view = await mount()
    try {
      await fillValid(view); await view.submit()
      assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('login.errors.invalidCredentials'))
      await view.fill(field, field === 'email' ? 'updated@example.test' : 'updated-password')
      assert.ok(!view.container.querySelector('[role="alert"]'))
    } finally { await view.close() }
  })
  test(`a delayed Login validation response ignores edited ${field} but retains the other field error`, async t => {
    let finish!: (response: Response) => void
    t.mock.method(authApi, 'login', createAuthApi('', () => new Promise<Response>(resolve => { finish = resolve })).login)
    const view = await mount()
    try {
      await fillValid(view); await view.submit()
      await view.fill(field, field === 'email' ? 'updated@example.test' : 'updated-password')
      await act(async () => finish(rejected(400, { Email: ['Rejected.'], Password: ['Rejected.'] })))
      assert.equal(view.input(field).getAttribute('aria-invalid'), 'false')
      assert.equal(view.input(field === 'email' ? 'password' : 'email').getAttribute('aria-invalid'), 'true')
      assert.ok(view.container.querySelector('[role="alert"]'))
    } finally { await view.close() }
  })
  test(`a delayed credential rejection cannot be restored after Login ${field} changed`, async t => {
    let finish!: (response: Response) => void
    t.mock.method(authApi, 'login', createAuthApi('', () => new Promise<Response>(resolve => { finish = resolve })).login)
    const view = await mount()
    try {
      await fillValid(view); await view.submit()
      await view.fill(field, field === 'email' ? 'updated@example.test' : 'updated-password')
      await act(async () => finish(rejected(401)))
      assert.ok(!view.container.querySelector('[role="alert"]'))
      assert.equal(view.container.querySelector('form')!.getAttribute('aria-busy'), 'false')
    } finally { await view.close() }
  })
}

for (const status of [429, 503]) test(`editing credentials retains an unrelated HTTP ${status} failure`, async t => {
  let calls = 0
  t.mock.method(authApi, 'login', createAuthApi('', async () => { calls++; return rejected(status) }).login)
  const view = await mount()
  try {
    await fillValid(view); await view.submit()
    await view.fill('email', 'updated@example.test'); await view.fill('password', 'updated-password')
    assert.ok(view.container.querySelector('[role="alert"]'))
    if (status === 429) {
      assert.ok(view.container.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled)
      assert.ok(view.container.querySelector('[role="timer"]'))
      await view.submit(); assert.equal(calls, 1, 'typing must not bypass the server cooldown')
    }
  } finally { await view.close() }
})
