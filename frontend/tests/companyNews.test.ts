import assert from 'node:assert/strict'
import { test } from 'node:test'
import { companyHeadlines } from '../src/market/companyNews.ts'

test('headlines must name the selected company or ticker, not merely mention it in article metadata', () => {
  const titles = ['Apple launches a new product', 'AAPL earnings update', 'Pineapple market grows', 'Intel vs SK Hynix', 'Ameren quarterly filing']
  const news = { symbol: 'AAPL', articles: titles.map(title => ({ title, url: 'https://example.com/apple', source: 'Example', publishedAtUtc: '2026-09-18T18:00:00Z' })) }
  assert.deepEqual(companyHeadlines(news, 'AAPL:NASDAQ', 'Apple Inc.').map(a => a.title), titles.slice(0, 2))
  assert.deepEqual(companyHeadlines(news, 'MSFT', 'Microsoft Corporation'), [])
  assert.deepEqual(companyHeadlines(undefined, 'AAPL', 'Apple Inc.'), [])
  assert.deepEqual(companyHeadlines({ ...news, articles: [{ ...news.articles[0], title: 'A broad market update' }] }, 'A', 'Agilent Technologies Inc.'), [])
})
