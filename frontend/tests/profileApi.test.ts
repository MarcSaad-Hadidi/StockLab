import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createProfileApi, ProfileApiError } from '../src/api/profileApi.ts'

const profile = {
  id: 'd4fd8828-1a30-46f7-86c0-8cbbfa644f66', displayName: 'Samira Martin', email: 'samira@example.com',
  createdAtUtc: '2026-09-24T23:40:00Z', updatedAtUtc: '2026-09-25T00:15:00Z',
}
const authorization = () => ({ Authorization: 'Bearer test-token' })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })

test('profile reads and updates use authenticated API requests and the server response', async () => {
  const calls: { url: string; init?: RequestInit }[] = []
  const api = createProfileApi('https://stocklab.test/', async (url, init) => {
    calls.push({ url: String(url), init }); return json(profile)
  }, authorization)
  assert.deepEqual(await api.getProfile(), profile)
  assert.deepEqual(await api.updateProfile({ displayName: ' Samira Martin ', email: ' samira@example.com ' }), profile)
  assert.equal(calls[0].url, 'https://stocklab.test/api/profile')
  assert.equal(calls[1].init?.method, 'PUT')
  assert.equal(new Headers(calls[1].init?.headers).get('Authorization'), 'Bearer test-token')
  assert.deepEqual(JSON.parse(String(calls[1].init?.body)), { displayName: 'Samira Martin', email: 'samira@example.com' })
})

test('missing authentication never sends a profile request', async () => {
  let calls = 0
  const api = createProfileApi('', async () => { calls++; return json(profile) }, () => null)
  await assert.rejects(api.getProfile(), (error: unknown) => error instanceof ProfileApiError && error.code === 'unauthorized')
  assert.equal(calls, 0)
})

test('profile conflicts and field validations are preserved without unsafe server messages', async () => {
  for (const [status, error] of [[409, 'email_already_registered'], [409, 'profile_update_conflict'], [400, 'validation_error'], [401, 'unauthorized'], [404, 'profile_not_found']] as const) {
    const api = createProfileApi('', async () => json({ error, message: 'private database details', errors: { Email: ['Invalid email'] } }, status), authorization)
    await assert.rejects(api.updateProfile({ displayName: 'Name', email: 'test@example.com' }), (failure: unknown) => {
      assert.ok(failure instanceof ProfileApiError)
      assert.equal(failure.code, error)
      assert.doesNotMatch(failure.message, /private database/)
      if (status === 400) assert.deepEqual(failure.fieldErrors, { Email: ['Invalid email'] })
      return true
    })
  }
})

test('malformed profiles and timestamps without UTC offset cannot become account information', async () => {
  for (const body of [{ ...profile, displayName: '' }, { ...profile, createdAtUtc: 'bad' }, { ...profile, createdAtUtc: '2026-09-24T23:40:00' }, { ...profile, id: 'not-a-guid' }]) {
    const api = createProfileApi('', async () => json(body), authorization)
    await assert.rejects(api.getProfile(), (error: unknown) => error instanceof ProfileApiError && error.code === 'invalid_response')
  }
})

test('network failures and cancellation stay distinguishable', async () => {
  const api = createProfileApi('', async () => { throw new TypeError('network failed') }, authorization)
  await assert.rejects(api.getProfile(), (error: unknown) => error instanceof ProfileApiError && error.code === 'offline')
  const controller = new AbortController()
  controller.abort()
  await assert.rejects(api.getProfile(controller.signal), (error: unknown) => error instanceof DOMException && error.name === 'AbortError')
})
