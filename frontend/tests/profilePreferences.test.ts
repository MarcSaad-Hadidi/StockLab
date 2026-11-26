import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createPreferencesApi, PreferencesApiError } from '../src/api/preferencesApi.ts'

const preferences = { userId: 'user-a', emailNotifications: false, tradeAlerts: false, marketingCommunications: false, darkMode: false }
const auth = 'Bearer signed-test'

test('loads uncached account preferences with the explicit session token', async () => {
  const api = createPreferencesApi('https://example.test/', async (url, options) => {
    assert.equal(url, 'https://example.test/api/profile/preferences')
    assert.equal(options?.method, 'GET')
    assert.equal(options?.cache, 'no-store')
    assert.equal(new Headers(options?.headers).get('Authorization'), auth)
    return Response.json(preferences)
  })
  assert.deepEqual(await api.get(auth, 'user-a'), preferences)
  await assert.rejects(api.get(auth, 'user-b'), PreferencesApiError)
})

test('sends a partial update, preserves false values, and requires a matching response', async () => {
  const api = createPreferencesApi('', async (url, options) => {
    assert.equal(url, '/api/profile/preferences')
    assert.equal(options?.method, 'PATCH')
    assert.deepEqual(JSON.parse(String(options?.body)), { tradeAlerts: false })
    return Response.json(preferences)
  })
  assert.deepEqual(await api.update(auth, 'user-a', { tradeAlerts: false }), preferences)
  const bad = createPreferencesApi('', async () => Response.json({ ...preferences, darkMode: 'false' }))
  await assert.rejects(bad.get(auth, 'user-a'), PreferencesApiError)
})
