import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createAiTraderApi, AiTraderApiError } from '../src/api/aiTraderApi.ts'
import { bodies, id, secondId, overview, position, trade, decision, rejection, performance, equity, model, backtest } from './fixtures/aiTrader.ts'

const auth = () => ({ Authorization: 'Bearer test-token' })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
const invalid = (error: unknown) => error instanceof AiTraderApiError && error.code === 'invalid_response'

test('all eleven reads use exact routes, bearer authorization, cancellation and validated DTOs', async () => {
  const urls: string[] = []
  const controller = new AbortController()
  const api = createAiTraderApi('https://api.example.test/', async (input, init) => {
    urls.push(String(input))
    assert.deepEqual(init?.headers, { Accept: 'application/json', Authorization: 'Bearer test-token' })
    assert.equal(init?.signal, controller.signal)
    const path = String(input).split('/api/ai-trader/')[1].split('?')[0]
    return json(path.startsWith('trades/') ? trade : path.startsWith('decisions/') ? decision : bodies[path])
  }, auth)
  const signal = controller.signal
  assert.deepEqual(await api.getOverview(signal), overview)
  assert.deepEqual(await api.getPositions(signal), [position])
  assert.deepEqual(await api.getTrades(200, signal), [trade])
  assert.deepEqual(await api.getTrade(secondId, signal), trade)
  assert.deepEqual(await api.getDecisions(20, signal), [decision])
  assert.deepEqual(await api.getDecision(id, signal), decision)
  assert.deepEqual(await api.getRejectedDecisions(20, signal), [rejection])
  assert.deepEqual(await api.getPerformance(signal), performance)
  assert.deepEqual(await api.getEquityHistory({ limit: 200, startDateUtc: '2026-09-01T00:00:00Z', endDateUtc: '2026-10-20T23:59:59Z' }, signal), [equity])
  assert.deepEqual(await api.getModels(signal), [model])
  assert.deepEqual(await api.getBacktests(signal), [backtest])
  assert.deepEqual(urls.map(url => url.split('/api/ai-trader/')[1]), ['overview', 'positions', 'trades?limit=200', `trades/${secondId}`, 'decisions?limit=20', `decisions/${id}`, 'rejected-decisions?limit=20', 'performance', 'performance/history?limit=200&startDateUtc=2026-09-01T00%3A00%3A00Z&endDateUtc=2026-10-20T23%3A59%3A59Z', 'models', 'backtests'])
})

for (const [status, code] of [[401, 'unauthorized'], [404, 'not_found'], [409, 'conflict'], [429, 'rate_limited'], [502, 'upstream_invalid'], [503, 'unavailable'], [504, 'timeout']] as const) {
  test(`HTTP ${status} stays distinct from an empty catalog`, async () => {
    const api = createAiTraderApi('', async () => json({ error: 'private_error', message: 'private path/token' }, status), auth)
    await assert.rejects(api.getModels(), (error: unknown) => error instanceof AiTraderApiError && error.status === status && error.code === code && !error.message.includes('private'))
  })
}

test('missing, expired or replaced authorization prevents network access', async () => {
  let calls = 0
  for (const authorization of [() => null, () => ({ Authorization: 'Bearer new-token' })]) {
    const api = createAiTraderApi('', async () => { calls++; return json(overview) }, authorization)
    await assert.rejects(api.getOverview(undefined, 'Bearer old-token'), (error: unknown) => error instanceof AiTraderApiError && error.code === 'unauthorized')
  }
  assert.equal(calls, 0)
})

test('network, invalid JSON, aborted requests and empty lists have different outcomes', async () => {
  await assert.rejects(createAiTraderApi('', async () => { throw new Error('network') }, auth).getModels(), (error: unknown) => error instanceof AiTraderApiError && error.code === 'offline')
  await assert.rejects(createAiTraderApi('', async () => new Response('{bad'), auth).getModels(), invalid)
  assert.deepEqual(await createAiTraderApi('', async () => json([]), auth).getModels(), [])
  const controller = new AbortController()
  controller.abort()
  await assert.rejects(createAiTraderApi('', async () => { throw new Error('abort') }, auth).getModels(controller.signal), { name: 'AbortError' })
})

for (const [name, body] of Object.entries({
  overview: { ...overview, initialCapital: 100000 }, positions: [{ ...position, quantity: 'NaN' }],
  trades: [{ ...trade, tradeId: 'not-guid' }], decisions: [{ ...decision, signal: 'APPROVED' }],
  'rejected-decisions': [{ ...rejection, rejectedAtUtc: '2026-02-30T00:00:00Z' }],
  performance: { ...performance, unvaluedLegacySellCount: -1 },
  'performance/history': [{ ...equity, valuedAtUtc: '2026-10-20' }],
  models: [{ ...model, accuracy: 72 }], backtests: [{ ...backtest, winRate: undefined }],
})) {
  test(`malformed ${name} is rejected rather than converted into empty data`, async () => {
    const api = createAiTraderApi('', async () => json(body), auth)
    const calls: Record<string, () => Promise<unknown>> = { overview: () => api.getOverview(), positions: () => api.getPositions(), trades: () => api.getTrades(), decisions: () => api.getDecisions(), 'rejected-decisions': () => api.getRejectedDecisions(), performance: () => api.getPerformance(), 'performance/history': () => api.getEquityHistory(), models: () => api.getModels(), backtests: () => api.getBacktests() }
    await assert.rejects(calls[name](), invalid)
  })
}

test('decimal strings retain all CLR decimal digits and nullable metrics stay null', async () => {
  const body = { ...performance, totalValue: '108000.00000000000000000000001', averageWin: null, averageLoss: null, winRate: null, maxDrawdown: null, bestTrade: null, worstTrade: null }
  assert.deepEqual(await createAiTraderApi('', async () => json(body), auth).getPerformance(), body)
})

test('invalid query bounds, limits and GUIDs fail before fetch', async () => {
  let calls = 0
  const api = createAiTraderApi('', async () => { calls++; return json([]) }, auth)
  for (const limit of [0, 201, 1.5, NaN]) await assert.rejects(api.getTrades(limit))
  await assert.rejects(api.getTrade('bad'))
  await assert.rejects(api.getEquityHistory({ startDateUtc: '2026-10-21T00:00:00Z', endDateUtc: '2026-10-20T00:00:00Z' }))
  await assert.rejects(api.getEquityHistory({ startDateUtc: '2026-02-30T00:00:00Z' }))
  assert.equal(calls, 0)
})

for (const [name, body] of Object.entries({
  numericMoney: { ...overview, totalValue: 108000 }, exponentMoney: { ...overview, totalValue: '1e5' },
  decimalOverflow: { ...overview, totalValue: '79228162514264337593543950336' }, negativeCount: { ...overview, openPositionsCount: -1 },
  missingCoverage: { ...overview, historicalCoverage: null }, invalidCoverage: { ...overview, historicalCoverage: { ...overview.historicalCoverage, status: 'Complete' } },
  invalidDate: { ...overview, valuationTimestampUtc: '2026-02-30T00:00:00Z' }, missingUtc: { ...overview, valuationTimestampUtc: '2026-10-20T12:00:00' },
  offsetTimestamp: { ...overview, valuationTimestampUtc: '2026-10-20T12:00:00+02:00' }, invalidHour: { ...overview, valuationTimestampUtc: '2026-10-20T24:00:00Z' },
  confidenceAsPoints: { ...overview, winRate: 65 }, infinity: { ...overview, totalReturn: Infinity },
})) test(`overview rejects ${name}`, async () => { await assert.rejects(createAiTraderApi('', async () => json(body), auth).getOverview(), invalid) })

test('all list resources accept genuine empty arrays and all supported outcome enums', async () => {
  const api = createAiTraderApi('', async () => json([]), auth)
  for (const load of [() => api.getPositions(), () => api.getTrades(), () => api.getDecisions(), () => api.getRejectedDecisions(), () => api.getEquityHistory(), () => api.getModels(), () => api.getBacktests()]) assert.deepEqual(await load(), [])
  const cases = [['BUY', 'EXECUTED'], ['BUY', 'REJECTED'], ['SELL', 'EXECUTED'], ['HOLD', 'HOLD'], ['BUY', 'UNRESOLVED']]
  for (const [signal, executionStatus] of cases) {
    const result = await createAiTraderApi('', async () => json([{ ...decision, signal, executionStatus }]), auth).getDecisions()
    assert.equal(result[0].signal, signal); assert.equal(result[0].executionStatus, executionStatus)
  }
})
