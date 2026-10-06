import assert from 'node:assert/strict'
import { test } from 'node:test'
import { TradingApiError, createTradingApi } from '../src/api/tradingApi.ts'

function jsonResponse(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'content-type': 'application/json' },
  })
}

const result = {
  transactionId: '3b7f6c30-8c4f-4fe8-9350-2f6be1f9c2c0',
  orderId: '4b7f6c30-8c4f-4fe8-9350-2f6be1f9c2c0',
  side: 'BUY',
  symbol: 'AAPL',
  quantity: 2,
  executionPrice: 125,
  totalAmount: '250',
  cashBalance: 99_750,
  holdingQuantity: 2,
  averageCost: 125,
  executedAtUtc: '2026-09-27T12:00:00Z',
}

test('executed order totals remain exact decimal strings and reject lossy numeric responses', async () => {
  const request = { orderId: result.orderId, side: 'BUY' as const, symbol: 'AAPL', quantity: 2, orderType: 'market' as const }
  for (const totalAmount of ['999999989999999.999900000001', '10000.000000000001', '0.000000000001']) {
    const api = createTradingApi('', async () => jsonResponse({ ...result, totalAmount }),
      () => ({ Authorization: 'Bearer test-token' }))
    assert.equal((await api.executeTrade(request)).totalAmount, totalAmount)
  }
  for (const totalAmount of [250, '', '-1', '0', '1e-12', '0.0000000000001']) {
    const api = createTradingApi('', async () => jsonResponse({ ...result, totalAmount }),
      () => ({ Authorization: 'Bearer test-token' }))
    await assert.rejects(api.executeTrade(request),
      (error: unknown) => error instanceof TradingApiError && error.code === 'invalid_response')
  }
})

test('executeTrade sends the authenticated paper order contract', async () => {
  let request: { url: string; init: RequestInit } | undefined
  const api = createTradingApi('http://localhost:5274', async (url, init) => {
    request = { url, init }
    return jsonResponse(result)
  }, () => ({ Authorization: 'Bearer signed-token' }))

  assert.deepEqual(await api.executeTrade({
    orderId: result.orderId,
    side: 'BUY',
    symbol: ' aapl ',
    quantity: 2,
    orderType: 'market',
  }), result)
  assert.equal(request?.url, 'http://localhost:5274/api/portfolio/trades')
  assert.equal(request?.init.method, 'POST')
  assert.equal((request?.init.headers as Record<string, string>).Authorization, 'Bearer signed-token')
  assert.deepEqual(JSON.parse(String(request?.init.body)), {
    orderId: result.orderId,
    side: 'BUY',
    symbol: 'AAPL',
    quantity: 2,
    orderType: 'market',
  })
})

test('limit orders include their limit price and missing sessions are rejected locally', async () => {
  const api = createTradingApi('', async () => jsonResponse(result), () => null)
  await assert.rejects(
    api.executeTrade({ orderId: result.orderId, side: 'SELL', symbol: 'AAPL', quantity: 1, orderType: 'limit', limitPrice: 130 }),
    (error: unknown) => error instanceof TradingApiError && error.code === 'unauthorized',
  )

  let body: unknown
  const authorizedApi = createTradingApi('', async (_, init) => {
    body = JSON.parse(String(init?.body))
    return jsonResponse({ ...result, side: 'SELL', executionPrice: 130 })
  }, () => ({ Authorization: 'Bearer token' }))
  await authorizedApi.executeTrade({ orderId: result.orderId, side: 'SELL', symbol: 'AAPL', quantity: 1, orderType: 'limit', limitPrice: 130 })
  assert.deepEqual(body, {
    orderId: result.orderId,
    side: 'SELL',
    symbol: 'AAPL',
    quantity: 1,
    orderType: 'limit',
    limitPrice: 130,
  })
})

test('maps controlled backend errors and malformed success responses', async () => {
  const api = createTradingApi('', async () => jsonResponse({ error: 'insufficient_cash' }, 422), () => ({ Authorization: 'Bearer token' }))
  await assert.rejects(
    api.executeTrade({ orderId: result.orderId, side: 'BUY', symbol: 'AAPL', quantity: 2, orderType: 'market' }),
    (error: unknown) => error instanceof TradingApiError && error.status === 422 && error.code === 'insufficient_cash',
  )

  const malformed = createTradingApi('', async () => jsonResponse({ transactionId: '' }), () => ({ Authorization: 'Bearer token' }))
  await assert.rejects(
    malformed.executeTrade({ orderId: result.orderId, side: 'BUY', symbol: 'AAPL', quantity: 2, orderType: 'market' }),
    (error: unknown) => error instanceof TradingApiError && error.code === 'invalid_response',
  )
})

test('buy and sell requests preserve fractional quantities and four-decimal limits', async () => {
  for (const side of ['BUY', 'SELL'] as const) {
    for (const limitPrice of [0.0001, 1.2345]) {
      const order = { orderId: result.orderId, side, symbol: 'AAPL', quantity: 1.12345678, orderType: 'limit' as const, limitPrice }
      let body: unknown
      const api = createTradingApi('', async (_, init) => {
        body = JSON.parse(String(init.body))
        return jsonResponse(result)
      }, () => ({ Authorization: 'Bearer test-token' }))
      await api.executeTrade(order)
      assert.deepEqual(body, order)
    }
  }
})

test('a prepared order refuses a replaced or missing token before fetching', async () => {
  for (const current of [null, { Authorization: 'Bearer account-b' }, { Authorization: 'Bearer rotated-account-a' }]) {
    let requests = 0
    let sentNotifications = 0
    const api = createTradingApi('', async () => {
      requests++
      return jsonResponse(result)
    }, () => current)
    await assert.rejects(api.executeTrade({ orderId: result.orderId, side: 'BUY', symbol: 'AAPL', quantity: 2,
      orderType: 'market' }, undefined, 'Bearer account-a', () => { sentNotifications++ }),
    (error: unknown) => error instanceof TradingApiError && error.code === 'session_changed')
    assert.equal(requests, 0)
    assert.equal(sentNotifications, 0)
  }
})

test('a prepared order sends its checked token and abort signal', async () => {
  const controller = new AbortController()
  let authorizationReads = 0
  let sentNotifications = 0
  const api = createTradingApi('', async (_, init) => {
    assert.equal(sentNotifications, 1)
    assert.equal((init.headers as Record<string, string>).Authorization, 'Bearer account-a')
    assert.equal(init.signal, controller.signal)
    return jsonResponse(result)
  }, () => ({ Authorization: ++authorizationReads === 1 ? 'Bearer account-a' : 'Bearer account-b' }))
  await api.executeTrade({ orderId: result.orderId, side: 'BUY', symbol: 'AAPL', quantity: 2,
    orderType: 'market' }, controller.signal, 'Bearer account-a', () => { sentNotifications++ })
  assert.equal(authorizationReads, 1)
  assert.equal(sentNotifications, 1)
})
