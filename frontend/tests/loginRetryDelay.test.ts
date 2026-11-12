import assert from 'node:assert/strict'
import { test, type TestContext } from 'node:test'
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
const dom = new JSDOM('', { url: 'http://localhost/login' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document,
  FormData: dom.window.FormData, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { default: LoginPage } = await import('../src/login/LoginPage.tsx')

function clock(t: TestContext) {
  let now = Date.parse('2026-11-11T12:00:00Z')
  let next = 0
  const timers = new Map<number, () => void>()
  t.mock.method(Date, 'now', () => now)
  t.mock.method(window, 'setInterval', callback => {
    const id = ++next
    timers.set(id, callback as () => void)
    return id
  })
  t.mock.method(window, 'clearInterval', id => { timers.delete(id!) })
  return { timers, async advance(milliseconds: number, fire = true) {
    now += milliseconds
    if (fire) await act(async () => { for (const callback of [...timers.values()]) callback() })
  } }
}

async function mount(language = 'en') {
  await i18n.changeLanguage(language)
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(LoginPage)))
  container.querySelector<HTMLInputElement>('[name="email"]')!.value = 'account@example.test'
  container.querySelector<HTMLInputElement>('[name="password"]')!.value = 'test-password'
  return { container,
    button: () => container.querySelector<HTMLButtonElement>('button[type="submit"]')!,
    async submit() { await act(async () => { container.querySelector('form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })) }) },
    async close() { await act(async () => root.unmount()); container.remove() },
  }
}

for (const language of ['en', 'fr']) test(`login counts down, prevents blocked submits and enables a manual retry in ${language}`, async t => {
  const time = clock(t)
  let calls = 0
  t.mock.method(authApi, 'login', createAuthApi('', async () => {
    calls++
    return new Response('{}', { status: calls === 1 ? 429 : 401, headers: { 'Retry-After': '3' } })
  }).login)
  const view = await mount(language)
  try {
    await view.submit()
    assert.equal(view.button().disabled, true)
    assert.equal(view.container.querySelector('[role="timer"]')!.textContent, i18n.t('login.retryCountdown', { count: 3 }))
    assert.equal(view.container.querySelector('[role="timer"]')!.getAttribute('aria-live'), 'off')
    await view.submit()
    assert.equal(calls, 1)
    assert.equal(view.container.querySelector<HTMLInputElement>('[name="password"]')!.value, 'test-password')
    await time.advance(1001)
    assert.equal(view.container.querySelector('[role="timer"]')!.textContent, i18n.t('login.retryCountdown', { count: 2 }))
    await time.advance(1000)
    assert.equal(view.container.querySelector('[role="timer"]')!.textContent, i18n.t('login.retryCountdown', { count: 1 }))
    await time.advance(999)
    assert.equal(view.button().disabled, false)
    assert.equal(time.timers.size, 0)
    assert.equal(view.container.querySelector('[role="status"]')!.textContent, i18n.t('login.retryReady'))
    assert.equal(calls, 1, 'expiry must not submit credentials automatically')
    await view.submit()
    assert.equal(calls, 2)
    assert.equal(view.container.querySelector('[role="alert"]')!.textContent, i18n.t('login.errors.invalidCredentials'))
  } finally { await view.close() }
})

test('a fresh 429 replaces the expired delay with the new server delay', async t => {
  const time = clock(t)
  let calls = 0
  t.mock.method(authApi, 'login', createAuthApi('', async () => new Response('{}',
    { status: 429, headers: { 'Retry-After': String(++calls === 1 ? 2 : 7) } })).login)
  const view = await mount()
  try {
    await view.submit(); await time.advance(2000); await view.submit()
    assert.equal(view.button().disabled, true)
    assert.equal(view.container.querySelector('[role="timer"]')!.textContent, i18n.t('login.retryCountdown', { count: 7 }))
    assert.equal(calls, 2)
  } finally { await view.close() }
  assert.equal(time.timers.size, 0)
})

for (const event of ['focus', 'visibilitychange']) test(`${event} refreshes a countdown after background timer suspension`, async t => {
  const time = clock(t)
  t.mock.method(authApi, 'login', createAuthApi('', async () => new Response('{}',
    { status: 429, headers: { 'Retry-After': '5' } })).login)
  const view = await mount()
  try {
    await view.submit(); await time.advance(10000, false)
    await act(async () => {
      const target = event === 'focus' ? window : document
      target.dispatchEvent(new dom.window.Event(event))
    })
    assert.equal(view.button().disabled, false)
    assert.equal(time.timers.size, 0)
  } finally { await view.close() }
})

test('missing server retry metadata uses a bounded one-minute fallback', async t => {
  const time = clock(t)
  t.mock.method(authApi, 'login', createAuthApi('', async () => new Response('{}', { status: 429 })).login)
  const view = await mount()
  try {
    await view.submit()
    assert.equal(view.container.querySelector('[role="timer"]')!.textContent, i18n.t('login.retryCountdown', { count: 60 }))
    await time.advance(60000)
    assert.equal(view.button().disabled, false)
  } finally { await view.close() }
})

test('a submit already in flight cannot be sent twice', async t => {
  let finish!: (response: Response) => void
  let calls = 0
  t.mock.method(authApi, 'login', createAuthApi('', async () => {
    calls++
    return new Promise<Response>(resolve => { finish = resolve })
  }).login)
  const view = await mount()
  try {
    await view.submit(); await view.submit()
    assert.equal(calls, 1)
    await act(async () => { finish(new Response('{}', { status: 401 })) })
    assert.equal(view.button().disabled, false)
  } finally { await view.close() }
})
