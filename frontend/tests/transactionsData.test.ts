import assert from 'node:assert/strict'
import { test } from 'node:test'
import { filterTransactions, paginateTransactions, transactions, type Transaction } from '../src/transactions/transactionsData.ts'

const fixtureTransactions: Transaction[] = [
  {
    id: 'transaction-aapl-buy',
    symbol: 'AAPL',
    company: 'Apple Inc.',
    assetType: 'Stock',
    action: 'BUY',
    quantity: 10,
    executionPrice: 191.45,
    totalAmount: 1914.5,
    date: '2024-05-24T14:32:00.000Z',
  },
  {
    id: 'transaction-msft-sell',
    symbol: 'MSFT',
    company: 'Microsoft Corp.',
    assetType: 'Stock',
    action: 'SELL',
    quantity: 8,
    executionPrice: 415.6,
    totalAmount: 3324.8,
    date: '2024-05-23T18:15:00.000Z',
  },
  {
    id: 'transaction-voo-buy',
    symbol: 'VOO',
    company: 'Vanguard S&P 500 ETF',
    assetType: 'ETF',
    action: 'BUY',
    quantity: 4,
    executionPrice: 534.12,
    totalAmount: 2136.48,
    date: '2024-05-20T13:30:00.000Z',
  },
]

test('filters transactions by query, asset type, action, and inclusive date range', () => {
  const results = filterTransactions(fixtureTransactions, {
    query: 'apple',
    assetType: 'Stock',
    action: 'BUY',
    from: '2024-05-24',
    to: '2024-05-24',
  })

  assert.deepEqual(results.map((transaction) => transaction.id), ['transaction-aapl-buy'])
})

test('filters the local history by asset type and action', () => {
  const results = filterTransactions(transactions, {
    query: '',
    assetType: 'ETF',
    action: 'BUY',
    from: '',
    to: '',
  })

  assert.ok(results.length > 0)
  assert.ok(results.every((transaction) => transaction.assetType === 'ETF' && transaction.action === 'BUY'))
})

test('paginates the 128 local transactions and clamps the last page', () => {
  const lastPage = paginateTransactions(transactions, 99, 10)

  assert.equal(lastPage.currentPage, 13)
  assert.equal(lastPage.totalPages, 13)
  assert.equal(lastPage.startIndex, 121)
  assert.equal(lastPage.endIndex, 128)
  assert.equal(lastPage.items.length, 8)
})
