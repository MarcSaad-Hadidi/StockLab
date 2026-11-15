import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createGoogleAuthApi, GoogleAuthApiError } from '../src/api/googleAuthApi.ts'

const session = { accessToken: 'stocklab-jwt', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account', displayName: 'Alex', email: 'alex@example.com', preferredLanguage: 'fr' } }
const json = (value: unknown, status = 200, headers = {}) => new Response(JSON.stringify(value), { status, headers })

test('Google config encodes the actual origin and disabled configuration has no invented client ID', async () => {
  const requests: [string, RequestInit | undefined][] = []
  const api = createGoogleAuthApi('https://api.example/', async (url, init) => {
    requests.push([String(url), init]); return json({ enabled: false })
  })
  assert.deepEqual(await api.getConfig('https://app.example:443'), { enabled: false })
  assert.equal(requests[0][0], 'https://api.example/api/auth/google/config?origin=https%3A%2F%2Fapp.example%3A443')
  assert.equal(requests[0][1]?.method, 'GET')
})

test('Google exchanges a code and links with password proof using the anti-CSRF header and normal session contract', async () => {
  const calls: [string, RequestInit | undefined][] = []
  const api = createGoogleAuthApi('', async (url, init) => { calls.push([String(url), init]); return json(session) })
  assert.deepEqual(await api.exchange({ code: 'one-use-code', language: 'fr' }), session)
  assert.deepEqual(await api.link({ linkToken: 'opaque-ticket', password: ' proof ', language: 'en' }), session)
  assert.deepEqual(calls.map(([url, init]) => [url, init?.method, init?.headers, JSON.parse(String(init?.body))]), [
    ['/api/auth/google/exchange', 'POST', { Accept: 'application/json', 'Content-Type': 'application/json', 'X-Requested-With': 'StockLab.GoogleOAuth' }, { code: 'one-use-code', language: 'fr' }],
    ['/api/auth/google/link', 'POST', { Accept: 'application/json', 'Content-Type': 'application/json', 'X-Requested-With': 'StockLab.GoogleOAuth' }, { linkToken: 'opaque-ticket', password: ' proof ', language: 'en' }],
  ])
})

test('only a valid collision response exposes the short-lived link ticket', async () => {
  const api = createGoogleAuthApi('', async () => json({ error: 'google_link_required', linkToken: 'opaque', expiresAtUtc: '2099-01-01T00:00:00Z' }, 409))
  await assert.rejects(api.exchange({ code: 'code', language: 'en' }), error => error instanceof GoogleAuthApiError
    && error.code === 'google_link_required' && error.linkTicket?.linkToken === 'opaque')
  const invalid = createGoogleAuthApi('', async () => json({ error: 'google_link_required', linkToken: '', expiresAtUtc: 'bad' }, 409))
  await assert.rejects(invalid.exchange({ code: 'code', language: 'en' }), error => error instanceof GoogleAuthApiError
    && error.code === 'invalid_response' && error.linkTicket === undefined)
})

for (const [status, code] of [[401, 'invalid_credentials'], [400, 'google_link_expired'], [409, 'google_account_conflict'],
  [401, 'google_invalid_response'], [503, 'google_unavailable']] as const) {
  test(`Google maps ${code} without exposing backend details`, async () => {
    const api = createGoogleAuthApi('', async () => json({ error: code, message: 'internal secret' }, status))
    await assert.rejects(api.exchange({ code: 'code', language: 'en' }), error => error instanceof GoogleAuthApiError
      && error.code === code && !error.message.includes('internal secret'))
  })
}

test('Google rejects non-200 and malformed success responses, and handles network failures and rate limits', async () => {
  for (const response of [json(session, 201), json({ ...session, accessToken: '' }), json({ enabled: true, clientId: '' })]) {
    const api = createGoogleAuthApi('', async () => response)
    await assert.rejects(api.exchange({ code: 'code', language: 'en' }), error => error instanceof GoogleAuthApiError && error.code === 'invalid_response')
  }
  const offline = createGoogleAuthApi('', async () => { throw new Error('socket secret') })
  await assert.rejects(offline.getConfig('https://app.example'), error => error instanceof GoogleAuthApiError && error.code === 'offline')
  const limited = createGoogleAuthApi('', async () => json({}, 429, { 'Retry-After': '12' }))
  await assert.rejects(limited.exchange({ code: 'code', language: 'en' }), error => error instanceof GoogleAuthApiError
    && error.code === 'rate_limited' && error.retryAfterSeconds === 12)
})
