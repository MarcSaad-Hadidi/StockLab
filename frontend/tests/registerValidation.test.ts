import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { authApi, createAuthApi } from '../src/api/authApi.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/register' })
Object.assign(globalThis, {
  React, window: dom.window, document: dom.window.document,
  FormData: dom.window.FormData, IS_REACT_ACT_ENVIRONMENT: true,
})
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { default: RegisterPage } = await import('../src/register/RegisterPage.tsx')

function jsonError(status: number, error: string, errors: Record<string, string[]> = {}) {
  return new Response(JSON.stringify({ error, errors }), {
    status, headers: { 'content-type': 'application/json' },
  })
}

async function mount(language = 'en') {
  await i18n.changeLanguage(language)
  const container = document.createElement('div')
  document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(RegisterPage)))
  const input = (name: string) => container.querySelector<HTMLInputElement>(`input[name="${name}"]`)!
  return {
    container, input,
    async fill(name: string, value: string) {
      await act(async () => {
        Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(input(name), value)
        input(name).dispatchEvent(new dom.window.Event('input', { bubbles: true }))
      })
    },
    async submit() {
      await act(async () => {
        container.querySelector('form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }))
      })
    },
    async close() { await act(async () => root.unmount()); container.remove() },
  }
}

async function fillValid(view: Awaited<ReturnType<typeof mount>>) {
  for (const [field, value] of Object.entries({
    name: 'Samira Martin', email: 'taken@example.com', password: 'valid-password', confirmPassword: 'valid-password',
  })) await view.fill(field, value)
}

test('editing email clears its previous local error while retaining the other required-field errors', async () => {
  const view = await mount()
  try {
    await view.submit()
    assert.equal(view.input('email').getAttribute('aria-invalid'), 'true')
    await view.fill('email', 'available@example.com')
    assert.equal(view.input('email').getAttribute('aria-invalid'), 'false')
    assert.ok(!view.input('email').closest('.field-group')!.querySelector('.field-error'))
    for (const name of ['name', 'password', 'confirmPassword']) {
      assert.equal(view.input(name).getAttribute('aria-invalid'), 'true', `${name} must keep its own error`)
      assert.ok(view.input(name).closest('.field-group')!.querySelector('.field-error'))
    }
  } finally { await view.close() }
})

for (const language of ['en', 'fr']) {
  test(`editing email clears the duplicate-email alert in ${language} and a new submit validates the current address`, async (t) => {
    const requests: unknown[] = []
    t.mock.method(authApi, 'register', createAuthApi('', async (_url, init) => {
      requests.push(JSON.parse(String(init?.body)))
      return jsonError(409, 'email_already_registered')
    }).register)
    const view = await mount(language)
    try {
      await fillValid(view)
      await view.submit()
      assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('register.errors.emailTaken'))
      await view.fill('name', 'Samira Moreno')
      assert.ok(view.container.querySelector('[role="alert"]'), 'editing another field must not dismiss an email rejection')
      await view.fill('email', 'invalid-address')
      assert.ok(!view.container.querySelector('[role="alert"]'), 'the old duplicate-email alert must clear')
      assert.equal(view.input('email').getAttribute('aria-invalid'), 'false')
      await view.submit()
      assert.equal(view.input('email').closest('.field-group')!.querySelector('.field-error')!.textContent,
        i18n.t('register.errors.emailInvalid'))
      assert.ok(!view.container.querySelector('[role="alert"]'))
      assert.equal(requests.length, 1, 'a locally invalid address must not be sent to the API')
    } finally { await view.close() }
  })
}

test('editing email retains other server field errors and the next response replaces them using the current values', async (t) => {
  const requests: unknown[] = []
  t.mock.method(authApi, 'register', createAuthApi('', async (_url, init) => {
    requests.push(JSON.parse(String(init?.body)))
    return requests.length === 1
      ? jsonError(400, 'validation_error', { Email: ['Invalid email.'], Password: ['Invalid password.'] })
      : jsonError(400, 'validation_error', { DisplayName: ['Invalid name.'] })
  }).register)
  const view = await mount()
  try {
    await fillValid(view)
    await view.submit()
    assert.equal(view.input('email').getAttribute('aria-invalid'), 'true')
    assert.equal(view.input('password').getAttribute('aria-invalid'), 'true')
    await view.fill('email', 'available@example.com')
    assert.equal(view.input('email').getAttribute('aria-invalid'), 'false')
    assert.equal(view.input('password').getAttribute('aria-invalid'), 'true')
    assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('register.errors.invalidRequest'))
    await view.submit()
    assert.deepEqual(requests[1], { displayName: 'Samira Martin', email: 'available@example.com', password: 'valid-password' })
    assert.equal(view.input('name').getAttribute('aria-invalid'), 'true')
    assert.equal(view.input('email').getAttribute('aria-invalid'), 'false')
    assert.equal(view.input('password').getAttribute('aria-invalid'), 'false')
  } finally { await view.close() }
})

test('editing email removes the validation summary when email was its only rejected field', async (t) => {
  t.mock.method(authApi, 'register', createAuthApi('', async () =>
    jsonError(400, 'validation_error', { Email: ['Invalid email.'] })).register)
  const view = await mount()
  try {
    await fillValid(view)
    await view.submit()
    assert.ok(view.container.querySelector('[role="alert"]'))
    await view.fill('email', 'available@example.com')
    assert.ok(!view.container.querySelector('[role="alert"]'), 'an email-only summary must clear')
    assert.equal(view.input('email').getAttribute('aria-invalid'), 'false')
  } finally { await view.close() }
})

test('editing email preserves an unrelated rate-limit error', async (t) => {
  t.mock.method(authApi, 'register', createAuthApi('', async () => jsonError(429, 'rate_limited')).register)
  const view = await mount()
  try {
    await fillValid(view)
    await view.submit()
    await view.fill('email', 'available@example.com')
    assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('register.errors.rateLimited'))
  } finally { await view.close() }
})

for (const scenario of ['duplicate email', 'email and password validation']) {
  test(`a delayed ${scenario} response cannot restore an error for an email edited during submission`, async (t) => {
    let finish!: (response: Response) => void
    t.mock.method(authApi, 'register', createAuthApi('', () => new Promise<Response>(resolve => { finish = resolve })).register)
    const view = await mount()
    try {
      await fillValid(view)
      await view.submit()
      assert.equal(view.container.querySelector('form')!.getAttribute('aria-busy'), 'true')
      await view.fill('email', 'available@example.com')
      await act(async () => finish(scenario === 'duplicate email'
        ? jsonError(409, 'email_already_registered')
        : jsonError(400, 'validation_error', { Email: ['Invalid email.'], Password: ['Invalid password.'] })))
      assert.equal(view.input('email').value, 'available@example.com')
      assert.equal(view.input('email').getAttribute('aria-invalid'), 'false')
      assert.ok(!view.input('email').closest('.field-group')!.querySelector('.field-error'))
      assert.equal(view.container.querySelector('form')!.getAttribute('aria-busy'), 'false')
      if (scenario === 'duplicate email') assert.ok(!view.container.querySelector('[role="alert"]'), 'a stale response must not restore the alert')
      else {
        assert.equal(view.input('password').getAttribute('aria-invalid'), 'true')
        assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('register.errors.invalidRequest'))
      }
    } finally { await view.close() }
  })
}
