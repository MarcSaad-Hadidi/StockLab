import { isCalculatedAmount } from './decimalAmount'

const scale = 12

/** Parse the original decimal text, including input exponents, without a numeric operand. */
function normalizedUnits(value: string, digits: number): bigint {
  if (typeof value !== 'string' || value.length > 100) throw new Error('Invalid decimal operand')
  const match = /^([+-]?)(\d*)(?:\.(\d*))?(?:[eE]([+-]?\d+))?$/.exec(value)
  if (!match || !(match[2] || match[3])) throw new Error('Invalid decimal operand')
  // Only the decimal-point offset is numeric; all significant digits stay in BigInt.
  const exponent = Number(match[4] ?? '0')
  if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > 100) throw new Error('Invalid decimal exponent')
  const fraction = match[3] ?? ''
  const shift = exponent - fraction.length + digits
  let units = BigInt((match[2] || '0') + fraction)
  if (shift >= 0) units *= 10n ** BigInt(shift)
  else {
    const divisor = 10n ** BigInt(-shift)
    units = units / divisor + (units % divisor * 2n >= divisor ? 1n : 0n)
  }
  return match[1] === '-' ? -units : units
}

function units(value: string): bigint {
  if (!isCalculatedAmount(value)) throw new Error('Invalid decimal amount')
  const negative = value.startsWith('-')
  const [integer, fraction = ''] = (negative ? value.slice(1) : value).split('.')
  const result = BigInt(integer + fraction.padEnd(scale, '0'))
  return negative ? -result : result
}

function decimal(value: bigint, digits = scale): string {
  const negative = value < 0n
  const text = (negative ? -value : value).toString().padStart(digits + 1, '0')
  const fraction = text.slice(-digits).replace(/0+$/, '')
  return `${negative ? '-' : ''}${text.slice(0, -digits)}${fraction ? `.${fraction}` : ''}`
}

export function normalizeOperand(value: string, digits: 4 | 8): string | null {
  try { return decimal(normalizedUnits(value, digits), digits) } catch { return null }
}

export function tradeProduct(price: string, quantity: string): string {
  return decimal(normalizedUnits(price, 4) * normalizedUnits(quantity, 8))
}

/** Live valuation keeps the quote's precision; order execution alone normalizes prices to four decimals. */
export function valuationProduct(price: string, quantity: string): string {
  const product = normalizedUnits(price, 28) * normalizedUnits(quantity, 8)
  const divisor = 10n ** 24n
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
