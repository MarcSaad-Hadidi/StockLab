import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createProfileApi, ProfileApiError } from '../src/api/profileApi.ts'

const profile = {
  id: 'd4fd8828-1a30-46f7-86c0-8cbbfa644f66', displayName: 'Samira Martin', email: 'samira@example.com',
  createdAtUtc: '2026-09-24T23:40:00Z', updatedAtUtc: '2026-09-25T00:15:00Z',
  preferredLanguage: 'en',
}
const authorization = () => ({ Authorization: 'Bearer test-token' })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { ETag: '"AQIDBAUGBwg="' } })

test('language updates persist only the preference through an authenticated profile endpoint', async () => {
  let captured: { url: string; init?: RequestInit } | undefined
  const api = createProfileApi('', async (url, init) => {
    captured = { url: String(url), init }
    return json({ ...profile, preferredLanguage: 'fr' })
  }, authorization)
  const saved = await api.updateLanguage('fr')
  assert.equal(saved.preferredLanguage, 'fr')
  assert.equal(captured?.url, '/api/profile/language')
  assert.equal(captured?.init?.method, 'PUT')
  assert.equal(captured?.init?.keepalive, true, 'the small preference write survives ordinary page navigation')
  assert.equal(new Headers(captured?.init?.headers).get('Authorization'), 'Bearer test-token')
  assert.deepEqual(JSON.parse(String(captured?.init?.body)), { preferredLanguage: 'fr' })
})

test('an explicit login preference uses the newly authenticated token before a browser session is stored', async () => {
  const requests: RequestInit[] = []
  const api = createProfileApi('', async (_url, init) => { requests.push(init!); return json({ ...profile, preferredLanguage: 'fr' }) }, () => null)
  const saved = await api.updateLanguage('fr', undefined, 'Bearer fresh-login-token')
  assert.equal(saved.preferredLanguage, 'fr')
  assert.deepEqual(requests.map(request => request.method), ['GET', 'PUT'])
  assert.ok(requests.every(request => new Headers(request.headers).get('Authorization') === 'Bearer fresh-login-token'))
  assert.equal(requests[0].cache, 'no-store')
  assert.equal(new Headers(requests[1].headers).get('If-Match'), '"AQIDBAUGBwg="')
  assert.deepEqual(JSON.parse(String(requests[1].body)), { preferredLanguage: 'fr' })
  await assert.rejects(api.getProfile(), (error: unknown) => error instanceof ProfileApiError && error.code === 'unauthorized')
})

test('a language write never falls back to unconditional PUT when the profile ETag is missing or malformed', async () => {
  for (const etag of [null, '""', '"AA=="', 'W/"AQIDBAUGBwg="', '*', 'unquoted', '"not base64"', '"a"', '"AA==", "AA=="']) {
    const methods: string[] = []
    const api = createProfileApi('', async (_url, init) => {
      methods.push(String(init?.method))
      return new Response(JSON.stringify(profile), { headers: etag ? { ETag: etag } : {} })
    }, authorization)
    await assert.rejects(api.updateLanguage('fr'), (error: unknown) => error instanceof ProfileApiError && error.code === 'invalid_response')
    assert.deepEqual(methods, ['GET'])
  }
})

test('conditional language writes retain their original authorization across the version read', async () => {
  let token = 'Bearer account-a'
  const calls: Array<{ method?: string; auth: string | null }> = []
  const api = createProfileApi('', async (_url, init) => {
    calls.push({ method: init?.method, auth: new Headers(init?.headers).get('Authorization') })
    token = 'Bearer account-b'
    return json({ ...profile, preferredLanguage: 'fr' })
  }, () => ({ Authorization: token }))
  await api.updateLanguage('fr')
  assert.deepEqual(calls, [{ method: 'GET', auth: 'Bearer account-a' }, { method: 'PUT', auth: 'Bearer account-a' }])
})

test('missing or unsupported server preferences cannot be accepted as saved profile data', async () => {
  for (const preferredLanguage of [undefined, null, 'es', 'FR', 'fr-FR', 1]) {
    const api = createProfileApi('', async () => json({ ...profile, preferredLanguage }), authorization)
    await assert.rejects(api.getProfile(), (error: unknown) => error instanceof ProfileApiError && error.code === 'invalid_response')
  }
})

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
