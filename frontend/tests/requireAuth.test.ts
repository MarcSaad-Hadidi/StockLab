import assert from 'node:assert/strict'
import { beforeEach, test, type TestContext } from 'node:test'
import { JSDOM } from 'jsdom'
import React, { act, StrictMode, useEffect, useState } from 'react'
import { authStorageKey, clearAuthSession, getAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'

const dom = new JSDOM('', { url: 'http://localhost/dashboard' })
const destinations: string[] = []
// JSDOM cannot navigate. Replace only this browser boundary; React and storage stay real.
const location = { replace: (url: string) => destinations.push(url) }
const browser = new Proxy(dom.window, { get: (target, key) => key === 'location' ? location : Reflect.get(target, key, target) })
Object.assign(globalThis, { React, window: browser, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { RequireAuth } = await import('../src/auth/RequireAuth.tsx')
const session = { accessToken: 'token-a', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Account A', email: 'a@example.test' } }
beforeEach(() => { window.localStorage.clear(); destinations.length = 0 })

async function mount() {
  let mounts = 0
  let cleanups = 0
  let signal!: AbortSignal
  function PrivateContent() {
    const [draft, setDraft] = useState('private draft')
    useEffect(() => {
      mounts++
      const controller = new AbortController(); signal = controller.signal
      return () => { controller.abort(); cleanups++ }
    }, [])
    return React.createElement('button', { onClick: () => setDraft('changed draft') }, `${getAuthSession()!.user.id}: ${draft}`)
  }
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(StrictMode, null,
    React.createElement(RequireAuth, null, React.createElement(PrivateContent)))))
  return { container, mounts: () => mounts, cleanups: () => cleanups, signal: () => signal,
    async close() { await act(async () => root.unmount()); container.remove() } }
}

for (const stored of [null, '{}', 'invalid json', JSON.stringify({ ...session, expiresAtUtc: '2000-01-01T00:00:00Z' })]) {
  test(`missing, malformed or expired sessions redirect without mounting private content: ${stored}`, async () => {
    if (stored) window.localStorage.setItem(authStorageKey, stored)
    const view = await mount()
    try {
      assert.equal(view.container.textContent, '')
      assert.equal(view.mounts(), 0)
      assert.deepEqual(destinations, ['/login'], 'StrictMode must not duplicate the redirect')
      assert.equal(window.localStorage.getItem(authStorageKey), null)
    } finally { await view.close() }
  })
}

test('a valid session mounts the private page without redirecting', async () => {
  saveAuthSession(session)
  const view = await mount()
  try { assert.match(view.container.textContent!, /account-a: private draft/); assert.deepEqual(destinations, []) }
  finally { await view.close() }
})

for (const trigger of ['logout', 'storage', 'storage clear', 'focus', 'visibility', 'pageshow']) {
  test(`${trigger} removes private content, aborts its work and replaces the URL with Login`, async () => {
    saveAuthSession(session)
    const view = await mount()
    try {
      await act(async () => {
        if (trigger === 'logout') clearAuthSession()
        else {
          window.localStorage.removeItem(authStorageKey)
          if (trigger === 'storage' || trigger === 'storage clear') window.dispatchEvent(new dom.window.StorageEvent('storage', { key: trigger === 'storage' ? authStorageKey : null }))
          else if (trigger === 'visibility') {
            Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
            document.dispatchEvent(new dom.window.Event('visibilitychange'))
          } else window.dispatchEvent(new dom.window.Event(trigger))
        }
      })
      assert.equal(view.container.textContent, '')
      assert.equal(view.signal().aborted, true)
      assert.equal(view.cleanups(), view.mounts())
      assert.deepEqual(destinations, ['/login'])
    } finally { await view.close() }
  })
}

function clock(t: TestContext) {
  let now = Date.parse('2026-10-26T12:00:00Z')
  let nextId = 0
  const timers = new Map<number, { due: number; run: () => void }>()
  t.mock.method(Date, 'now', () => now)
  t.mock.method(window, 'setTimeout', (callback, delay = 0) => {
    const id = ++nextId
    timers.set(id, { due: now + delay, run: callback as () => void })
    return id
  })
  t.mock.method(window, 'clearTimeout', id => { timers.delete(id!) })
  return { timers, now: () => now, async tick() {
    now += 5000
    await act(async () => { for (const [id, timer] of [...timers]) if (timer.due <= now) { timers.delete(id); timer.run() } })
  } }
}

test('expiry while idle unmounts the page without a focus event and cancels timers', async t => {
  const time = clock(t)
  saveAuthSession({ ...session, expiresAtUtc: new Date(time.now() + 5000).toISOString() })
  const view = await mount()
  try {
    await time.tick()
    assert.equal(view.container.textContent, '')
    assert.equal(view.signal().aborted, true)
    assert.deepEqual(destinations, ['/login'])
    assert.equal(getAuthSession(), null)
  } finally { await view.close() }
  assert.equal(time.timers.size, 0)
})

test('same-account token rotation keeps the page and uses the new expiry deadline', async t => {
  const time = clock(t)
  saveAuthSession({ ...session, expiresAtUtc: new Date(time.now() + 5000).toISOString() })
  const view = await mount()
  try {
    await act(async () => view.container.querySelector('button')!.click())
    const previousMounts = view.mounts()
    await act(async () => saveAuthSession({ ...session, accessToken: 'token-b', expiresAtUtc: new Date(time.now() + 10000).toISOString() }))
    await time.tick()
    assert.equal(view.mounts(), previousMounts)
    assert.match(view.container.textContent!, /changed draft/)
    assert.deepEqual(destinations, [])
    await time.tick()
    assert.equal(view.container.textContent, '')
    assert.deepEqual(destinations, ['/login'])
  } finally { await view.close() }
})

test('account replacement destroys the previous owner’s local form state', async () => {
  saveAuthSession(session)
  const view = await mount()
  try {
    await act(async () => view.container.querySelector('button')!.click())
    const previousSignal = view.signal()
    await act(async () => saveAuthSession({ ...session, accessToken: 'token-b', user: { ...session.user, id: 'account-b' } }))
    assert.match(view.container.textContent!, /account-b: private draft/)
    assert.doesNotMatch(view.container.textContent!, /account-a|changed draft/)
    assert.equal(previousSignal.aborted, true)
    assert.deepEqual(destinations, [])
  } finally { await view.close() }
})

test('storage read failure fails closed without mounting private content', async t => {
  saveAuthSession(session)
  t.mock.method(dom.window.Storage.prototype, 'getItem', () => { throw new Error('blocked') })
  const view = await mount()
  try { assert.equal(view.mounts(), 0); assert.deepEqual(destinations, ['/login']) }
  finally { await view.close() }
})
