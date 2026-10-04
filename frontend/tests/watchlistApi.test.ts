import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createWatchlistApi, WatchlistApiError } from '../src/api/watchlistApi.ts'

const item = { symbol: 'AAPL', createdAtUtc: '2026-10-04T12:00:00Z' }
const authorization = () => ({ Authorization: 'Bearer test-token' })
const rejectsWith = (code: string) => (error: unknown) => error instanceof WatchlistApiError && error.code === code

test('GET reads authenticated membership with Accept and cancellation', async () => {
  const controller = new AbortController()
  const api = createWatchlistApi('https://api.example.test/', async (url, init) => {
    assert.equal(url, 'https://api.example.test/api/watchlist')
    assert.equal(init?.method, 'GET')
    assert.deepEqual(init?.headers, { Accept: 'application/json', Authorization: 'Bearer test-token' })
    assert.equal(init?.signal, controller.signal)
    return Response.json([item])
  }, authorization)
  assert.deepEqual(await api.getWatchlist(controller.signal), [item])
})

test('POST sends only the normalized symbol and validates the created item', async () => {
  const api = createWatchlistApi('', async (url, init) => {
    assert.equal(url, '/api/watchlist')
    assert.equal(init?.method, 'POST')
    assert.equal(init?.body, '{"symbol":"AAPL"}')
    assert.deepEqual(init?.headers, { Accept: 'application/json', Authorization: 'Bearer test-token', 'Content-Type': 'application/json' })
    return Response.json(item, { status: 201 })
  }, authorization)
  assert.deepEqual(await api.addToWatchlist(' aapl '), item)
})

test('DELETE escapes the whole symbol and accepts 204 without reading a body', async () => {
  const api = createWatchlistApi('', async (url, init) => {
    assert.equal(url, '/api/watchlist/AAPL%3ANASDAQ%25')
    assert.equal(init?.method, 'DELETE')
    assert.equal(init?.body, undefined)
    assert.equal(new Headers(init?.headers).get('Authorization'), 'Bearer test-token')
    const response = new Response(null, { status: 204 })
    response.json = async () => { assert.fail('204 must not parse JSON') }
    return response
  }, authorization)
  await api.removeFromWatchlist('aapl:NASDAQ%')
})

test('every operation requires the existing auth session before sending a request', async () => {
  let calls = 0
  const api = createWatchlistApi('', async () => { calls++; return Response.json([]) }, () => null)
  await assert.rejects(api.getWatchlist(), rejectsWith('unauthorized'))
  await assert.rejects(api.addToWatchlist('AAPL'), rejectsWith('unauthorized'))
  await assert.rejects(api.removeFromWatchlist('AAPL'), rejectsWith('unauthorized'))
  assert.equal(calls, 0)
})

for (const [status, code] of [[401, 'unauthorized'], [404, 'not_found'], [409, 'already_exists'], [400, 'invalid_symbol'], [500, 'server_error'], [503, 'server_error']] as const) {
  test(`HTTP ${status} produces ${code} without exposing backend details`, async () => {
    const api = createWatchlistApi('', async () => Response.json({ error: 'technical_database_details', message: 'secret internals' }, { status }), authorization)
    await assert.rejects(api.addToWatchlist('AAPL'), error => {
      assert.ok(error instanceof WatchlistApiError)
      assert.equal(error.code, code)
      assert.equal(error.status, status)
      assert.doesNotMatch(error.message, /secret|technical_database/)
      return true
    })
  })
}

test('network failure maps to offline', async () => {
  const api = createWatchlistApi('', async () => { throw new TypeError('network failed') }, authorization)
  await assert.rejects(api.getWatchlist(), rejectsWith('offline'))
})

test('invalid JSON and malformed membership are rejected', async () => {
  const values: unknown[] = [{}, [null], [{ ...item, symbol: ' ' }], [{ ...item, createdAtUtc: 'not a date' }], [{ symbol: 'AAPL' }]]
  for (const body of values) {
    const api = createWatchlistApi('', async () => Response.json(body), authorization)
    await assert.rejects(api.getWatchlist(), rejectsWith('invalid_response'))
  }
  const api = createWatchlistApi('', async () => new Response('invalid JSON'), authorization)
  await assert.rejects(api.getWatchlist(), rejectsWith('invalid_response'))
})

test('malformed or unexpected mutation success cannot confirm a change', async () => {
  for (const response of [Response.json({ symbol: 'AAPL' }, { status: 201 }), Response.json(item, { status: 200 }), new Response(null, { status: 204 })]) {
    const api = createWatchlistApi('', async () => response, authorization)
    await assert.rejects(api.addToWatchlist('AAPL'), rejectsWith('invalid_response'))
  }
  const api = createWatchlistApi('', async () => Response.json({}), authorization)
  await assert.rejects(api.removeFromWatchlist('AAPL'), rejectsWith('invalid_response'))
})

test('cancellation remains AbortError, including cancellation while parsing JSON', async () => {
  const controller = new AbortController()
  const api = createWatchlistApi('', async () => {
    const response = Response.json([item])
    response.json = async () => { controller.abort(); throw new DOMException('aborted', 'AbortError') }
    return response
  }, authorization)
  await assert.rejects(api.getWatchlist(controller.signal), (error: unknown) => error instanceof DOMException && error.name === 'AbortError')
  let calls = 0
  const aborted = createWatchlistApi('', async () => { calls++; return Response.json([]) }, authorization)
  await assert.rejects(aborted.getWatchlist(controller.signal), (error: unknown) => error instanceof DOMException && error.name === 'AbortError')
  assert.equal(calls, 0)
})
