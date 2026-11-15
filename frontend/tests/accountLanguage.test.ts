import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM, VirtualConsole } from 'jsdom'
import React, { act } from 'react'
import { authApi, createAuthApi } from '../src/api/authApi.ts'
import { getAuthSession, clearAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'
import { profileApi, createProfileApi, ProfileApiError, type UserProfile } from '../src/api/profileApi.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const redirects: string[] = []
const console = new VirtualConsole()
console.on('jsdomError', error => {
  if (/navigation/i.test(error.message)) redirects.push(i18n.language)
  else throw error
})
const dom = new JSDOM('', { url: 'http://localhost/login', virtualConsole: console })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document,
  FormData: dom.window.FormData, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n, setLanguage, languageStorageKey } = await import('../src/i18n/i18n.ts')
const { default: LoginPage } = await import('../src/login/LoginPage.tsx')
const { LanguageSelector } = await import('../src/components/LanguageSelector.tsx')
const { clearLoginLanguage } = await import('../src/i18n/languagePreference.ts')

const languageAccount = { id: '1b7f6c30-8c4f-4fe8-9350-2f6be1f9c2c0', displayName: 'Language account', email: 'language@example.com', preferredLanguage: 'fr' as const }
const languageSession = { accessToken: 'new-login-token', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z', user: languageAccount }
const languageProfile = { ...languageAccount, createdAtUtc: '2026-09-01T00:00:00Z', updatedAtUtc: '2026-11-14T00:00:00Z' }
async function mountLanguage(component: React.ReactElement) {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(component))
  return { container, async close(preserveChoice = false) { await act(async () => root.unmount()); container.remove(); clearAuthSession(); if (!preserveChoice) { clearLoginLanguage(); window.sessionStorage.clear() } } }
}
async function submitLogin(container: HTMLElement) {
  container.querySelector<HTMLInputElement>('[name="email"]')!.value = 'language@example.com'
  container.querySelector<HTMLInputElement>('[name="password"]')!.value = 'test-only-password'
  await act(async () => container.querySelector('form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })))
}

for (const language of ['en', 'fr'] as const) test(`an explicit ${language} choice at login takes priority and is saved before navigation`, async t => {
  window.localStorage.clear(); window.sessionStorage.clear()
  const previous = language === 'en' ? 'fr' : 'en'
  await setLanguage(previous)
  t.mock.method(authApi, 'login', async () => ({ ...languageSession, user: { ...languageAccount, preferredLanguage: previous } }))
  let requested = ''
  t.mock.method(profileApi, 'updateLanguage', async next => { requested = next; return { ...languageProfile, preferredLanguage: next } })
  const view = await mountLanguage(React.createElement(LoginPage))
  try {
    await act(async () => view.container.querySelector<HTMLButtonElement>(`button[title="${language === 'en' ? 'English' : 'Français'}"]`)!.click())
    await submitLogin(view.container)
    assert.equal(requested, language)
    assert.equal(i18n.language, language)
    assert.equal(getAuthSession()?.user.preferredLanguage, language)
    assert.equal(redirects.at(-1), language)
  } finally { await view.close() }
})

test('a failed login-language save does not navigate or claim that the new preference was saved', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); await setLanguage('fr')
  t.mock.method(authApi, 'login', async () => languageSession)
  t.mock.method(profileApi, 'updateLanguage', async () => { throw new ProfileApiError(503, 'server_error') })
  const view = await mountLanguage(React.createElement(LoginPage))
  try {
    await act(async () => view.container.querySelector<HTMLButtonElement>('button[title="English"]')!.click())
    const count = redirects.length
    await submitLogin(view.container)
    assert.equal(redirects.length, count)
    assert.equal(getAuthSession(), null)
    assert.equal(i18n.language, 'en')
    assert.ok(view.container.querySelector('[role="alert"]'))
  } finally { await view.close() }
})

test('the authenticated global selector applies the choice immediately and commits only a confirmed account preference', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); saveAuthSession(languageSession); await setLanguage('fr')
  let finish!: (profile: UserProfile) => void
  t.mock.method(profileApi, 'updateLanguage', () => new Promise(resolve => { finish = resolve }))
  const view = await mountLanguage(React.createElement(LanguageSelector))
  try {
    const button = view.container.querySelector<HTMLButtonElement>('button[title="English"]')!
    await act(async () => button.click())
    assert.equal(i18n.language, 'en')
    assert.equal(getAuthSession()?.user.preferredLanguage, 'fr')
    assert.equal(button.disabled, true)
    await act(async () => finish({ ...languageProfile, preferredLanguage: 'en' }))
    assert.equal(getAuthSession()?.user.preferredLanguage, 'en')
    assert.equal(button.disabled, false)
    assert.equal(view.container.querySelector('[role="alert"]'), null)
  } finally { await view.close() }
})

test('a failed global save keeps the manual interface choice, exposes the failure and can retry', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); saveAuthSession(languageSession); await setLanguage('fr')
  let fail = true
  t.mock.method(profileApi, 'updateLanguage', async language => {
    if (fail) throw new ProfileApiError(0, 'offline')
    return { ...languageProfile, preferredLanguage: language }
  })
  const view = await mountLanguage(React.createElement(LanguageSelector))
  try {
    await act(async () => view.container.querySelector<HTMLButtonElement>('button[title="English"]')!.click())
    assert.equal(i18n.language, 'en')
    assert.equal(getAuthSession()?.user.preferredLanguage, 'fr')
    assert.ok(view.container.querySelector('[role="alert"]'))
    fail = false
    await act(async () => view.container.querySelector<HTMLButtonElement>('button[title="English"]')!.click())
    assert.equal(getAuthSession()?.user.preferredLanguage, 'en')
    assert.equal(view.container.querySelector('[role="alert"]'), null)
  } finally { await view.close() }
})

test('a late global save cannot change a replacement account or reapply an older locale', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); saveAuthSession(languageSession); await setLanguage('fr')
  let finish!: (profile: object) => void
  t.mock.method(profileApi, 'updateLanguage', () => new Promise(resolve => { finish = resolve }))
  const view = await mountLanguage(React.createElement(LanguageSelector))
  try {
    await act(async () => view.container.querySelector<HTMLButtonElement>('button[title="English"]')!.click())
    await act(async () => { saveAuthSession({ ...languageSession, accessToken: 'replacement-token', user: { ...languageAccount, id: 'replacement' } }); void setLanguage('fr') })
    assert.ok(finish, 'a manual account choice starts a persistence request')
    await act(async () => finish({ ...languageProfile, preferredLanguage: 'en' }))
    assert.equal(getAuthSession()?.user.id, 'replacement')
    assert.equal(getAuthSession()?.user.preferredLanguage, 'fr')
    assert.equal(i18n.language, 'fr')
  } finally { await view.close() }
})

test('a guest choice survives navigation from Register and does not update a previously signed-in account', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); saveAuthSession(languageSession); await setLanguage('fr')
  let writes = 0
  t.mock.method(profileApi, 'updateLanguage', async language => { writes++; return { ...languageProfile, preferredLanguage: language } })
  const guest = await mountLanguage(React.createElement(LanguageSelector, { persistToAccount: false }))
  // Simulate the full-page transition without clearing this tab's sessionStorage.
  const loginChoice = (await import('../src/i18n/languagePreference.ts'))
  try {
    await act(async () => guest.container.querySelector<HTMLButtonElement>('button[title="English"]')!.click())
    assert.equal(writes, 0)
    assert.equal(getAuthSession()?.user.preferredLanguage, 'fr')
    assert.equal(loginChoice.getLoginLanguage(), 'en')
  } finally { await guest.close(true) }
  // The next Login page consumes the guest choice after authentication.
  t.mock.method(authApi, 'login', async () => languageSession)
  const next = await mountLanguage(React.createElement(LoginPage))
  try {
    await submitLogin(next.container)
    assert.equal(writes, 1)
    assert.equal(getAuthSession()?.user.preferredLanguage, 'en')
    assert.equal(loginChoice.getLoginLanguage(), null)
  } finally { await next.close() }
})

test('two global selectors share the pending write and retain a rotated session token', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); saveAuthSession(languageSession); await setLanguage('fr')
  let finish!: (value: UserProfile) => void
  let writes = 0
  t.mock.method(profileApi, 'updateLanguage', () => { writes++; return new Promise(resolve => { finish = resolve }) })
  const view = await mountLanguage(React.createElement('div', null, React.createElement(LanguageSelector), React.createElement(LanguageSelector)))
  try {
    const buttons = [...view.container.querySelectorAll<HTMLButtonElement>('button[title="English"]')]
    await act(async () => { buttons[0].click(); buttons[1].click() })
    assert.equal(writes, 1)
    assert.ok(buttons.every(button => button.disabled))
    await act(async () => saveAuthSession({ ...languageSession, accessToken: 'rotated-token' }))
    await act(async () => finish({ ...languageProfile, preferredLanguage: 'en' }))
    assert.equal(getAuthSession()?.accessToken, 'rotated-token')
    assert.equal(getAuthSession()?.user.preferredLanguage, 'en')
    assert.ok(buttons.every(button => !button.disabled && button.getAttribute('aria-pressed') === 'true'))
  } finally { await view.close() }
})

test('a guest choice wins when sessionStorage accepts reads but refuses new writes', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); clearLoginLanguage(); await setLanguage('fr')
  window.sessionStorage.setItem('stocklab-login-language', 'fr')
  const setItem = window.Storage.prototype.setItem
  t.mock.method(window.Storage.prototype, 'setItem', function (this: Storage, key: string, value: string) {
    if (this === window.sessionStorage) throw new Error('session storage is read only')
    setItem.call(this, key, value)
  })
  t.mock.method(authApi, 'login', async () => languageSession)
  t.mock.method(profileApi, 'updateLanguage', async language => ({ ...languageProfile, preferredLanguage: language }))
  const view = await mountLanguage(React.createElement(LoginPage))
  try {
    await act(async () => view.container.querySelector<HTMLButtonElement>('button[title="English"]')!.click())
    await submitLogin(view.container)
    assert.equal(getAuthSession()?.user.preferredLanguage, 'en')
    assert.equal(i18n.language, 'en')
  } finally { await view.close() }
})

for (const failure of ['unauthorized', 'session storage'] as const) test(`a ${failure} failure remains visible after the account session is cleared`, async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); clearLoginLanguage(); saveAuthSession(languageSession); await setLanguage('fr')
  t.mock.method(profileApi, 'updateLanguage', async language => {
    if (failure === 'unauthorized') { clearAuthSession(); throw new ProfileApiError(401, 'unauthorized') }
    return { ...languageProfile, preferredLanguage: language }
  })
  if (failure === 'session storage') {
    const setItem = window.Storage.prototype.setItem
    t.mock.method(window.Storage.prototype, 'setItem', function (this: Storage, key: string, value: string) {
      if (key === 'stocklab-auth') throw new Error('auth storage write failed')
      setItem.call(this, key, value)
    })
  }
  const view = await mountLanguage(React.createElement(LanguageSelector))
  try {
    await act(async () => view.container.querySelector<HTMLButtonElement>('button[title="English"]')!.click())
    assert.equal(getAuthSession(), null)
    assert.ok(view.container.querySelector('[role="alert"]'))
  } finally { await view.close() }
})

test('shared intent storage failures stay visible and never start an uncoordinated account write', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); clearLoginLanguage(); saveAuthSession(languageSession); await setLanguage('fr')
  let writes = 0
  t.mock.method(profileApi, 'updateLanguage', async language => { writes++; return { ...languageProfile, preferredLanguage: language } })
  const setItem = window.Storage.prototype.setItem
  t.mock.method(window.Storage.prototype, 'setItem', function (this: Storage, key: string, value: string) {
    if (key.startsWith('stocklab-language-intent:')) throw new Error('intent storage is full')
    setItem.call(this, key, value)
  })
  const view = await mountLanguage(React.createElement(LanguageSelector))
  try {
    await act(async () => view.container.querySelector<HTMLButtonElement>('button[title="English"]')!.click())
    assert.equal(writes, 0)
    assert.equal(i18n.language, 'en')
    assert.ok(view.container.querySelector('[role="alert"]'))
  } finally { await view.close() }
})

test('a newer document choosing the same language prevents an older kept-alive write from winning', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); clearLoginLanguage(); saveAuthSession(languageSession)
  const first = await import('../src/i18n/languagePreference.ts')
  const next = await import('../src/i18n/languagePreference.ts?next-document')
  let version = 0, saved = 'en'
  let release!: () => void, reached!: () => void
  const blocked = new Promise<void>(resolve => { release = resolve })
  const ready = new Promise<void>(resolve => { reached = resolve })
  const etag = () => `"${Buffer.from([0, 0, 0, 0, 0, 0, 0, version]).toString('base64')}"`
  const wire = createProfileApi('', async (_url, init) => {
    if (init?.method === 'PUT') {
      const language = JSON.parse(String(init.body)).preferredLanguage
      if (language === 'fr') { reached(); await blocked }
      if (new Headers(init.headers).get('If-Match') !== etag()) return Response.json({ error: 'profile_update_conflict' }, { status: 412 })
      saved = language; version++
    }
    return Response.json({ ...languageProfile, preferredLanguage: saved }, { headers: { ETag: etag() } })
  }, () => ({ Authorization: 'Bearer test-token' }))
  t.mock.method(profileApi, 'updateLanguage', wire.updateLanguage)
  const older = first.saveLanguagePreference('fr', languageSession)
  const rejected = assert.rejects(older, (error: unknown) => first.isSupersededLanguageChange(error))
  await ready
  const newer = await next.saveLanguagePreference('en', languageSession)
  release(); await rejected
  assert.equal(newer.preferredLanguage, 'en')
  assert.equal(saved, 'en')
  assert.equal(version, 1, 'the same-value newer intent is a real versioned write')
  clearAuthSession()
})

test('a pending choice is resumed by the next document if navigation interrupted its version read', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); clearLoginLanguage(); saveAuthSession(languageSession)
  const first = await import('../src/i18n/languagePreference.ts')
  const next = await import('../src/i18n/languagePreference.ts?resumed-document')
  let readStarted!: () => void, release!: () => void
  const ready = new Promise<void>(resolve => { readStarted = resolve })
  const blocked = new Promise<void>(resolve => { release = resolve })
  let reads = 0, writes = 0
  const wire = createProfileApi('', async (_url, init) => {
    if (init?.method === 'GET' && ++reads === 1) { readStarted(); await blocked }
    if (init?.method === 'PUT') writes++
    return Response.json({ ...languageProfile, preferredLanguage: 'fr' }, { headers: { ETag: '"AQIDBAUGBwg="' } })
  }, () => ({ Authorization: 'Bearer test-token' }))
  t.mock.method(profileApi, 'updateLanguage', wire.updateLanguage)
  const older = first.saveLanguagePreference('fr', languageSession)
  const rejected = assert.rejects(older, (error: unknown) => first.isSupersededLanguageChange(error))
  await ready
  const pending = next.getPendingLanguageIntent(languageAccount.id)!
  assert.equal(pending.language, 'fr')
  await next.saveLanguagePreference(pending.language, languageSession, undefined, false, pending)
  release(); await rejected
  assert.equal(writes, 1)
  assert.equal(next.getPendingLanguageIntent(languageAccount.id), null)
  clearAuthSession()
})

test('only the latest intent retries a rowversion conflict and stops after a bounded number of attempts', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); clearLoginLanguage(); saveAuthSession(languageSession)
  const manager = await import('../src/i18n/languagePreference.ts')
  let attempts = 0
  t.mock.method(profileApi, 'updateLanguage', async language => {
    if (++attempts < 3) throw new ProfileApiError(412, 'profile_update_conflict')
    return { ...languageProfile, preferredLanguage: language }
  })
  await manager.saveLanguagePreference('en', languageSession)
  assert.equal(attempts, 3)
  attempts = 0
  t.mock.method(profileApi, 'updateLanguage', async () => { attempts++; throw new ProfileApiError(412, 'profile_update_conflict') })
  await assert.rejects(manager.saveLanguagePreference('fr', languageSession), (error: unknown) => error instanceof ProfileApiError && error.code === 'profile_update_conflict')
  assert.equal(attempts, 3)
  clearAuthSession()
})

test('finishing an old request cannot overwrite a newer intent published during its final ownership check', async t => {
  window.localStorage.clear(); window.sessionStorage.clear(); clearLoginLanguage(); saveAuthSession(languageSession)
  const first = await import('../src/i18n/languagePreference.ts')
  const next = await import('../src/i18n/languagePreference.ts?completion-race')
  let saved = 'en', version = 0, publishNew = false
  let newer!: Promise<UserProfile>
  const etag = () => `"${Buffer.from([0, 0, 0, 0, 0, 0, 0, version]).toString('base64')}"`
  const getItem = window.Storage.prototype.getItem
  t.mock.method(window.Storage.prototype, 'getItem', function (this: Storage, key: string) {
    const value = getItem.call(this, key)
    if (publishNew && key === 'stocklab-auth') {
      publishNew = false
      newer = next.saveLanguagePreference('en', languageSession)
    }
    return value
  })
  const wire = createProfileApi('', async (_url, init) => {
    if (init?.method === 'PUT') {
      if (new Headers(init.headers).get('If-Match') !== etag()) return Response.json({ error: 'profile_update_conflict' }, { status: 412 })
      saved = JSON.parse(String(init.body)).preferredLanguage; version++
      if (saved === 'fr') publishNew = true
    }
    return Response.json({ ...languageProfile, preferredLanguage: saved }, { headers: { ETag: etag() } })
  }, () => ({ Authorization: 'Bearer test-token' }))
  t.mock.method(profileApi, 'updateLanguage', wire.updateLanguage)
  await assert.rejects(first.saveLanguagePreference('fr', languageSession), first.isSupersededLanguageChange)
  assert.equal((await newer).preferredLanguage, 'en')
  assert.equal(saved, 'en')
  assert.equal(next.getPendingLanguageIntent(languageAccount.id), null)
  clearAuthSession()
})

for (const language of ['fr', 'en'] as const) test(`login restores account language ${language} before dashboard navigation`, async (t) => {
  window.localStorage.clear()
  await setLanguage(language === 'fr' ? 'en' : 'fr')
  const wireApi = createAuthApi('', async () => new Response(JSON.stringify({
    accessToken: 'test-session', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z',
    user: { id: 'account-id', displayName: 'Language account', email: 'language@example.com', preferredLanguage: language },
  })))
  t.mock.method(authApi, 'login', request => wireApi.login(request))
  const element = document.createElement('div'); document.body.append(element)
  const root = createRoot(element)
  try {
    await act(async () => root.render(React.createElement(LoginPage)))
    element.querySelector<HTMLInputElement>('[name="email"]')!.value = 'language@example.com'
    element.querySelector<HTMLInputElement>('[name="password"]')!.value = 'test-only-password'
    await act(async () => { element.querySelector('form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })) })
    assert.equal(i18n.language, language)
    assert.equal(document.documentElement.lang, language)
    assert.equal(window.localStorage.getItem(languageStorageKey), language)
    assert.equal(getAuthSession()?.user.preferredLanguage, language)
    assert.equal(redirects.at(-1), language)
  } finally { await act(async () => root.unmount()); element.remove(); clearAuthSession() }
})

test('saved language storage events update already open documents and reject unsupported values', async () => {
  await setLanguage('en')
  window.localStorage.setItem(languageStorageKey, 'fr')
  window.dispatchEvent(new dom.window.StorageEvent('storage', { key: languageStorageKey, newValue: 'fr' }))
  assert.equal(i18n.language, 'fr')
  assert.equal(document.documentElement.lang, 'fr')
  window.dispatchEvent(new dom.window.StorageEvent('storage', { key: languageStorageKey, newValue: 'es' }))
  window.dispatchEvent(new dom.window.StorageEvent('storage', { key: 'unrelated-key', newValue: 'en' }))
  assert.equal(i18n.language, 'fr')
  await setLanguage('en')
})

test('a missing or unsupported login preference cannot replace the browser preference or create a session', async () => {
  for (const preferredLanguage of [undefined, 'es', null]) {
    const api = createAuthApi('', async () => new Response(JSON.stringify({ accessToken: 'test-token', tokenType: 'Bearer',
      expiresAtUtc: '2099-01-01T00:00:00Z', user: { id: 'id', displayName: 'Name', email: 'test@example.com', preferredLanguage } })))
    await assert.rejects(api.login({ email: 'test@example.com', password: 'test-password' }), /invalid/i)
    assert.equal(getAuthSession(), null)
  }
})
