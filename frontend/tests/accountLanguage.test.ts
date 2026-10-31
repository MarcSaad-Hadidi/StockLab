import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM, VirtualConsole } from 'jsdom'
import React, { act } from 'react'
import { authApi, createAuthApi } from '../src/api/authApi.ts'
import { getAuthSession, clearAuthSession } from '../src/auth/authStorage.ts'

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
