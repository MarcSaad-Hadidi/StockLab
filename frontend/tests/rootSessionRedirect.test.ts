import assert from 'node:assert/strict'
import { test } from 'node:test'
import { authStorageKey, type AuthSession } from '../src/auth/authStorage.ts'

const session: AuthSession = {
  accessToken: 'signed-token',
  tokenType: 'Bearer',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-id', displayName: 'Test Account', email: 'account@example.test' },
}

const cases = [
  { name: 'active', stored: JSON.stringify(session), expected: '/dashboard' },
  { name: 'absent', stored: null, expected: '/login' },
  { name: 'expired', stored: JSON.stringify({ ...session, expiresAtUtc: '2000-01-01T00:00:00Z' }), expected: '/login' },
  { name: 'invalid', stored: '{invalid JSON', expected: '/login' },
  { name: 'blocked', stored: null, expected: '/login' },
]

for (const { name, stored, expected } of cases) {
  test(`the root redirects directly to ${expected} when the session is ${name}`, async (t) => {
    let value = stored
    const storage = {
      getItem: (key: string) => key === authStorageKey ? value : null,
      removeItem: (key: string) => { if (key === authStorageKey) value = null },
    }
    const replace = t.mock.fn()
    const previousWindow = Object.getOwnPropertyDescriptor(globalThis, 'window')
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: {
        get localStorage() {
          if (name === 'blocked') throw new Error('Storage is blocked')
          return storage
        },
        location: { replace },
        Event,
        dispatchEvent: () => true,
      },
    })
    t.after(() => {
      if (previousWindow) Object.defineProperty(globalThis, 'window', previousWindow)
      else Reflect.deleteProperty(globalThis, 'window')
    })

    // A revisit loads the root entry in a new document.
    await import(`../src/navigation/redirectHome.ts?${name}`)

    assert.deepEqual(replace.mock.calls.map(call => call.arguments), [[expected]])
    assert.equal(value, expected === '/dashboard' ? stored : null)
  })
}
