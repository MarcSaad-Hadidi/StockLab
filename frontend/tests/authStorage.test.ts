import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  authStorageKey,
  clearAuthSession,
  getAuthSession,
  getAuthorizationHeader,
  saveAuthSession,
} from '../src/auth/authStorage.ts'

function createStorage() {
  const values = new Map<string, string>()
  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value) },
    removeItem: (key: string) => { values.delete(key) },
  }
}

const session = {
  accessToken: 'signed-token',
  tokenType: 'Bearer',
  expiresAtUtc: '2099-09-27T13:00:00Z',
  user: { id: '1b7f6c30-8c4f-4fe8-9350-2f6be1f9c2c0', displayName: 'Alex Johnson', email: 'alex@example.com' },
}

test('stores and reads the authenticated session and bearer header', () => {
  const storage = createStorage()
  assert.equal(saveAuthSession(session, storage), true)
  assert.equal(authStorageKey, 'stocklab-auth')
  assert.deepEqual(getAuthSession(storage), session)
  assert.deepEqual(getAuthorizationHeader(storage), { Authorization: 'Bearer signed-token' })
})

test('clears malformed or expired sessions before they can be used', () => {
  const storage = createStorage()
  storage.setItem(authStorageKey, '{"accessToken":"token"}')
  assert.equal(getAuthSession(storage), null)
  assert.equal(storage.getItem(authStorageKey), null)

  assert.equal(saveAuthSession({ ...session, expiresAtUtc: '2000-01-01T00:00:00Z' }, storage), true)
  assert.equal(getAuthSession(storage), null)
  assert.equal(getAuthorizationHeader(storage), null)
})

test('clearAuthSession removes an existing session', () => {
  const storage = createStorage()
  assert.equal(saveAuthSession(session, storage), true)
  clearAuthSession(storage)
  assert.equal(getAuthSession(storage), null)
})

test('reports when browser storage is blocked instead of claiming a saved session', () => {
  const blocked = {
    getItem: () => null,
    setItem: () => { throw new Error('storage blocked') },
    removeItem: () => undefined,
  }
  assert.equal(saveAuthSession(session, blocked), false)
})
