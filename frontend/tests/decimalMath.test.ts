import assert from 'node:assert/strict'
import { test } from 'node:test'
import { addAmounts, subtractAmounts, amountPercent, amountSign, tradeProduct, valuationProduct } from '../src/api/decimalMath.ts'

test('decimal arithmetic keeps cash and ledger fractions exact beside large balances', () => {
  assert.equal(subtractAmounts('999999999999999.9999', '0.000000000001'), '999999999999999.999899999999')
  assert.equal(addAmounts('99999.999999999999', '0.000000000001'), '100000')
  assert.equal(subtractAmounts('0', '0.000000000001'), '-0.000000000001')
  assert.equal(amountSign('-0.000000000001'), -1)
  assert.equal(amountSign('-0.000'), 0)
  assert.equal(amountPercent('1', '0'), null)
  assert.equal(amountPercent('1', '4'), 25)
  assert.throws(() => addAmounts('1e-12', '1'))
})

test('whole and split orders produce the same exact total after backend normalization', () => {
  assert.equal(tradeProduct(1.2345, 1), addAmounts(tradeProduct(1.2345, .5), tradeProduct(1.2345, .5)))
  assert.equal(tradeProduct(.0001, 1e-8), '0.000000000001')
  assert.equal(tradeProduct(1.23445, .500000005), '0.617250012345')
  assert.equal(tradeProduct(.000049, 1), '0')
  assert.equal(tradeProduct(1, .000000004), '0')
})

test('live valuation preserves quote precision beyond four decimals', () => {
  assert.equal(valuationProduct(1.234567, .5), '0.6172835')
  assert.equal(valuationProduct(.000001, .000001), '0.000000000001')
})
