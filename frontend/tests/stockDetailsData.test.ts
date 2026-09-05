import assert from 'node:assert/strict'
import { test } from 'node:test'
import { getStockBySymbol, marketStocks } from '../src/market/marketData.ts'
import {
  calculateTradeTotal,
  getStockDetails,
  getTradeExecutionPrice,
  getVisibleHistory,
  type ChartRange,
  type TradeOrderType,
} from '../src/market/stockDetailsData.ts'

test('builds the AAPL detail fixture used by the Stock Details screen', () => {
  const apple = getStockBySymbol(marketStocks, 'AAPL')

  assert.ok(apple)
  const details = getStockDetails(apple)

  assert.equal(details.company, 'Apple Inc.')
  assert.equal(details.exchange, 'NASDAQ')
  assert.equal(details.price, 191.45)
  assert.equal(details.changePercent, '1.35%')
  assert.equal(details.aiInsight.recommendation, 'BUY')
  assert.equal(details.aiInsight.confidence, 82)
  assert.ok(details.history.length >= 12)
})

test('uses market data to provide sensible details for symbols without a custom fixture', () => {
  const microsoft = getStockBySymbol(marketStocks, 'MSFT')

  assert.ok(microsoft)
  const details = getStockDetails(microsoft)

  assert.equal(details.symbol, 'MSFT')
  assert.equal(details.company, 'Microsoft Corporation')
  assert.equal(details.price, 415.6)
  assert.equal(details.stats.marketCap, '$3.08T')
  assert.equal(details.aiInsight.recommendation, 'BUY')
})

test('calculates a trade total from the simulated quote and quantity', () => {
  assert.equal(calculateTradeTotal(191.45, 10), 1914.5)
  assert.equal(calculateTradeTotal(191.45, 0), 0)
  assert.equal(calculateTradeTotal(191.45, -2), 0)
})

test('returns a distinct historical window for every advertised chart range', () => {
  const apple = getStockBySymbol(marketStocks, 'AAPL')

  assert.ok(apple)
  const details = getStockDetails(apple)
  const ranges: ChartRange[] = ['1D', '5D', '1M', '3M', '6M', 'YTD', '1Y', '5Y', 'MAX']
  const lengths = ranges.map((range) => getVisibleHistory(details.history, range).length)

  assert.deepEqual(lengths, [2, 5, 6, 10, 12, 13, 16, 20, 24])
  assert.equal(getVisibleHistory(details.history, 'MAX').at(-1)?.value, details.price)
})

test('uses the selected limit price for limit-order trade totals', () => {
  const marketOrder: TradeOrderType = 'market'
  const limitOrder: TradeOrderType = 'limit'

  assert.equal(getTradeExecutionPrice(marketOrder, 191.45), 191.45)
  assert.equal(getTradeExecutionPrice(limitOrder, 191.45, 188.5), 188.5)
  assert.equal(getTradeExecutionPrice(limitOrder, 191.45), 0)
})
