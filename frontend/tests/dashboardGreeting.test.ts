import test from 'node:test'
import assert from 'node:assert/strict'

import {
  getMillisecondsUntilNextGreetingChange,
  getTimeBasedGreeting,
} from '../src/dashboard/dashboardGreeting.ts'

function localDate(hour: number, minute: number, second = 0, millisecond = 0) {
  return new Date(2026, 5, 1, hour, minute, second, millisecond)
}

test('returns the named greeting for morning, afternoon, and evening', () => {
  assert.equal(getTimeBasedGreeting(localDate(5, 0), 'Ghaith'), 'Good morning, Ghaith')
  assert.equal(getTimeBasedGreeting(localDate(12, 0), 'Ghaith'), 'Good afternoon, Ghaith')
  assert.equal(getTimeBasedGreeting(localDate(18, 0), 'Ghaith'), 'Good evening, Ghaith')
})

test('uses evening overnight and falls back when the name is unavailable', () => {
  assert.equal(getTimeBasedGreeting(localDate(4, 59)), 'Good evening')
  assert.equal(getTimeBasedGreeting(localDate(10, 30), '  '), 'Good morning')
})

test('changes periods exactly at 05:00, 12:00, and 18:00', () => {
  assert.equal(getTimeBasedGreeting(localDate(4, 59)), 'Good evening')
  assert.equal(getTimeBasedGreeting(localDate(5, 0)), 'Good morning')
  assert.equal(getTimeBasedGreeting(localDate(11, 59)), 'Good morning')
  assert.equal(getTimeBasedGreeting(localDate(12, 0)), 'Good afternoon')
  assert.equal(getTimeBasedGreeting(localDate(17, 59)), 'Good afternoon')
  assert.equal(getTimeBasedGreeting(localDate(18, 0)), 'Good evening')
})

test('schedules the next update at the next period boundary', () => {
  assert.equal(getMillisecondsUntilNextGreetingChange(localDate(11, 59, 59, 999)), 1)
  assert.equal(getMillisecondsUntilNextGreetingChange(localDate(17, 59, 59, 999)), 1)
  assert.equal(getMillisecondsUntilNextGreetingChange(localDate(23, 0)), 6 * 60 * 60 * 1000)
})
