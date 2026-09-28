import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createPortfolioApi, PortfolioApiError } from '../src/api/portfolioApi.ts'

const validPortfolio = {
  cashBalance: 98_000,
  investedValue: 2_000,
  totalValue: 100_000,
  currency: 'USD',
  positions: [{ symbol: 'AAPL:NASDAQ', quantity: 10, averageCost: 200 }],
}

test('portfolio api sends the bearer token and validates the portfolio contract', async () => {
  const requests: RequestInit[] = []
  const api = createPortfolioApi('https://api.example.test/', async (input, init) => {
    assert.equal(input, 'https://api.example.test/api/portfolio')
    requests.push(init ?? {})
    return new Response(JSON.stringify(validPortfolio), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    })
  }, () => ({ Authorization: 'Bearer test-token' }))

  const portfolio = await api.getPortfolio()

  assert.deepEqual(portfolio, { ...validPortfolio, initialCapital: null })
  assert.deepEqual(requests[0]?.headers, {
    Accept: 'application/json',
    Authorization: 'Bearer test-token',
  })
})

test('portfolio api rejects calls without an authenticated session', async () => {
  let calls = 0
  const api = createPortfolioApi('', async () => {
    calls += 1
    return new Response('{}')
  }, () => null)

  await assert.rejects(api.getPortfolio(), (error: unknown) => {
    assert.ok(error instanceof PortfolioApiError)
    assert.equal(error.code, 'unauthorized')
    return true
  })
  assert.equal(calls, 0)
})

test('portfolio api rejects malformed successful responses instead of exposing partial data', async () => {
  const api = createPortfolioApi('', async () => new Response(JSON.stringify({
    ...validPortfolio,
    positions: [{ symbol: 'AAPL', quantity: 10, averageCost: 0 }],
  }), { status: 200 }), () => ({ Authorization: 'Bearer test-token' }))

  await assert.rejects(api.getPortfolio(), (error: unknown) => {
    assert.ok(error instanceof PortfolioApiError)
    assert.equal(error.code, 'invalid_response')
    return true
  })
})

test('portfolio api maps a missing portfolio to a stable error code', async () => {
  const api = createPortfolioApi('', async () => new Response(JSON.stringify({ error: 'portfolio_not_found' }), { status: 404 }), () => ({ Authorization: 'Bearer test-token' }))

  await assert.rejects(api.getPortfolio(), (error: unknown) => {
    assert.ok(error instanceof PortfolioApiError)
    assert.equal(error.code, 'portfolio_not_found')
    assert.equal(error.status, 404)
    return true
  })
})

test('portfolio api reads the authenticated recent transaction feed', async () => {
  const api = createPortfolioApi('', async (input, init) => {
    assert.equal(input, '/api/portfolio/transactions?limit=3')
    assert.equal((init?.headers as Record<string, string>).Authorization, 'Bearer test-token')
    return new Response(JSON.stringify([{
      id: 'transaction-id',
      side: 'BUY',
      symbol: 'AAPL:NASDAQ',
      quantity: 2,
      executionPrice: 200,
      totalAmount: 400,
      executedAtUtc: '2026-09-28T12:00:00Z',
    }]), { status: 200 })
  }, () => ({ Authorization: 'Bearer test-token' }))

  const transactions = await api.getRecentTransactions(3)

  assert.equal(transactions[0]?.symbol, 'AAPL:NASDAQ')
  assert.equal(transactions[0]?.side, 'BUY')
})
