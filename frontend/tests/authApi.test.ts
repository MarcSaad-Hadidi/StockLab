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
    user: { id: '1b7f6c30-8c4f-4fe8-9350-2f6be1f9c2c0', displayName: 'Alex Johnson', email: 'alex@example.com' },
  }))

  assert.deepEqual(await api.login({ email: 'alex@example.com', password: 'password' }), {
    accessToken: 'signed-token',
    tokenType: 'Bearer',
    expiresAtUtc: '2026-09-27T13:00:00Z',
    user: { id: '1b7f6c30-8c4f-4fe8-9350-2f6be1f9c2c0', displayName: 'Alex Johnson', email: 'alex@example.com' },
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
