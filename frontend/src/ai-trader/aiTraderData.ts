import type { AiTraderEquityResponse, AiTraderPositionResponse } from '../api/aiTraderApi'
import { productPercent } from '../api/decimalMath'
import { formatCurrency, formatSignedCurrency, localeForLanguage } from '../i18n/formatters'

export const tabs = ['Overview', 'Positions', 'Trades', 'Decisions', 'Performance', 'Model', 'Backtests'] as const
export type TabName = typeof tabs[number]
export const ranges = ['1M', '3M', '6M', '1Y', 'ALL'] as const
export type EquityRange = typeof ranges[number]
export const rejectionReasons = ['InvalidDecision', 'InvalidPrice', 'HoldSignal', 'LowConfidence', 'CurrencyMismatch', 'MaxPositionsReached', 'MaxSymbolExposureReached', 'InsufficientCash', 'NoPositionToSell', 'TradeTooSmall'] as const

/** Existing percent formatters consume percentage points, while #80 returns fractions. */
export const fractionPoints = (value: number | null): number | null => value === null ? null : value * 100
export const positionPnlPercent = (position: AiTraderPositionResponse) => productPercent(position.pnL, position.quantity, position.averagePrice)
// Opt into #80's CLR decimal precision without changing other pages' ledger contract.
export const formatAiCurrency = (value: number | string | null | undefined, language?: string, digits = 2, currency = 'USD') => formatCurrency(value, language, digits, currency, true)
export const formatAiSignedCurrency = (value: number | string | null | undefined, language?: string, digits = 2, currency = 'USD') => formatSignedCurrency(value, language, digits, currency, true)
export function formatAiTimestamp(value: string | null, language: string) {
  return value === null ? '—' : new Intl.DateTimeFormat(localeForLanguage(language), {
    dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC',
  }).format(new Date(value)) + ' UTC'
}

export function equityInRange(rows: readonly AiTraderEquityResponse[], range: EquityRange, now = new Date()) {
  if (range === 'ALL') return [...rows]
  const months = { '1M': 1, '3M': 3, '6M': 6, '1Y': 12 }[range]
  const start = new Date(now)
  const day = start.getUTCDate()
  start.setUTCDate(1)
  start.setUTCMonth(start.getUTCMonth() - months)
  const lastDay = new Date(Date.UTC(start.getUTCFullYear(), start.getUTCMonth() + 1, 0)).getUTCDate()
  start.setUTCDate(Math.min(day, lastDay))
  return rows.filter(row => Date.parse(row.valuedAtUtc) >= start.getTime() && Date.parse(row.valuedAtUtc) <= now.getTime())
}
