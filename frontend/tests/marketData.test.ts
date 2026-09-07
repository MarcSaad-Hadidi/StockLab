import assert from 'node:assert/strict'
import { test } from 'node:test'
import { marketRoute, stockDetailsRoute } from '../src/market/marketRoutes.ts'
import { filterMarketStocks, getMarketSections, getStockBySymbol, marketStocks, paginateMarketStocks } from '../src/market/marketData.ts'

test('filters market stocks by symbol or company name without changing the source list', () => {
  const results = filterMarketStocks(marketStocks, { query: 'microsoft' })

  assert.deepEqual(results.map((stock) => stock.symbol), ['MSFT'])
  assert.equal(marketStocks.length, 35)
})

test('builds the canonical Market and Stock Details routes', () => {
  assert.equal(marketRoute, '/market')
  assert.equal(stockDetailsRoute('BRK.B'), '/market?symbol=brk.b')
})

test('filters market stocks by the selected asset type', () => {
  const results = filterMarketStocks(marketStocks, { assetType: 'ETFs' })

  assert.ok(results.length > 0)
  assert.ok(results.every((stock) => stock.assetType === 'ETF'))
})

test('paginates market results with bounded page metadata', () => {
  const secondPage = paginateMarketStocks(marketStocks, 2, 10)
  const lastPage = paginateMarketStocks(marketStocks, 99, 10)

  assert.equal(secondPage.totalPages, 4)
  assert.equal(secondPage.currentPage, 2)
  assert.equal(secondPage.startIndex, 11)
  assert.equal(secondPage.endIndex, 20)
  assert.deepEqual(secondPage.items.map((stock) => stock.symbol), ['SMCI', 'ARM', 'PLTR', 'MSTR', 'RDDT', 'WBD', 'PDD', 'NIO', 'SNAP', 'LCID'])
  assert.equal(lastPage.currentPage, 4)
  assert.equal(lastPage.startIndex, 31)
  assert.equal(lastPage.endIndex, 35)
  assert.deepEqual(lastPage.items.map((stock) => stock.symbol), ['BTC', 'ETH', 'SOL', 'XRP', 'DOGE'])
})

test('builds the three overview sections in the order shown by the Market page', () => {
  const sections = getMarketSections(marketStocks)

  assert.deepEqual(sections.popular.map((stock) => stock.symbol), ['AAPL', 'MSFT', 'NVDA', 'AMZN', 'GOOGL'])
  assert.deepEqual(sections.gainers.map((stock) => stock.symbol), ['SMCI', 'ARM', 'PLTR', 'MSTR', 'RDDT'])
  assert.deepEqual(sections.losers.map((stock) => stock.symbol), ['WBD', 'PDD', 'NIO', 'SNAP', 'LCID'])
})

test('adapts overview sections to the selected asset filter', () => {
  const etfSections = getMarketSections(marketStocks, { assetType: 'ETFs' })
  const indexSections = getMarketSections(marketStocks, { assetType: 'Indices' })
  const cryptoSections = getMarketSections(marketStocks, { assetType: 'Crypto' })
  const usMarketSections = getMarketSections(marketStocks, { market: 'US Market' })

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

test('returns all stocks when filters are omitted or explicitly cleared', () => {
  assert.deepEqual(filterMarketStocks(marketStocks), marketStocks)
  assert.deepEqual(filterMarketStocks(marketStocks, { query: '  ', assetType: 'All', market: 'All', favoriteOnly: false }), marketStocks)
})

for (const [assetType, expectedType, count] of [
  ['Stocks', 'Stock', 20], ['ETFs', 'ETF', 5], ['Indices', 'Index', 5], ['Crypto', 'Crypto', 5],
] as const) {
  test(`filters ${assetType} independently`, () => {
    const results = filterMarketStocks(marketStocks, { assetType })
    assert.equal(results.length, count)
    assert.ok(results.every((stock) => stock.assetType === expectedType))
  })
}

test('combines market and asset type, including incompatible criteria', () => {
  assert.equal(filterMarketStocks(marketStocks, { market: 'US Market' }).length, 30)
  assert.deepEqual(filterMarketStocks(marketStocks, { assetType: 'ETFs', market: 'US Market' }).map((stock) => stock.symbol), ['VOO', 'SPY', 'QQQ', 'DIA', 'IWM'])
  assert.deepEqual(filterMarketStocks(marketStocks, { assetType: 'Crypto', market: 'US Market' }), [])
})

test('intersects normalized search with asset type and market', () => {
  assert.deepEqual(filterMarketStocks(marketStocks, { query: '  s&p  ', assetType: 'ETFs' }).map((stock) => stock.symbol), ['VOO', 'SPY'])
  assert.deepEqual(filterMarketStocks(marketStocks, { query: 'S&P', assetType: 'ETFs', market: 'US Market' }).map((stock) => stock.symbol), ['VOO', 'SPY'])
  assert.deepEqual(filterMarketStocks(marketStocks, { query: 'bitcoin', assetType: 'Stocks' }), [])
  assert.deepEqual(filterMarketStocks(marketStocks, { query: 'missing' }), [])
  assert.deepEqual(filterMarketStocks([], { query: 'S&P' }), [])
})

test('intersects favorites with every criterion and reflects favorite changes', () => {
  const favoriteSymbols = new Set(['AAPL', 'VOO', 'BTC'])
  const filters = { query: 'S&P', assetType: 'ETFs', market: 'US Market', favoriteOnly: true, favoriteSymbols } as const
  assert.deepEqual(filterMarketStocks(marketStocks, filters).map((stock) => stock.symbol), ['VOO'])
  favoriteSymbols.delete('VOO')
  assert.deepEqual(filterMarketStocks(marketStocks, filters), [])
  assert.deepEqual(filterMarketStocks(marketStocks, { assetType: 'Stocks', favoriteOnly: true, favoriteSymbols }).map((stock) => stock.symbol), ['AAPL'])
  assert.deepEqual(filterMarketStocks(marketStocks, { favoriteOnly: true }), [])
  assert.equal(filterMarketStocks(marketStocks, { favoriteOnly: false, favoriteSymbols }).length, 35)
})

test('applies combined filters to overview sections without falling back to All', () => {
  const sections = getMarketSections(marketStocks, { assetType: 'ETFs', market: 'US Market' })
  assert.deepEqual(sections.popular.map((stock) => stock.symbol), ['VOO', 'SPY', 'QQQ', 'DIA', 'IWM'])
  assert.deepEqual(sections.gainers.map((stock) => stock.symbol), ['QQQ', 'VOO', 'SPY'])
  assert.deepEqual(sections.losers.map((stock) => stock.symbol), ['IWM', 'DIA'])
  assert.deepEqual(getMarketSections(marketStocks, { assetType: 'Crypto', market: 'US Market' }), { popular: [], gainers: [], losers: [] })
})

test('paginates filtered and empty results with accurate counts', () => {
  const results = filterMarketStocks(marketStocks, { assetType: 'ETFs', market: 'US Market' })
  const page = paginateMarketStocks(results, 3)
  assert.equal(page.currentPage, 1)
  assert.equal(page.totalPages, 1)
  assert.equal(page.startIndex, 1)
  assert.equal(page.endIndex, 5)
  assert.deepEqual(page.items, results)
  assert.deepEqual(paginateMarketStocks([], 3), { items: [], currentPage: 1, totalPages: 1, startIndex: 0, endIndex: 0 })
})
