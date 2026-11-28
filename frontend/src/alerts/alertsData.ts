import { isPositiveAmount } from '../api/decimalAmount'

export type AlertStatus = 'active' | 'triggered' | 'disabled'
export type AlertCondition = 'above' | 'below'

export type AlertTerms = {
  condition: AlertCondition
  targetPrice: string
}

export type PriceAlert = AlertTerms & {
  id: string
  symbol: string
  currency: string
  status: AlertStatus
  triggeredPrice: string | null
  triggeredAtUtc: string | null
  createdAtUtc: string
}

export function isAlertPrice(value: unknown): value is string {
  return isPositiveAmount(value) && /^\d{1,15}(?:\.\d{1,4})?$/.test(value)
}

export function parseAlertTarget(input: string): string | null {
  const price = input.trim().replace(',', '.').replace(/^0+(?=\d)/, '').replace(/^\./, '0.')
  return isAlertPrice(price) ? price : null
}
