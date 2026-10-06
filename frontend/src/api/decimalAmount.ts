/** Plain decimal text from the ledger, never converted through a floating-point number. */
export function isDecimalAmount(value: unknown): value is string {
  return typeof value === 'string' && /^-?(?:0|[1-9]\d{0,28})(?:\.\d{1,12})?$/.test(value)
}

export function isNonNegativeAmount(value: unknown): value is string {
  return isDecimalAmount(value) && !value.startsWith('-')
}

export function isPositiveAmount(value: unknown): value is string {
  return isNonNegativeAmount(value) && /[1-9]/.test(value)
}
