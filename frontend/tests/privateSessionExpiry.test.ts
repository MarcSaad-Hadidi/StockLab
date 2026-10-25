import assert from 'node:assert/strict'
import { test, type TestContext } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { profileApi } from '../src/api/profileApi.ts'
import { portfolioApi } from '../src/api/portfolioApi.ts'
import { watchlistApi } from '../src/api/watchlistApi.ts'
import { marketDataApi } from '../src/api/marketDataClient.ts'
import { clearAuthSession, getAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'
import { useAuthUser } from '../src/auth/useAuthUser.ts'
import { useProfileData } from '../src/profile/useProfileData.ts'
import { useWatchlistData } from '../src/watchlist/useWatchlistData.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/profile' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')

const user = { id: 'd4fd8828-1a30-46f7-86c0-8cbbfa644f66', displayName: 'Private Account', email: 'private@example.test' }
const profile = { ...user, createdAtUtc: '2026-09-24T23:40:00Z', updatedAtUtc: '2026-09-25T00:15:00Z' }
const capital = { cashBalance: '20000', initialCapital: '25000', investedValue: '5000', totalValue: '25000', currency: 'CAD', positions: [] }
const member = { symbol: 'AAPL', createdAtUtc: '2026-10-04T12:00:00Z' }
const quote = { symbol: 'AAPL', price: 200, priceDecimal: '200', change: 4, changePercent: 2, volume: null, currency: 'CAD',
  asOfUtc: '2026-10-04T12:00:00Z', name: 'Apple Inc.', exchange: 'NASDAQ', open: null, high: null, low: null,
  previousClose: null, averageVolume: null, isMarketOpen: null, fiftyTwoWeek: null }

function clock(t: TestContext) {
  let now = Date.parse('2026-10-24T12:00:00Z')
  let nextId = 0
  const timers = new Map<number, { due: number; run: () => void }>()
  t.mock.method(Date, 'now', () => now)
  t.mock.method(window, 'setTimeout', (callback, delay = 0, ...args) => {
    assert.equal(typeof callback, 'function')
    const id = ++nextId
    timers.set(id, { due: now + delay, run: () => (callback as (...args: unknown[]) => void)(...args) })
    return id
  })
  t.mock.method(window, 'clearTimeout', id => { timers.delete(id!) })
  window.localStorage.clear()
  saveAuthSession({ accessToken: 'private-token', tokenType: 'Bearer', expiresAtUtc: new Date(now + 5000).toISOString(), user })
  return {
    now: () => now,
    timers,
    async tick(milliseconds = 5000) {
      now += milliseconds
      await act(async () => {
        for (const [id, timer] of [...timers]) if (timer.due <= now) { timers.delete(id); timer.run() }
      })
    },
  }
}

function fixture(t: TestContext) {
  t.mock.method(profileApi, 'getProfile', async () => profile)
  t.mock.method(portfolioApi, 'getPortfolio', async () => capital)
  t.mock.method(watchlistApi, 'getWatchlist', async () => [member])
  t.mock.method(marketDataApi, 'quote', async () => quote)
}

async function mount(component: React.ReactElement) {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(component))
  return { container, async close() { await act(async () => root.unmount()); container.remove() } }
}

async function probe(enrich = false) {
  let state!: { identity: ReturnType<typeof useAuthUser>; profile: ReturnType<typeof useProfileData>; watchlist: ReturnType<typeof useWatchlistData> }
  function Probe() {
    state = { identity: useAuthUser(), profile: useProfileData(), watchlist: useWatchlistData(enrich) }
    return null
  }
  return { ...await mount(React.createElement(Probe)), state: () => state }
}

function assertCleared(state: ReturnType<Awaited<ReturnType<typeof probe>>['state']>) {
  assert.equal(state.identity, null)
  assert.equal(state.profile.profile, null)
  assert.equal(state.profile.capital, null)
  assert.equal(state.profile.loadError, 'unauthorized')
  assert.equal(state.profile.saving, false)
  assert.deepEqual(state.watchlist.items, [])
  assert.equal(state.watchlist.favorites.size, 0)
  assert.equal(state.watchlist.pendingSymbols.size, 0)
  assert.equal(state.watchlist.error, 'unauthorized')
}

for (const trigger of ['expiry', 'same-tab logout'] as const) {
  test(`shared identity, profile and watchlist clear together on ${trigger} without focus`, async t => {
    const time = clock(t); fixture(t)
    const view = await probe()
    try {
      assert.equal(view.state().profile.profile?.email, user.email)
      assert.equal(view.state().watchlist.favorites.has('AAPL'), true)
      if (trigger === 'expiry') await time.tick()
      else await act(async () => clearAuthSession())
      assertCleared(view.state())
      assert.equal(getAuthSession(), null)
    } finally { await view.close() }
    assert.equal(time.timers.size, 0, 'unmount must remove session timers')
  })
}

for (const resource of ['profile', 'capital', 'membership', 'quote'] as const) {
  test(`expiry aborts a pending ${resource} read and ignores its late success`, async t => {
    const time = clock(t); fixture(t)
    let signal!: AbortSignal
    let finish!: () => void
    if (resource === 'profile') t.mock.method(profileApi, 'getProfile', incoming => new Promise(resolve => { signal = incoming!; finish = () => resolve(profile) }))
    if (resource === 'capital') t.mock.method(portfolioApi, 'getPortfolio', incoming => new Promise(resolve => { signal = incoming!; finish = () => resolve(capital) }))
    if (resource === 'membership') t.mock.method(watchlistApi, 'getWatchlist', incoming => new Promise(resolve => { signal = incoming!; finish = () => resolve([member]) }))
    if (resource === 'quote') t.mock.method(marketDataApi, 'quote', (_symbol, incoming) => new Promise(resolve => { signal = incoming!; finish = () => resolve(quote) }))
    const view = await probe(resource === 'quote')
    try {
      await time.tick()
      assert.equal(signal.aborted, true)
      await act(async () => finish())
      assertCleared(view.state())
    } finally { await view.close() }
  })
}

for (const operation of ['profile save', 'watchlist add', 'watchlist remove'] as const) {
  test(`expiry aborts ${operation} and its late response cannot restore private data or report success`, async t => {
    const time = clock(t); fixture(t)
    let signal!: AbortSignal
    let finish!: () => void
    if (operation === 'profile save') t.mock.method(profileApi, 'updateProfile', (_request, incoming) => new Promise(resolve => { signal = incoming!; finish = () => resolve(profile) }))
    if (operation === 'watchlist add') t.mock.method(watchlistApi, 'addToWatchlist', (_symbol, incoming) => new Promise(resolve => { signal = incoming!; finish = () => resolve({ ...member, symbol: 'MSFT' }) }))
    if (operation === 'watchlist remove') t.mock.method(watchlistApi, 'removeFromWatchlist', (_symbol, incoming) => new Promise<void>(resolve => { signal = incoming!; finish = resolve }))
    const view = await probe()
    try {
      let pending!: Promise<boolean>
      await act(async () => { pending = operation === 'profile save' ? view.state().profile.save(user)
        : operation === 'watchlist add' ? view.state().watchlist.add('MSFT') : view.state().watchlist.remove('AAPL') })
      await time.tick()
      assert.equal(signal.aborted, true)
      assertCleared(view.state())
      await act(async () => { finish(); assert.equal(await pending, false) })
      assertCleared(view.state())
      assert.equal(getAuthSession(), null)
    } finally { await view.close() }
  })
}

test('same-account token rotation reschedules expiry and preserves the new session until its own deadline', async t => {
  const time = clock(t); fixture(t)
  const view = await probe()
  try {
    await act(async () => saveAuthSession({ ...getAuthSession()!, accessToken: 'rotated-token', expiresAtUtc: new Date(time.now() + 10000).toISOString() }))
    await time.tick()
    assert.equal(getAuthSession()?.accessToken, 'rotated-token')
    assert.equal(view.state().profile.profile?.email, user.email)
    assert.equal(view.state().watchlist.favorites.has('AAPL'), true)
    await time.tick()
    assertCleared(view.state())
  } finally { await view.close() }
})

test('a login after expiry reloads the mounted private hooks without returning to the expired account', async t => {
  const time = clock(t); fixture(t)
  const view = await probe()
  try {
    await time.tick()
    assertCleared(view.state())
    const nextUser = { ...user, id: '91f8d5fd-cb7d-4852-b3a5-4c0c8d440a8c', displayName: 'Next Account', email: 'next@example.test' }
    t.mock.method(profileApi, 'getProfile', async () => ({ ...profile, ...nextUser }))
    t.mock.method(portfolioApi, 'getPortfolio', async () => ({ ...capital, initialCapital: '50000', currency: 'USD' }))
    t.mock.method(watchlistApi, 'getWatchlist', async () => [{ ...member, symbol: 'MSFT' }])
    await act(async () => saveAuthSession({ accessToken: 'next-token', tokenType: 'Bearer', expiresAtUtc: new Date(time.now() + 5000).toISOString(), user: nextUser }))
    assert.deepEqual(view.state().identity, nextUser)
    assert.equal(view.state().profile.profile?.email, nextUser.email)
    assert.equal(view.state().profile.capital?.initialCapital, '50000')
    assert.deepEqual([...view.state().watchlist.favorites], ['MSFT'])
    await time.tick()
    assertCleared(view.state())
  } finally { await view.close() }
})

for (const language of ['en', 'fr']) {
  test(`Profile removes the visible edit draft, capital and password dialog on expiry in ${language}`, async t => {
    const time = clock(t); fixture(t)
    await i18n.changeLanguage(language)
    const { default: Page } = await import('../src/profile/ProfilePage.tsx')
    const view = await mount(React.createElement(Page))
    try {
      assert.match(view.container.textContent!, /private@example.test/)
      await act(async () => (view.container.querySelector('.summary-primary') as HTMLButtonElement).click())
      await act(async () => (view.container.querySelector('.summary-secondary') as HTMLButtonElement).click())
      assert.ok(view.container.querySelector('[role="dialog"]'))
      await time.tick()
      assert.doesNotMatch(view.container.textContent!, /Private Account|private@example.test|25[ ,]000/)
      assert.equal(view.container.querySelector('#profile-form input'), null)
      assert.equal(view.container.querySelector('[role="dialog"]'), null)
      assert.ok(view.container.querySelector('a[href="/login"]'))
      assert.equal(view.container.querySelector('.app-topbar-avatar')?.textContent, '')
    } finally { await view.close() }
  })

  test(`Watchlist clears rows, summary and the previous account's success notice on same-tab logout in ${language}`, async t => {
    clock(t); fixture(t)
    await i18n.changeLanguage(language)
    t.mock.method(watchlistApi, 'getWatchlist', async () => [member, { ...member, symbol: 'MSFT' }])
    t.mock.method(watchlistApi, 'removeFromWatchlist', async () => {})
    const { default: Page } = await import('../src/watchlist/WatchlistPage.tsx')
    const view = await mount(React.createElement(Page))
    try {
      const remove = view.container.querySelector('.watchlist-row .remove-button') as HTMLButtonElement
      assert.ok(remove)
      await act(async () => remove.click())
      assert.ok(view.container.querySelector('.toast.visible'))
      await act(async () => clearAuthSession())
      assert.equal(view.container.querySelector('.watchlist-row'), null)
      assert.equal(view.container.querySelector('.toast')?.textContent, '')
      assert.ok(view.container.querySelector('a[href="/login"]'))
      assert.equal(view.container.querySelector('.app-topbar-avatar')?.textContent, '')
    } finally { await view.close() }
  })
}
