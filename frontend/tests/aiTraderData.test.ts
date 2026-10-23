import assert from 'node:assert/strict'
import { test } from 'node:test'
import { fractionPoints, positionPnlPercent, equityInRange, formatAiTimestamp } from '../src/ai-trader/aiTraderData.ts'
import { formatCurrency, formatSignedCurrency, formatPercent, formatSignedPercent } from '../src/i18n/formatters.ts'
import { position, equity } from './fixtures/aiTrader.ts'

test('API fractions convert exactly once, preserving null, zero and negative returns', () => {
  for (const [fraction, expected] of [[.12, '12%'], [.65, '65%'], [.20, '20%'], [0, '0%'], [-.12, '-12%'], [null, '—']] as const) assert.equal(formatPercent(fractionPoints(fraction), 'en', 0), expected)
  assert.equal(formatSignedPercent(fractionPoints(.08), 'en', 0), '+8%')
})
test('position return uses exact quantity times cost and a zero denominator stays unavailable', () => {
  assert.equal(positionPnlPercent(position), 10)
  assert.equal(positionPnlPercent({ ...position, pnL: '-1.2345678' }), -10)
  assert.equal(positionPnlPercent({ ...position, pnL: '0' }), 0)
  assert.equal(positionPnlPercent({ ...position, averagePrice: '0' }), null)
  assert.equal(positionPnlPercent({ ...position, averagePrice: '0.00000000000000000001', quantity: '0.00000001', pnL: '0.0000000000000000000000000001' }), 100)
})
test('financial display keeps decimal digits beyond Number precision and twelve decimals', () => {
  assert.equal(formatCurrency('108000.00000000000000000000001', 'en', 2, 'USD', true), '$108,000.00000000000000000000001')
  assert.equal(formatSignedCurrency('-0.0000000000000000000000000001', 'en', 2, 'USD', true), '-$0.0000000000000000000000000001')
})
test('UTC ranges use calendar months with clamped days and preserve only genuine observations', () => {
  const rows = ['2025-10-20', '2026-04-20', '2026-07-20', '2026-09-20', '2026-10-20'].map(day => ({ ...equity, valuedAtUtc: `${day}T00:00:00Z` }))
  const now = new Date('2026-10-20T00:00:00Z')
  assert.equal(equityInRange(rows, '1M', now).length, 2)
  assert.equal(equityInRange(rows, '3M', now).length, 3)
  assert.equal(equityInRange(rows, '6M', now).length, 4)
  assert.equal(equityInRange(rows, '1Y', now).length, 5)
  assert.deepEqual(equityInRange(rows, 'ALL', now), rows)
  assert.deepEqual(equityInRange([], 'ALL', now), [])
  const february = [{ ...equity, valuedAtUtc: '2026-02-28T00:00:00Z' }]
  assert.deepEqual(equityInRange(february, '1M', new Date('2026-03-31T00:00:00Z')), february)
  assert.match(formatAiTimestamp('2026-10-20T15:30:00Z', 'en'), /2026.*15:30|2026.*3:30/)
})
