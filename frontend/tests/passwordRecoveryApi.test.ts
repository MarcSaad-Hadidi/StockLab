import assert from 'node:assert/strict'
import { test } from 'node:test'

const recovery = await import('../src/api/passwordRecoveryApi.ts')
const payload = { userId: 'd4fd8828-1a30-46f7-86c0-8cbbfa644f66', token: 'ab'.repeat(32), newPassword: 'new-password', confirmPassword: 'new-password' }

test('recovery API sends anonymous POSTs and accepts only the documented empty success statuses', async () => {
  assert.ok(recovery, 'the password recovery API must exist')
  const requests: { url: string; init?: RequestInit }[] = []
  const api = recovery.createPasswordRecoveryApi('https://api.example/', async (url, init) => {
    requests.push({ url: String(url), init })
    return new Response(null, { status: String(url).endsWith('/forgot') ? 202 : 204 })
  })
  await api.forgotPassword({ email: ' person@example.com ', language: 'fr' })
  await api.resetPassword(payload)
  assert.deepEqual(requests.map(request => request.url), ['https://api.example/api/auth/password/forgot', 'https://api.example/api/auth/password/reset'])
  assert.deepEqual(JSON.parse(String(requests[0].init?.body)), { email: 'person@example.com', language: 'fr' })
  assert.deepEqual(JSON.parse(String(requests[1].init?.body)), payload)
  for (const request of requests) {
    assert.equal(request.init?.method, 'POST')
    assert.equal(new Headers(request.init?.headers).has('Authorization'), false)
    assert.equal(request.init?.referrerPolicy, 'no-referrer')
  }
})

for (const method of ['forgotPassword', 'resetPassword'] as const) {
  for (const status of [200, 201, method === 'forgotPassword' ? 204 : 202]) {
    test(`${method} rejects misleading success status ${status}`, async () => {
      assert.ok(recovery, 'the password recovery API must exist')
      const api = recovery.createPasswordRecoveryApi('', async () => new Response(null, { status }))
      await assert.rejects(method === 'forgotPassword' ? api[method]({ email: 'person@example.com', language: 'en' }) : api[method](payload),
        error => error instanceof recovery.PasswordRecoveryApiError && error.code === 'invalid_response')
    })
  }
}

for (const [status, body, code] of [
  [400, { error: 'invalid_reset_token' }, 'invalid_reset_token'],
  [400, { error: 'validation_error', errors: { NewPassword: ['private password detail'], ConfirmPassword: ['private mismatch'] } }, 'validation_error'],
  [409, { error: 'password_update_conflict' }, 'password_update_conflict'],
  [429, { error: 'too_many_requests', retryAfterSeconds: 17 }, 'too_many_requests'],
  [503, { error: 'password_recovery_unavailable' }, 'password_recovery_unavailable'],
  [500, { error: 'internal_server_error', message: 'private exception' }, 'internal_server_error'],
  [418, { error: 'private exception' }, 'invalid_response'],
] as const) {
  test(`recovery API maps ${status} to safe ${code}`, async () => {
    assert.ok(recovery, 'the password recovery API must exist')
    const api = recovery.createPasswordRecoveryApi('', async () => new Response(JSON.stringify(body), { status }))
    await assert.rejects(api.resetPassword(payload), error => {
      assert.ok(error instanceof recovery.PasswordRecoveryApiError)
      assert.equal(error.code, code)
      assert.ok(!JSON.stringify(error).includes('private'))
      if (code === 'validation_error') assert.deepEqual(error.fields, ['newpassword', 'confirmpassword'])
      if (code === 'too_many_requests') assert.equal(error.retryAfterSeconds, 17)
      return true
    })
  })
}

test('rate-limit headers take priority and offline errors never expose network details', async () => {
  assert.ok(recovery, 'the password recovery API must exist')
  const api = recovery.createPasswordRecoveryApi('', async () => new Response(JSON.stringify({ retryAfterSeconds: 7 }), { status: 429, headers: { 'Retry-After': '12' } }))
  await assert.rejects(api.resetPassword(payload), error => error instanceof recovery.PasswordRecoveryApiError && error.retryAfterSeconds === 12)
  const offline = recovery.createPasswordRecoveryApi('', async () => { throw new Error('private network detail') })
  await assert.rejects(offline.resetPassword(payload), error => error instanceof recovery.PasswordRecoveryApiError && error.code === 'offline' && !error.message.includes('private'))
})
