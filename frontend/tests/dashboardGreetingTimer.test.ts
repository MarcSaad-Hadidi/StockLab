import test from 'node:test'
import assert from 'node:assert/strict'

import { getTimeBasedGreeting } from '../src/dashboard/dashboardGreeting.ts'
import { startDashboardGreetingTimer } from '../src/dashboard/dashboardGreetingTimer.ts'

function localDate(hour: number, minute: number, second = 0, millisecond = 0) {
  return new Date(2026, 5, 1, hour, minute, second, millisecond)
}

test('refreshes a stale mount greeting when the effect starts across each period boundary', () => {
  const cases = [
    {
      beforeBoundary: localDate(4, 59, 59, 999),
      effectStart: localDate(5, 0),
      expected: 'Good morning, Ghaith',
      expectedDelay: 7 * 60 * 60 * 1000,
    },
    {
      beforeBoundary: localDate(11, 59, 59, 999),
      effectStart: localDate(12, 0),
      expected: 'Good afternoon, Ghaith',
      expectedDelay: 6 * 60 * 60 * 1000,
    },
    {
      beforeBoundary: localDate(17, 59, 59, 999),
      effectStart: localDate(18, 0),
      expected: 'Good evening, Ghaith',
      expectedDelay: 11 * 60 * 60 * 1000,
    },
  ]

  for (const testCase of cases) {
    let greeting = getTimeBasedGreeting(testCase.beforeBoundary, 'Ghaith')
    const scheduled: Array<{ callback: () => void, delay: number }> = []
    const clearedTimeouts: number[] = []

    const stop = startDashboardGreetingTimer(
      (nextGreeting) => { greeting = nextGreeting },
      'Ghaith',
      {
        now: () => testCase.effectStart,
        setTimeoutFn: (callback, delay) => {
          scheduled.push({ callback, delay })
          return scheduled.length
        },
        clearTimeoutFn: (timeoutId) => { clearedTimeouts.push(timeoutId) },
      },
    )

    assert.equal(greeting, testCase.expected)
    assert.equal(scheduled.length, 1)
    assert.equal(scheduled[0].delay, testCase.expectedDelay)

    stop()
    assert.deepEqual(clearedTimeouts, [1])
  }
})
