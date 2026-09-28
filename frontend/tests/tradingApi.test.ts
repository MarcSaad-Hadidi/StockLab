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
  totalAmount: 250,
  cashBalance: 99_750,
  holdingQuantity: 2,
  averageCost: 125,
  executedAtUtc: '2026-09-27T12:00:00Z',
}

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
