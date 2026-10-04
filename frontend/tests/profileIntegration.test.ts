import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { profileApi, ProfileApiError } from '../src/api/profileApi.ts'
import { portfolioApi } from '../src/api/portfolioApi.ts'
import { saveAuthSession, getAuthSession, clearAuthSession } from '../src/auth/authStorage.ts'
import { useProfileData } from '../src/profile/useProfileData.ts'

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

const user = { id: 'd4fd8828-1a30-46f7-86c0-8cbbfa644f66', displayName: 'Samira Martin', email: 'samira@example.com' }
const session = { accessToken: 'signed-token', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z', user }
const profile = { ...user, createdAtUtc: '2026-09-24T23:40:00Z', updatedAtUtc: '2026-09-25T00:15:00Z' }
const capital = { cashBalance: 20000, initialCapital: 25000, investedValue: 5000, totalValue: 25000, currency: 'CAD', positions: [] }
async function mount(component: React.ReactElement) {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(component))
  return { container, async close() { await act(async () => root.unmount()); container.remove() } }
}
function seed() { window.localStorage.clear(); saveAuthSession(session) }

test('Profile renders the server account and actual capital without manufactured account details', async (t) => {
  seed()
  t.mock.method(profileApi, 'getProfile', async () => profile)
  t.mock.method(portfolioApi, 'getPortfolio', async () => capital)
  const { default: Page } = await import('../src/profile/ProfilePage.tsx')
  const view = await mount(React.createElement(Page))
  try {
    assert.match(view.container.textContent!, /Samira Martin/)
    assert.match(view.container.textContent!, /samira@example.com/)
    assert.match(view.container.textContent!, /CA\$25,000\.00/)
    assert.doesNotMatch(view.container.textContent!, /Alex Johnson|123-4567|3 Active|Verified Account|Preview mode/)
    assert.ok((view.container.querySelector('[aria-label="Edit Phone Number"]') as HTMLButtonElement).disabled)
  } finally { await view.close() }
})

test('successful edits use the server result and survive a new page load', async (t) => {
  seed()
  let persisted = profile
  t.mock.method(profileApi, 'getProfile', async () => persisted)
  t.mock.method(profileApi, 'updateProfile', async (request) => { persisted = { ...profile, ...request, displayName: request.displayName.trim() }; return persisted })
  t.mock.method(portfolioApi, 'getPortfolio', async () => capital)
  let state: ReturnType<typeof useProfileData>
  function Probe() { state = useProfileData(); return null }
  let view = await mount(React.createElement(Probe))
  try {
    await act(async () => { assert.equal(await state.save({ displayName: ' New Name ', email: 'new@example.com' }), true) })
    assert.equal(state!.profile?.displayName, 'New Name')
    assert.equal(getAuthSession()?.user.displayName, 'New Name')
    assert.equal(getAuthSession()?.accessToken, session.accessToken)
  } finally { await view.close() }
  view = await mount(React.createElement(Probe))
  try { assert.equal(state!.profile?.displayName, 'New Name'); assert.equal(state!.profile?.email, 'new@example.com') }
  finally { await view.close() }
})

test('failed saves keep the existing profile and session and do not report success', async (t) => {
  seed()
  t.mock.method(profileApi, 'getProfile', async () => profile)
  t.mock.method(portfolioApi, 'getPortfolio', async () => { throw new Error('offline') })
  t.mock.method(profileApi, 'updateProfile', async () => { throw new ProfileApiError(409, 'email_already_registered') })
  let state: ReturnType<typeof useProfileData>
  function Probe() { state = useProfileData(); return null }
  const view = await mount(React.createElement(Probe))
  try {
    assert.equal(state!.profile?.displayName, user.displayName)
    assert.equal(state!.capital, null)
    await act(async () => { assert.equal(await state.save({ displayName: 'Other Name', email: 'taken@example.com' }), false) })
    assert.equal(state!.saveError, 'email_already_registered')
    assert.equal(state!.profile?.email, user.email)
    assert.equal(getAuthSession()?.user.email, user.email)
  } finally { await view.close() }
})

test('a late save cannot restore a logged-out account or update another session', async (t) => {
  seed()
  t.mock.method(profileApi, 'getProfile', async () => profile)
  t.mock.method(portfolioApi, 'getPortfolio', async () => capital)
  let finish!: (value: typeof profile) => void
  t.mock.method(profileApi, 'updateProfile', () => new Promise<typeof profile>(resolve => { finish = resolve }))
  let state: ReturnType<typeof useProfileData>
  function Probe() { state = useProfileData(); return null }
  const view = await mount(React.createElement(Probe))
  try {
    let saving!: Promise<boolean>
    await act(async () => { saving = state.save({ displayName: 'Other Name', email: user.email }) })
    clearAuthSession()
    await act(async () => { finish({ ...profile, displayName: 'Other Name' }); assert.equal(await saving, false) })
    assert.equal(getAuthSession(), null)
    assert.equal(state!.profile, null)
    assert.equal(state!.loadError, 'unauthorized')
  } finally { await view.close() }
})

test('anonymous profile loads display a sign-in state and never show the example identity', async () => {
  window.localStorage.clear()
  const { default: Page } = await import('../src/profile/ProfilePage.tsx')
  const view = await mount(React.createElement(Page))
  try {
    assert.match(view.container.textContent!, /sign in/i)
    assert.doesNotMatch(view.container.textContent!, /Alex Johnson|alex.johnson@example.com/)
    assert.ok(view.container.querySelector('a[href="/login"]'))
  } finally { await view.close() }
})

test('the visible edit form sends changes and only confirms server success', async (t) => {
  seed()
  t.mock.method(profileApi, 'getProfile', async () => profile)
  t.mock.method(portfolioApi, 'getPortfolio', async () => capital)
  const requests: unknown[] = []
  let fail = true
  t.mock.method(profileApi, 'updateProfile', async (request) => {
    requests.push(request)
    if (fail) throw new ProfileApiError(409, 'email_already_registered')
    return { ...profile, ...request, displayName: request.displayName.trim() }
  })
  const { default: Page } = await import('../src/profile/ProfilePage.tsx')
  const view = await mount(React.createElement(Page))
  const inputValue = (label: string, value: string) => {
    const input = view.container.querySelector(`input[aria-label="${label}"]`) as HTMLInputElement
    Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(input, value)
    input.dispatchEvent(new dom.window.Event('input', { bubbles: true }))
  }
  try {
    const edit = Array.from(view.container.querySelectorAll('button')).find(button => button.textContent === 'Edit Profile')!
    await act(async () => edit.click())
    assert.equal(requests.length, 0, 'opening the editor must not save the profile')
    await act(async () => { inputValue('Full Name', ' New Name '); inputValue('Email Address', 'new@example.com') })
    const form = view.container.querySelector('#profile-form')!
    await act(async () => { form.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })) })
    assert.deepEqual(requests[0], { displayName: ' New Name ', email: 'new@example.com' })
    assert.match(view.container.querySelector('[role="alert"]')!.textContent!, /already used/)
    assert.equal(view.container.querySelector('input[aria-label="Email Address"]')!.getAttribute('aria-invalid'), 'true')
    assert.doesNotMatch(view.container.textContent!, /Your profile has been saved/)
    assert.equal(view.container.querySelector('h1')!.textContent, user.displayName)
    fail = false
    await act(async () => { form.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })) })
    assert.match(view.container.textContent!, /Your profile has been saved/)
    assert.equal(view.container.querySelector('h1')!.textContent, 'New Name')
    assert.equal(view.container.querySelector('input[aria-label="Full Name"]'), null)
  } finally { await view.close() }
})

test('an unauthorized response clears the session and ignores a later portfolio response', async (t) => {
  seed()
  t.mock.method(profileApi, 'getProfile', async () => { throw new ProfileApiError(401, 'unauthorized') })
  let finish!: (value: typeof capital) => void
  t.mock.method(portfolioApi, 'getPortfolio', () => new Promise<typeof capital>(resolve => { finish = resolve }))
  let state: ReturnType<typeof useProfileData>
  function Probe() { state = useProfileData(); return null }
  const view = await mount(React.createElement(Probe))
  try {
    await act(async () => finish(capital))
    assert.equal(getAuthSession(), null)
    assert.equal(state!.profile, null)
    assert.equal(state!.capital, null)
    assert.equal(state!.loading, false)
    assert.equal(state!.loadError, 'unauthorized')
  } finally { await view.close() }
})

test('a save that fails after another login cannot retain the previous account', async (t) => {
  seed()
  t.mock.method(profileApi, 'getProfile', async () => profile)
  t.mock.method(portfolioApi, 'getPortfolio', async () => capital)
  let reject!: (error: Error) => void
  t.mock.method(profileApi, 'updateProfile', () => new Promise<typeof profile>((_, fail) => { reject = fail }))
  let state: ReturnType<typeof useProfileData>
  function Probe() { state = useProfileData(); return null }
  const view = await mount(React.createElement(Probe))
  try {
    let saving!: Promise<boolean>
    await act(async () => { saving = state.save({ displayName: 'New Name', email: user.email }) })
    const other = { ...session, accessToken: 'another-token', user: { ...user, id: '91f8d5fd-cb7d-4852-b3a5-4c0c8d440a8c', displayName: 'Another Account' } }
    saveAuthSession(other)
    await act(async () => { reject(new ProfileApiError(401, 'unauthorized')); assert.equal(await saving, false) })
    assert.equal(getAuthSession()?.accessToken, other.accessToken)
    assert.equal(state!.profile, null)
    assert.equal(state!.capital, null)
    assert.equal(state!.loadError, 'unauthorized')
  } finally { await view.close() }
})
