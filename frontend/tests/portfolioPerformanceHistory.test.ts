import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import React, { act } from 'react'
import { JSDOM } from 'jsdom'
import { createRoot } from 'react-dom/client'
import { portfolioApi, PortfolioApiError, type PortfolioPerformanceHistory, type PortfolioPerformanceRange } from '../src/api/portfolioApi.ts'
import { saveAuthSession, clearAuthSession } from '../src/auth/authStorage.ts'
import { usePortfolioPerformanceHistory, type PortfolioPerformanceHistoryState } from '../src/portfolio/usePortfolioPerformanceHistory.ts'

const dom = new JSDOM('', { url: 'http://localhost/portfolio' })
Object.assign(globalThis, { window: dom.window, document: dom.window.document,
  HTMLElement: dom.window.HTMLElement, IS_REACT_ACT_ENVIRONMENT: true })
const session = (id: string, token = id) => ({ accessToken: token, tokenType: 'Bearer' as const,
  expiresAtUtc: '2099-01-01T00:00:00Z', user: { id, displayName: id, email: `${id}@example.com` } })
const history = (range: PortfolioPerformanceRange = '1M', amount = '99999.999999999999'): PortfolioPerformanceHistory => ({
  range, currency: 'CAD', fromUtc: range === 'ALL' ? null : '2026-10-01T12:00:00Z', toUtc: '2026-11-01T12:00:00Z',
  sampleCount: 2, resolution: 'observation', points: [
    { recordedAtUtc: '2026-10-31T12:00:00Z', totalValue: amount, source: 'Cash', quoteAsOfUtc: null },
    { recordedAtUtc: '2026-11-01T12:00:00Z', totalValue: '10000.000000000001', source: 'Cash', quoteAsOfUtc: null },
  ],
  firstRecordedAtUtc: '2026-10-31T12:00:00Z', lastRecordedAtUtc: '2026-11-01T12:00:00Z',
})
const deferred = <T,>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}
beforeEach(() => { window.localStorage.clear(); saveAuthSession(session('owner-a')) })

async function mount() {
  let state!: PortfolioPerformanceHistoryState
  function Probe({ range }: { range: PortfolioPerformanceRange }) { state = usePortfolioPerformanceHistory(range); return null }
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  async function render(range: PortfolioPerformanceRange) { await act(async () => root.render(React.createElement(Probe, { range }))) }
  await render('1M')
  return { get state() { return state }, render, async close() { await act(async () => root.unmount()); container.remove() } }
}

test('personal history loads exact persisted data independently of current valuation', async t => {
  t.mock.method(portfolioApi, 'getPortfolio', async () => { throw new Error('Current valuation must not be requested.') })
  const calls: string[] = []
  t.mock.method(portfolioApi, 'getPerformanceHistory', async (range, _signal, authorization) => {
    calls.push(`${range}:${authorization}`); return history(range)
  })
  const view = await mount()
  try {
    assert.deepEqual(calls, ['1M:Bearer owner-a'])
    assert.equal(view.state.data?.points[0].totalValue, '99999.999999999999')
    assert.equal(view.state.error, null); assert.equal(view.state.isLoading, false)
  } finally { await view.close() }
})

test('changing range aborts old history and ignores late results', async t => {
  const old = deferred<PortfolioPerformanceHistory>(), next = deferred<PortfolioPerformanceHistory>()
  let oldSignal: AbortSignal | undefined
  t.mock.method(portfolioApi, 'getPerformanceHistory', (range, signal) => {
    if (range === '1M') { oldSignal = signal; return old.promise }
    return next.promise
  })
  const view = await mount()
  try {
    assert.equal(view.state.isLoading, true)
    await view.render('1D'); assert.equal(oldSignal?.aborted, true); assert.equal(view.state.data, null)
    await act(async () => next.resolve(history('1D')))
    await act(async () => old.resolve(history('1M', '111111')))
    assert.equal(view.state.data?.range, '1D')
    assert.equal(view.state.data?.points[0].totalValue, '99999.999999999999')
  } finally { await view.close() }
})

for (const replacement of [session('owner-b'), session('owner-a', 'rotated-token')]) {
  test(`history cannot leak a previous account/token after ${replacement.accessToken}`, async t => {
    const old = deferred<PortfolioPerformanceHistory>(), next = deferred<PortfolioPerformanceHistory>()
    let calls = 0, oldSignal: AbortSignal | undefined
    t.mock.method(portfolioApi, 'getPerformanceHistory', (_range, signal) => {
      calls++; if (calls === 1) { oldSignal = signal; return old.promise } return next.promise
    })
    const view = await mount()
    try {
      await act(async () => saveAuthSession(replacement))
      assert.equal(oldSignal?.aborted, true); assert.equal(view.state.data, null)
      await act(async () => next.resolve(history('1M', '222222')))
      await act(async () => old.resolve(history('1M', '111111')))
      assert.equal(view.state.data?.points[0].totalValue, '222222')
      await act(async () => clearAuthSession())
      assert.equal(view.state.data, null); assert.equal(view.state.error?.code, 'unauthorized')
    } finally { await view.close() }
  })
}

test('a failed history load is an error and retry can recover without inventing data', async t => {
  let calls = 0
  t.mock.method(portfolioApi, 'getPerformanceHistory', async () => {
    if (++calls === 1) throw new PortfolioApiError(503, 'server_error', 'unavailable')
    return { ...history(), sampleCount: 0, points: [] }
  })
  const view = await mount()
  try {
    assert.equal(view.state.data, null); assert.equal(view.state.error?.code, 'server_error')
    await act(async () => view.state.retry())
    assert.deepEqual(view.state.data?.points, []); assert.equal(view.state.error, null)
  } finally { await view.close() }
})
