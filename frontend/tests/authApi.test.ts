import assert from 'node:assert/strict'
import { test } from 'node:test'
import { AuthApiError, createAuthApi } from '../src/api/authApi.ts'

function jsonResponse(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

test('register sends the backend contract and returns the created user', async () => {
  let request: { url: string; init: RequestInit } | undefined
  const api = createAuthApi('http://localhost:5274', async (url, init) => {
    request = { url, init }
    return jsonResponse({
      id: '1b7f6c30-8c4f-4fe8-9350-2f6be1f9c2c0',
      displayName: 'Alex Johnson',
      email: 'alex@example.com',
      createdAtUtc: '2026-09-27T12:00:00Z',
    }, 201)
  })

  const result = await api.register({
    displayName: ' Alex Johnson ',
    email: ' alex@example.com ',
    password: 'correct horse battery staple',
  })

  assert.deepEqual(result, {
    id: '1b7f6c30-8c4f-4fe8-9350-2f6be1f9c2c0',
    displayName: 'Alex Johnson',
    email: 'alex@example.com',
    createdAtUtc: '2026-09-27T12:00:00Z',
  })
  assert.equal(request?.url, 'http://localhost:5274/api/auth/register')
  assert.equal(request?.init.method, 'POST')
  assert.deepEqual(JSON.parse(String(request?.init.body)), {
    displayName: 'Alex Johnson',
    email: 'alex@example.com',
    password: 'correct horse battery staple',
  })
})

test('login returns a validated bearer session', async () => {
  const api = createAuthApi('', async () => jsonResponse({
    accessToken: 'signed-token',
    tokenType: 'Bearer',
    expiresAtUtc: '2026-09-27T13:00:00Z',
    user: { id: '1b7f6c30-8c4f-4fe8-9350-2f6be1f9c2c0', displayName: 'Alex Johnson', email: 'alex@example.com', preferredLanguage: 'fr' },
  }))

  assert.deepEqual(await api.login({ email: 'alex@example.com', password: 'password' }), {
    accessToken: 'signed-token',
    tokenType: 'Bearer',
    expiresAtUtc: '2026-09-27T13:00:00Z',
    user: { id: '1b7f6c30-8c4f-4fe8-9350-2f6be1f9c2c0', displayName: 'Alex Johnson', email: 'alex@example.com', preferredLanguage: 'fr' },
  })
})

test('maps safe backend errors without exposing technical response details', async () => {
  const api = createAuthApi('', async () => jsonResponse({
    error: 'email_already_registered',
    message: 'An account already exists for this email.',
    internal: 'sql details must not reach the UI',
  }, 409))

  await assert.rejects(
    api.register({ displayName: 'Alex', email: 'alex@example.com', password: 'password' }),
    (error: unknown) => error instanceof AuthApiError
      && error.status === 409
      && error.code === 'email_already_registered'
      && error.message === 'An account already exists for this email.'
      && !error.message.includes('sql'),
  )
})

test('maps invalid credentials, rate limits, and offline requests to stable codes', async () => {
  const statuses = [
    [401, 'invalid_credentials'],
    [429, 'rate_limited'],
  ] as const
  for (const [status, code] of statuses) {
    const api = createAuthApi('', async () => jsonResponse({}, status))
    await assert.rejects(api.login({ email: 'alex@example.com', password: 'wrong' }),
      (error: unknown) => error instanceof AuthApiError && error.status === status && error.code === code)
  }

  const offline = createAuthApi('', async () => { throw new Error('network internals') })
  await assert.rejects(offline.login({ email: 'alex@example.com', password: 'password' }),
    (error: unknown) => error instanceof AuthApiError && error.status === 0 && error.code === 'offline')
})

test('rejects malformed successful responses', async () => {
  const api = createAuthApi('', async () => jsonResponse({ accessToken: '', tokenType: 'Bearer' }))
  await assert.rejects(api.login({ email: 'alex@example.com', password: 'password' }),
    (error: unknown) => error instanceof AuthApiError && error.code === 'invalid_response')
})

for (const [header, expected] of [['12', 12], ['0', 0], ['86400', 86400]] as const) {
  test(`429 exposes Retry-After ${header} ahead of the JSON delay`, async () => {
    const api = createAuthApi('', async () => new Response(JSON.stringify({ error: 'invalid_credentials', retryAfterSeconds: 99 }),
      { status: 429, headers: { 'Retry-After': header } }))
    await assert.rejects(api.login({ email: 'alex@example.com', password: 'password' }),
      (error: unknown) => error instanceof AuthApiError && error.code === 'rate_limited' && error.retryAfterSeconds === expected)
  })
}

test('HTTP-date Retry-After uses the server Date header despite browser clock skew', async t => {
  t.mock.method(Date, 'now', () => Date.parse('2030-01-01T00:00:00Z'))
  const api = createAuthApi('', async () => new Response('', { status: 429, headers: {
    Date: 'Wed, 11 Nov 2026 12:00:00 GMT', 'Retry-After': 'Wed, 11 Nov 2026 12:00:25 GMT',
  } }))
  await assert.rejects(api.login({ email: 'alex@example.com', password: 'password' }),
    (error: unknown) => error instanceof AuthApiError && error.retryAfterSeconds === 25)
})

test('HTTP-date without server Date rounds up from the browser clock, and expired dates allow immediate retry', async t => {
  t.mock.method(Date, 'now', () => Date.parse('2026-11-11T12:00:00.250Z'))
  for (const [date, expected] of [['Wed, 11 Nov 2026 12:00:02 GMT', 2], ['Wed, 11 Nov 2026 11:59:59 GMT', 0]] as const) {
    const api = createAuthApi('', async () => new Response('', { status: 429, headers: { 'Retry-After': date } }))
    await assert.rejects(api.login({ email: 'alex@example.com', password: 'password' }),
      (error: unknown) => error instanceof AuthApiError && error.retryAfterSeconds === expected)
  }
})

for (const header of ['', 'invalid', '-1', '1.5', 'Infinity', '86401']) {
  test(`missing or invalid Retry-After ${JSON.stringify(header)} falls back to the JSON delay`, async () => {
    const api = createAuthApi('', async () => new Response(JSON.stringify({ retryAfterSeconds: 17 }),
      { status: 429, headers: { 'Retry-After': header } }))
    await assert.rejects(api.login({ email: 'alex@example.com', password: 'password' }),
      (error: unknown) => error instanceof AuthApiError && error.retryAfterSeconds === 17)
  })
}

for (const delay of [undefined, null, -1, 1.5, '30', 86401]) {
  test(`invalid JSON retry delay ${JSON.stringify(delay)} remains unavailable`, async () => {
    const api = createAuthApi('', async () => jsonResponse({ retryAfterSeconds: delay }, 429))
    await assert.rejects(api.login({ email: 'alex@example.com', password: 'password' }),
      (error: unknown) => error instanceof AuthApiError && error.retryAfterSeconds === null)
  })
}

test('other authentication errors do not start a rate-limit cooldown', async () => {
  const api = createAuthApi('', async () => new Response(JSON.stringify({ retryAfterSeconds: 60 }),
    { status: 401, headers: { 'Retry-After': '60' } }))
  await assert.rejects(api.login({ email: 'alex@example.com', password: 'password' }),
    (error: unknown) => error instanceof AuthApiError && error.code === 'invalid_credentials' && error.retryAfterSeconds === null)
})
