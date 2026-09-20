import assert from 'node:assert/strict'
import test from 'node:test'

import {
  getTrendClass,
  getTrendIcon,
  getTrendTone,
} from '../src/components/trend/trend.ts'

test('maps numeric changes to the shared trend tone and direction', () => {
  const cases = [
    { value: 5, tone: 'positive', icon: 'trending-up', className: 'metric-change-positive' },
    { value: -5, tone: 'negative', icon: 'trending-down', className: 'metric-change-negative' },
    { value: 0, tone: 'neutral', icon: null, className: 'metric-change-neutral' },
    { value: null, tone: 'neutral', icon: null, className: 'metric-change-neutral' },
  ] as const

  for (const expected of cases) {
    const tone = getTrendTone(expected.value)

    assert.equal(tone, expected.tone)
    assert.equal(getTrendIcon(tone), expected.icon)
    assert.equal(getTrendClass(tone), expected.className)
  }
})

test('a downward AI Trader label keeps the semantic negative tone', () => {
  const formattedChange = '↓ -5.0%'
  const tone = getTrendTone(-5)

  assert.equal(formattedChange.startsWith('-'), false)
  assert.equal(tone, 'negative')
  assert.equal(getTrendIcon(tone), 'trending-down')
  assert.equal(getTrendClass(tone), 'metric-change-negative')
})
