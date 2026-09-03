import assert from 'node:assert/strict'
import { test } from 'node:test'
import { filterMarketStocks, getMarketSections, getStockBySymbol, marketStocks } from '../src/market/marketData.ts'

test('filters market stocks by symbol or company name without changing the source list', () => {
  const results = filterMarketStocks(marketStocks, 'microsoft')

  assert.deepEqual(results.map((stock) => stock.symbol), ['MSFT'])
  assert.equal(marketStocks.length, 35)
})

test('filters market stocks by the selected asset type', () => {
  const results = filterMarketStocks(marketStocks, '', 'ETFs')

  assert.ok(results.length > 0)
  assert.ok(results.every((stock) => stock.assetType === 'ETF'))
})

test('builds the three overview sections in the order shown by the Market page', () => {
  const sections = getMarketSections(marketStocks)

  assert.deepEqual(sections.popular.map((stock) => stock.symbol), ['AAPL', 'MSFT', 'NVDA', 'AMZN', 'GOOGL'])
  assert.deepEqual(sections.gainers.map((stock) => stock.symbol), ['SMCI', 'ARM', 'PLTR', 'MSTR', 'RDDT'])
  assert.deepEqual(sections.losers.map((stock) => stock.symbol), ['WBD', 'PDD', 'NIO', 'SNAP', 'LCID'])
})

test('adapts overview sections to the selected asset filter', () => {
  const etfSections = getMarketSections(marketStocks, 'ETFs')
  const indexSections = getMarketSections(marketStocks, 'Indices')
  const cryptoSections = getMarketSections(marketStocks, 'Crypto')
  const usMarketSections = getMarketSections(marketStocks, 'US Market')

  assert.ok(etfSections.popular.length > 0)
  assert.ok(etfSections.gainers.length > 0)
  assert.ok(etfSections.losers.length > 0)
  assert.ok([...etfSections.popular, ...etfSections.gainers, ...etfSections.losers].every((stock) => stock.assetType === 'ETF'))
  assert.ok([...indexSections.popular, ...indexSections.gainers, ...indexSections.losers].every((stock) => stock.assetType === 'Index'))
  assert.ok([...cryptoSections.popular, ...cryptoSections.gainers, ...cryptoSections.losers].every((stock) => stock.assetType === 'Crypto'))
  assert.ok([...usMarketSections.popular, ...usMarketSections.gainers, ...usMarketSections.losers].every((stock) => stock.market === 'US Market'))
})

test('looks up a stock for the Stock Details destination case-insensitively', () => {
  assert.equal(getStockBySymbol(marketStocks, 'nvda')?.company, 'NVIDIA Corporation')
  assert.equal(getStockBySymbol(marketStocks, 'missing'), undefined)
})
