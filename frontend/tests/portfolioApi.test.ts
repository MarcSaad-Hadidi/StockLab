import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createPortfolioApi, PortfolioApiError } from '../src/api/portfolioApi.ts'

const validPortfolio = {
  cashBalance: '98000',
  investedValue: '2000',
  totalValue: '100000',
  initialCapital: '100000',
  currency: 'USD',
  positions: [{ symbol: 'AAPL:NASDAQ', quantity: '10', averageCost: '200' }],
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

  assert.deepEqual(portfolio, validPortfolio)
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

test('portfolio requests cannot silently switch away from their captured authorization', async () => {
  let calls = 0
  const api = createPortfolioApi('', async () => { calls += 1; return new Response('{}') },
    () => ({ Authorization: 'Bearer account-b' }))
  for (const request of [
    () => api.getPortfolio(undefined, 'Bearer account-a'),
    () => api.getRecentTransactions(5, undefined, 'Bearer account-a'),
    () => api.getTransactionHistory({ page: 1, pageSize: 10 }, undefined, 'Bearer account-a'),
  ]) await assert.rejects(request(), (error: unknown) => error instanceof PortfolioApiError && error.code === 'unauthorized')
  assert.equal(calls, 0)
})

test('portfolio api rejects malformed successful responses instead of exposing partial data', async () => {
  const api = createPortfolioApi('', async () => new Response(JSON.stringify({
    ...validPortfolio,
    positions: [{ symbol: 'AAPL', quantity: '10', averageCost: '0' }],
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
      quantity: '2',
      executionPrice: '200',
      totalAmount: '400',
      executedAtUtc: '2026-09-28T12:00:00Z',
    }]), { status: 200 })
  }, () => ({ Authorization: 'Bearer test-token' }))

  const transactions = await api.getRecentTransactions(3)

  assert.equal(transactions[0]?.symbol, 'AAPL:NASDAQ')
  assert.equal(transactions[0]?.side, 'BUY')
})

const validHistory = {
  items: [{ id: 'old-trade', side: 'SELL', symbol: 'AAPL:NASDAQ', quantity: '0.25',
    executionPrice: '1.2345', totalAmount: '0.3086', executedAtUtc: '2026-01-01T23:59:59Z' }],
  page: 2, pageSize: 10, totalCount: 11, currency: 'CAD',
  summary: { totalTrades: 61, totalInvested: '2000', totalProceeds: '3000' },
}

test('portfolio preserves exact cash and rejects incompatible numeric money fields', async () => {
  const cashBalance = '99999.999999999999'
  const api = createPortfolioApi('', async () => Response.json({ ...validPortfolio, cashBalance }),
    () => ({ Authorization: 'Bearer token' }))
  assert.equal((await api.getPortfolio()).cashBalance, cashBalance)
  for (const field of ['cashBalance', 'initialCapital', 'investedValue', 'totalValue']) {
    for (const value of [100000, '-1', '1e-12', '0.0000000000001', null, '999999999999999999800000000000.00000001']) {
      const invalid = createPortfolioApi('', async () => Response.json({ ...validPortfolio, [field]: value }),
        () => ({ Authorization: 'Bearer token' }))
      await assert.rejects(invalid.getPortfolio(), (error: unknown) => error instanceof PortfolioApiError && error.code === 'invalid_response')
    }
  }
})

test('history and recent activity preserve decimal strings through JSON parsing', async () => {
  const exact = '999999989999999.999900000001'
  const history = { ...validHistory, items: [{ ...validHistory.items[0], totalAmount: exact }],
    summary: { ...validHistory.summary, totalInvested: '10000.000000000001', totalProceeds: exact } }
  const api = createPortfolioApi('', async input => Response.json(String(input).includes('/history') ? history : history.items),
    () => ({ Authorization: 'Bearer test-token' }))
  const result = await api.getTransactionHistory({ page: 2, pageSize: 10 })
  assert.equal(result.items[0].totalAmount, exact)
  assert.equal(result.summary.totalInvested, '10000.000000000001')
  assert.equal(result.summary.totalProceeds, exact)
  assert.equal((await api.getRecentTransactions())[0].totalAmount, exact)
})

test('transaction APIs reject numeric, malformed, zero and negative ledger totals', async () => {
  for (const amount of [1e-12, 999999990000000, null, '', 'NaN', 'Infinity', '1e-12', '1,000',
    ' 1.25 ', '0.0000000000001', '0', '0.0000', '-0.01', '999999999999999999800000000000.00000001']) {
    const history = { ...validHistory, items: [{ ...validHistory.items[0], totalAmount: amount }] }
    const api = createPortfolioApi('', async input => Response.json(String(input).includes('/history') ? history : history.items),
      () => ({ Authorization: 'Bearer test-token' }))
    await assert.rejects(api.getTransactionHistory({ page: 2, pageSize: 10 }),
      (error: unknown) => error instanceof PortfolioApiError && error.code === 'invalid_response')
    await assert.rejects(api.getRecentTransactions(),
      (error: unknown) => error instanceof PortfolioApiError && error.code === 'invalid_response')
  }
})

test('history validates exact nonnegative aggregates without parsing a number', async () => {
  for (const amount of [Number('10000.000000000001'), '', '-0.000000000001', '1e4', '0.0000000000001']) {
    const api = createPortfolioApi('', async () => Response.json({ ...validHistory,
      summary: { ...validHistory.summary, totalInvested: amount } }), () => ({ Authorization: 'Bearer test-token' }))
    await assert.rejects(api.getTransactionHistory({ page: 2, pageSize: 10 }),
      (error: unknown) => error instanceof PortfolioApiError && error.code === 'invalid_response')
  }
})

test('history requests combine filters and preserve server totals and currency', async () => {
  const controller = new AbortController()
  const api = createPortfolioApi('https://api.example.test/', async (input, init) => {
    const url = new URL(String(input))
    assert.equal(url.pathname, '/api/portfolio/transactions/history')
    assert.deepEqual(Object.fromEntries(url.searchParams), {
      page: '2', pageSize: '10', search: 'AAPL:NASDAQ', side: 'SELL', from: '2026-01-01', to: '2026-01-31',
    })
    assert.equal(init?.signal, controller.signal)
    assert.equal((init?.headers as Record<string, string>).Authorization, 'Bearer test-token')
    return Response.json(validHistory)
  }, () => ({ Authorization: 'Bearer test-token' }))

  const result = await api.getTransactionHistory({ page: 2, pageSize: 10, search: ' AAPL:NASDAQ ',
    side: 'SELL', from: '2026-01-01', to: '2026-01-31' }, controller.signal)
  assert.deepEqual(result, validHistory)
})

test('history rejects missing currency, invalid rows and malformed pagination', async () => {
  for (const body of [
    { ...validHistory, currency: '' },
    { ...validHistory, items: [{ ...validHistory.items[0], quantity: '-1' }] },
    { ...validHistory, page: 0 },
    { ...validHistory, totalCount: -1 },
    { ...validHistory, summary: { ...validHistory.summary, totalProceeds: '-1' } },
  ]) {
    const api = createPortfolioApi('', async () => Response.json(body), () => ({ Authorization: 'Bearer test-token' }))
    await assert.rejects(api.getTransactionHistory({ page: 1, pageSize: 10 }),
      (error: unknown) => error instanceof PortfolioApiError && error.code === 'invalid_response')
  }
})


test('portfolio unit fields retain maximum decimal values and reject numeric operands', async () => {
  const position = { symbol: 'AAPL', quantity: '99999999999.99999999', averageCost: '999999999999999.9999' }
  const api = createPortfolioApi('', async () => Response.json({ ...validPortfolio, positions: [position] }), () => ({ Authorization: 'Bearer token' }))
  assert.deepEqual((await api.getPortfolio()).positions[0], position)
  for (const field of ['quantity', 'averageCost']) {
    const bad = createPortfolioApi('', async () => Response.json({ ...validPortfolio, positions: [{ ...position, [field]: 1 }] }), () => ({ Authorization: 'Bearer token' }))
    await assert.rejects(bad.getPortfolio(), (error: unknown) => error instanceof PortfolioApiError && error.code === 'invalid_response')
  }
})
