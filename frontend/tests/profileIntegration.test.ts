import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { profileApi, ProfileApiError } from '../src/api/profileApi.ts'
import { portfolioApi } from '../src/api/portfolioApi.ts'
import { saveAuthSession, getAuthSession, clearAuthSession, authStorageKey } from '../src/auth/authStorage.ts'
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

for (const trigger of ['storage', 'focus', 'save'] as const) {
  test(`same-account changes reload the profile on ${trigger} and discard the stale draft`, async (t) => {
    seed()
    let persisted = profile
    const get = t.mock.method(profileApi, 'getProfile', async () => persisted)
    const put = t.mock.method(profileApi, 'updateProfile', async (request) => ({ ...persisted, ...request }))
    t.mock.method(portfolioApi, 'getPortfolio', async () => capital)
    const { default: Page } = await import('../src/profile/ProfilePage.tsx')
    const view = await mount(React.createElement(Page))
    try {
      const edit = Array.from(view.container.querySelectorAll('button')).find(button => button.textContent === 'Edit Profile')!
      await act(async () => edit.click())
      assert.ok(view.container.querySelector('input[aria-label="Full Name"]'))
      persisted = { ...profile, displayName: 'Updated Elsewhere', email: 'elsewhere@example.com' }
      saveAuthSession({ ...session, user: persisted })
      await act(async () => {
        if (trigger === 'storage') window.dispatchEvent(new dom.window.StorageEvent('storage', { key: authStorageKey }))
        else if (trigger === 'focus') window.dispatchEvent(new dom.window.Event('focus'))
        else view.container.querySelector('#profile-form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }))
      })
      assert.match(view.container.textContent!, /elsewhere@example.com/)
      assert.equal(view.container.querySelector('[role="alert"]'), null)
      assert.equal(put.mock.callCount(), 0)
      assert.equal(view.container.querySelector('h1')!.textContent, persisted.displayName)
      assert.equal(view.container.querySelector('input[aria-label="Full Name"]'), null)
      const count = get.mock.callCount()
      await act(async () => window.dispatchEvent(new dom.window.Event('focus')))
      assert.equal(get.mock.callCount(), count, 'unchanged identity must not reload')
    } finally { await view.close() }
  })
}

for (const code of ['email_already_registered', 'validation_error', 'offline'] as const) {
  test(`${code} errors follow the edited fields and do not survive cancel and reopen`, async (t) => {
    seed()
    t.mock.method(profileApi, 'getProfile', async () => profile)
    t.mock.method(portfolioApi, 'getPortfolio', async () => capital)
    const put = t.mock.method(profileApi, 'updateProfile', async () => {
      throw new ProfileApiError(code === 'validation_error' ? 400 : 409, code,
        code === 'validation_error' ? { DisplayName: ['invalid name'], Email: ['invalid email'] } : {})
    })
    const { default: Page } = await import('../src/profile/ProfilePage.tsx')
    const view = await mount(React.createElement(Page))
    const button = (label: string) => Array.from(view.container.querySelectorAll('button')).find(item => item.textContent === label)!
    const input = (label: string) => view.container.querySelector(`input[aria-label="${label}"]`) as HTMLInputElement
    const change = (label: string, value: string) => {
      Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(input(label), value)
      input(label).dispatchEvent(new dom.window.Event('input', { bubbles: true }))
    }
    const submit = () => view.container.querySelector('#profile-form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true }))
    try {
      await act(async () => button('Edit Profile').click())
      await act(async () => submit())
      assert.ok(view.container.querySelector('[role="alert"]'))
      await act(async () => change('Full Name', 'Corrected Name'))
      assert.equal(input('Full Name').getAttribute('aria-invalid'), 'false')
      if (code !== 'offline') {
        assert.ok(view.container.querySelector('[role="alert"]'), 'errors on the untouched email remain visible')
        assert.equal(input('Email Address').getAttribute('aria-invalid'), 'true')
      }
      await act(async () => change('Email Address', 'corrected@example.com'))
      assert.equal(view.container.querySelector('[role="alert"]'), null)
      assert.equal(input('Email Address').getAttribute('aria-invalid'), 'false')
      assert.equal(put.mock.callCount(), 1, 'editing must not submit')
      await act(async () => view.container.querySelector<HTMLButtonElement>('[aria-label="Edit Full Name"]')!.click())
      assert.equal(input('Full Name').value, 'Corrected Name', 'row edit buttons must not reset the active draft')
      await act(async () => submit())
      assert.ok(view.container.querySelector('[role="alert"]'))
      await act(async () => button('Cancel').click())
      await act(async () => button('Edit Profile').click())
      assert.equal(view.container.querySelector('[role="alert"]'), null)
      assert.equal(input('Full Name').getAttribute('aria-invalid'), 'false')
      assert.equal(input('Email Address').getAttribute('aria-invalid'), 'false')
      assert.equal(input('Full Name').value, user.displayName)
      assert.equal(input('Email Address').value, user.email)
      assert.equal(getAuthSession()?.user.email, user.email)
      assert.doesNotMatch(view.container.textContent!, /Your profile has been saved/)
    } finally { await view.close() }
  })
}

for (const trigger of ['storage', 'focus', 'save'] as const) {
  test(`a same-account token rotation reloads on ${trigger} using the current credentials`, async (t) => {
    seed()
    const tokens: (string | undefined)[] = []
    t.mock.method(profileApi, 'getProfile', async () => { tokens.push(getAuthSession()?.accessToken); return profile })
    t.mock.method(portfolioApi, 'getPortfolio', async () => capital)
    const put = t.mock.method(profileApi, 'updateProfile', async request => ({ ...profile, ...request }))
    let state: ReturnType<typeof useProfileData>
    function Probe() { state = useProfileData(); return null }
    const view = await mount(React.createElement(Probe))
    try {
      saveAuthSession({ ...session, accessToken: 'rotated-token' })
      await act(async () => {
        if (trigger === 'storage') window.dispatchEvent(new dom.window.StorageEvent('storage', { key: authStorageKey }))
        else if (trigger === 'focus') window.dispatchEvent(new dom.window.Event('focus'))
        else assert.equal(await state.save({ displayName: 'Stale Draft', email: user.email }), false)
      })
      assert.deepEqual(tokens, [session.accessToken, 'rotated-token'])
      assert.equal(state!.loadError, null)
      assert.equal(state!.loading, false)
      assert.equal(state!.profile?.id, user.id)
      assert.equal(state!.capital?.currency, 'CAD')
      assert.equal(put.mock.callCount(), 0)
      await act(async () => { assert.equal(await state.save({ displayName: 'Fresh Draft', email: user.email }), true) })
      assert.equal(state!.profile?.displayName, 'Fresh Draft')
      assert.equal(getAuthSession()?.accessToken, 'rotated-token')
    } finally { await view.close() }
  })
}

for (const operation of ['load', 'save'] as const) {
  for (const outcome of ['success', 'unauthorized'] as const) {
    test(`a late ${operation} ${outcome} cannot invalidate or overwrite a rotated token`, async (t) => {
      seed()
      let resolve!: (value: typeof profile) => void
      let reject!: (error: Error) => void
      const pending = new Promise<typeof profile>((ok, fail) => { resolve = ok; reject = fail })
      let calls = 0
      const tokens: (string | undefined)[] = []
      t.mock.method(profileApi, 'getProfile', async () => {
        tokens.push(getAuthSession()?.accessToken)
        if (operation === 'load' && calls++ === 0) return pending
        return profile
      })
      t.mock.method(profileApi, 'updateProfile', () => pending)
      t.mock.method(portfolioApi, 'getPortfolio', async () => capital)
      let state: ReturnType<typeof useProfileData>
      function Probe() { state = useProfileData(); return null }
      const view = await mount(React.createElement(Probe))
      try {
        let saving: Promise<boolean> | undefined
        if (operation === 'save') await act(async () => { saving = state.save({ displayName: 'Old Token Result', email: user.email }) })
        saveAuthSession({ ...session, accessToken: 'rotated-token' })
        // No storage/focus notification: the response guard must detect the new login itself.
        await act(async () => {
          if (outcome === 'success') resolve({ ...profile, displayName: 'Old Token Result' })
          else reject(new ProfileApiError(401, 'unauthorized'))
          if (saving) assert.equal(await saving, false)
        })
        assert.deepEqual(tokens, [session.accessToken, 'rotated-token'])
        assert.equal(getAuthSession()?.accessToken, 'rotated-token')
        assert.equal(getAuthSession()?.user.displayName, user.displayName)
        assert.equal(state!.profile?.displayName, user.displayName)
        assert.equal(state!.loadError, null)
        assert.equal(state!.saveError, null)
        assert.equal(state!.loading, false)
        assert.equal(state!.saving, false)
      } finally { await view.close() }
    })
  }
}
