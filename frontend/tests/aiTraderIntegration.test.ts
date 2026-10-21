import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { createAiTraderApi } from '../src/api/aiTraderApi.ts'
import { saveAuthSession, clearAuthSession, getAuthorizationHeader, type AuthSession } from '../src/auth/authStorage.ts'
import { useAiTraderData, type AiTraderDataState } from '../src/ai-trader/useAiTraderData.ts'
import type { TabName } from '../src/ai-trader/aiTraderData.ts'
import { bodies, performance } from './fixtures/aiTrader.ts'

const dom = new JSDOM('', { url: 'http://localhost/ai-trader' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const account: AuthSession = { accessToken: 'token-a', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z', user: { id: 'a', displayName: 'Account A', email: 'a@example.test' } }
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
const settle = async () => { await act(async () => { await new Promise(resolve => setTimeout(resolve, 30)) }) }
beforeEach(() => { window.localStorage.clear(); saveAuthSession(account) })
async function mount(fetcher: typeof fetch, initial: TabName = 'Overview') {
  let state: AiTraderDataState
  const api = createAiTraderApi('', fetcher, getAuthorizationHeader)
  const container = document.createElement('div')
  document.body.append(container)
  const root = createRoot(container)
  function Probe({ tab }: { tab: TabName }) { state = useAiTraderData(tab, api); return React.createElement('output', null, JSON.stringify(state.resources)) }
  await act(async () => root.render(React.createElement(Probe, { tab: initial })))
  return { get state() { return state! }, container,
    tab: async (tab: TabName) => { await act(async () => root.render(React.createElement(Probe, { tab }))); await settle() },
    close: async () => { await act(async () => root.unmount()); container.remove() } }
}
test('Overview bounds concurrent reads, reuses cached data and upgrades only detailed histories', async () => {
  const paths: string[] = []
  let concurrent = 0, max = 0
  const view = await mount(async input => {
    const path = String(input).split('/api/ai-trader/')[1]
    paths.push(path); concurrent++; max = Math.max(max, concurrent)
    await new Promise(resolve => setTimeout(resolve, 5)); concurrent--
    return json(bodies[path.split('?')[0]])
  })
  try {
    await settle(); await settle()
    assert.equal(view.state.resources.performance.data?.totalValue, '108000')
    assert.ok(max <= 3)
    assert.equal(paths.length, 8)
    assert.equal(paths.filter(path => path === 'performance').length, 1)
    assert.ok(!paths.includes('overview'))
    await view.tab('Positions'); await view.tab('Model')
    assert.equal(paths.length, 8)
    await view.tab('Trades')
    assert.ok(paths.includes('trades?limit=200'))
    await act(async () => view.state.refresh()); await settle()
    assert.equal(paths.filter(path => path === 'trades?limit=200').length, 2)
  } finally { await view.close() }
})
test('an unavailable catalog leaves metrics and positions visible; retry targets only that source', async () => {
  let available = false
  const paths: string[] = []
  const view = await mount(async input => {
    const path = String(input).split('/api/ai-trader/')[1].split('?')[0]; paths.push(path)
    return path === 'models' && !available ? json({}, 503) : json(bodies[path])
  })
  try {
    await settle()
    assert.equal(view.state.resources.models.status, 'unavailable')
    assert.equal(view.state.resources.performance.status, 'success')
    assert.equal(view.state.resources.positions.data?.[0].symbol, 'TEST')
    const count = paths.length
    available = true
    await act(async () => view.state.retry('models')); await settle()
    assert.equal(paths.length, count + 1)
    assert.equal(view.state.resources.models.status, 'success')
  } finally { await view.close() }
})
test('a detailed tab finishes loading without fetching unrelated resources', async () => {
  const paths: string[] = []
  const view = await mount(async input => { const path = String(input).split('/api/ai-trader/')[1].split('?')[0]; paths.push(path); return json(bodies[path]) }, 'Performance')
  try {
    await settle()
    assert.deepEqual(paths, ['performance', 'performance/history'])
    assert.equal(view.state.isLoading, false)
  } finally { await view.close() }
})
test('logout, account replacement and token rotation erase data and ignore late old responses', async () => {
  const pending: Array<{ path: string; token: string; signal: AbortSignal | undefined; resolve: (response: Response) => void }> = []
  const view = await mount(async (input, init) => new Promise<Response>(resolve => pending.push({ path: String(input), token: (init?.headers as Record<string, string>).Authorization, signal: init?.signal ?? undefined, resolve })), 'Performance')
  try {
    const old = [...pending]
    assert.ok(old.length > 0)
    await act(async () => saveAuthSession({ ...account, accessToken: 'token-b', user: { ...account.user, id: 'b' } }))
    assert.equal(view.state.resources.performance.data, null)
    assert.ok(old.every(request => request.signal?.aborted))
    await act(async () => pending.filter(request => request.token === 'Bearer token-b').forEach(request => request.resolve(json(request.path.includes('/history') ? [] : { ...performance, totalValue: '222222' }))))
    await settle()
    await act(async () => old.forEach(request => request.resolve(json(performance))))
    assert.equal(view.state.resources.performance.data?.totalValue, '222222')
    await act(async () => saveAuthSession({ ...account, accessToken: 'token-c', user: { ...account.user, id: 'b' } }))
    assert.equal(view.state.resources.performance.data, null)
    const late = [...pending]
    await act(async () => clearAuthSession())
    assert.equal(view.state.resources.performance.status, 'unauthorized')
    await act(async () => late.forEach(request => request.resolve(json(performance))))
    assert.equal(view.state.resources.performance.data, null)
  } finally { await view.close() }
})
test('401 clears all protected resources and cancellation stops the remaining queue', async () => {
  let fail = false
  const signals: AbortSignal[] = []
  const view = await mount(async (input, init) => {
    if (init?.signal) signals.push(init.signal)
    return json(fail ? {} : bodies[String(input).split('/api/ai-trader/')[1].split('?')[0]], fail ? 401 : 200)
  })
  try {
    await settle(); fail = true
    await act(async () => view.state.refresh()); await settle()
    assert.ok(Object.values(view.state.resources).every(resource => resource.data === null && resource.status === 'unauthorized'))
  } finally { await view.close() }
  assert.ok(signals.slice(-3).some(signal => signal.aborted))
})
test('automatic session expiry masks loaded resources while the hook stays mounted', async t => {
  let now = Date.parse('2026-10-20T12:00:00Z')
  t.mock.method(Date, 'now', () => now)
  saveAuthSession({ ...account, expiresAtUtc: new Date(now + 5000).toISOString() })
  const callbacks: Array<() => void> = []
  const originalTimeout = window.setTimeout.bind(window)
  t.mock.method(window, 'setTimeout', (callback, milliseconds, ...args) => {
    if (milliseconds === 5000 && typeof callback === 'function') callbacks.push(callback as () => void)
    return originalTimeout(callback, milliseconds, ...args)
  })
  const view = await mount(async input => json(bodies[String(input).split('/api/ai-trader/')[1].split('?')[0]]), 'Performance')
  try {
    await settle()
    assert.equal(view.state.resources.performance.data?.totalValue, '108000')
    assert.ok(callbacks.length > 0)
    now += 5001
    await act(async () => callbacks.forEach(callback => callback()))
    assert.equal(view.state.resources.performance.data, null)
    assert.equal(view.state.resources.performance.status, 'unauthorized')
  } finally { await view.close() }
})
