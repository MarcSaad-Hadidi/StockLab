import { isDecimalAmount } from './decimalAmount'

const scale = 12

/** Round serialized decimal text like decimal.Round(..., AwayFromZero), including exponents. */
function normalizedUnits(value: number, digits: number): bigint {
  if (!Number.isFinite(value)) throw new Error('Invalid decimal number')
  const [coefficient, exponent = '0'] = Math.abs(value).toString().split('e')
  const [integer, fraction = ''] = coefficient.split('.')
  const shift = Number(exponent) - fraction.length + digits
  let units = BigInt(integer + fraction)
  if (shift >= 0) units *= 10n ** BigInt(shift)
  else {
    const divisor = 10n ** BigInt(-shift)
    units = units / divisor + (units % divisor * 2n >= divisor ? 1n : 0n)
  }
  return value < 0 ? -units : units
}

function units(value: string): bigint {
  if (!isDecimalAmount(value)) throw new Error('Invalid decimal amount')
  const negative = value.startsWith('-')
  const [integer, fraction = ''] = (negative ? value.slice(1) : value).split('.')
  const result = BigInt(integer + fraction.padEnd(scale, '0'))
  return negative ? -result : result
}

function decimal(value: bigint): string {
  const negative = value < 0n
  const text = (negative ? -value : value).toString().padStart(scale + 1, '0')
  const fraction = text.slice(-scale).replace(/0+$/, '')
  return `${negative ? '-' : ''}${text.slice(0, -scale)}${fraction ? `.${fraction}` : ''}`
}

export function tradeProduct(price: number, quantity: number): string {
  return decimal(normalizedUnits(price, 4) * normalizedUnits(quantity, 8))
}

/** Live valuation keeps the quote's precision; order execution alone normalizes prices to four decimals. */
export function valuationProduct(price: number, quantity: number): string {
  const product = normalizedUnits(price, 12) * normalizedUnits(quantity, 8)
  const divisor = 100_000_000n
  return decimal(product / divisor + (product % divisor * 2n >= divisor ? 1n : 0n))
}

export function addAmounts(left: string, right: string): string {
  return decimal(units(left) + units(right))
}

export function subtractAmounts(left: string, right: string): string {
  return decimal(units(left) - units(right))
}

export function amountSign(value: string): number {
  const result = units(value)
  return result < 0n ? -1 : result > 0n ? 1 : 0
}

/** Percentages/charts may be approximate; monetary operands and differences remain exact. */
export function amountPercent(numerator: string, denominator: string): number | null {
  const divisor = units(denominator)
  return divisor === 0n ? null : Number(units(numerator)) / Number(divisor) * 100
}
