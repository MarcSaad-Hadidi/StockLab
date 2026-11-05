import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createPortfolioApi, PortfolioApiError } from '../src/api/portfolioApi.ts'

const history = {
  range: '1M', currency: 'CAD', fromUtc: '2026-10-01T12:00:00Z', toUtc: '2026-11-01T12:00:00Z',
  sampleCount: 2, resolution: 'observation', points: [
    { recordedAtUtc: '2026-10-31T12:00:00Z', totalValue: '999999989999999.999900000001', source: 'TwelveData', quoteAsOfUtc: '2026-10-30T20:00:00Z' },
    { recordedAtUtc: '2026-11-01T12:00:00Z', totalValue: '10000.000000000001', source: 'Cash', quoteAsOfUtc: null },
  ],
  firstRecordedAtUtc: '2026-10-31T12:00:00Z', lastRecordedAtUtc: '2026-11-01T12:00:00Z',
}
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })

test('personal history requests the owned API range and preserves exact persisted totals', async () => {
  let url = '', headers: Headers | undefined
  const api = createPortfolioApi('https://stocklab.test/', async (input, init) => {
    url = String(input); headers = new Headers(init?.headers); return json(history)
  }, () => ({ Authorization: 'Bearer history-token' }))
  const result = await api.getPerformanceHistory('1M')
  assert.equal(url, 'https://stocklab.test/api/portfolio/performance/history?range=1M')
  assert.equal(headers?.get('Authorization'), 'Bearer history-token')
  assert.deepEqual(result, history)
})

test('an empty history response stays empty instead of receiving a fabricated baseline', async () => {
  const empty = { ...history, sampleCount: 0, points: [], firstRecordedAtUtc: null, lastRecordedAtUtc: null }
  const api = createPortfolioApi('', async () => json(empty), () => ({ Authorization: 'Bearer token' }))
  assert.deepEqual((await api.getPerformanceHistory('1M')).points, [])
})

test('history validates precise amounts, owned metadata and chronological UTC observations', async () => {
  const first = history.points[0]
  for (const invalid of [
    { ...history, sampleCount: 1 }, { ...history, currency: 'NOT-A-CURRENCY' },
    { ...history, points: [...history.points].reverse() },
    { ...history, points: [{ ...first, totalValue: 999999990000000 }] },
    { ...history, sampleCount: 1, points: [{ ...first, totalValue: '-1' }] },
    { ...history, sampleCount: 1, points: [{ ...first, source: 'UnknownProvider' }] },
    { ...history, sampleCount: 1, points: [{ ...first, recordedAtUtc: '2027-01-01T00:00:00Z' }] },
    { ...history, sampleCount: 1, points: [{ ...first, recordedAtUtc: '2026-09-01T00:00:00Z' }] },
    { ...history, resolution: 'invented' },
    { ...history, firstRecordedAtUtc: '2026-10-31T13:00:00Z' },
    { ...history, lastRecordedAtUtc: '2026-10-31T23:00:00Z' },
    { ...history, firstRecordedAtUtc: null },
  ]) {
    const api = createPortfolioApi('', async () => json(invalid), () => ({ Authorization: 'Bearer token' }))
    await assert.rejects(api.getPerformanceHistory('1M'), (error: unknown) =>
      error instanceof PortfolioApiError && error.code === 'invalid_response')
  }
})

test('history cannot fetch with a missing or replaced authorization snapshot', async () => {
  let calls = 0
  const api = createPortfolioApi('', async () => { calls++; return json(history) }, () => ({ Authorization: 'Bearer current' }))
  await assert.rejects(api.getPerformanceHistory('1M', undefined, 'Bearer old'), PortfolioApiError)
  const anonymous = createPortfolioApi('', async () => { calls++; return json(history) }, () => null)
  await assert.rejects(anonymous.getPerformanceHistory('1M'), PortfolioApiError)
  assert.equal(calls, 0)
})
