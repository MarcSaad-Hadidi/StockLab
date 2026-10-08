import { i18n } from './i18n.ts'
import { isCalculatedAmount, isDecimalAmount, isPositiveOperand } from '../api/decimalAmount.ts'

export function localeForLanguage(language = i18n.language) {
  return language === 'fr' ? 'fr-FR' : 'en-US'
}

export function formatCurrency(value: number | string | null | undefined, language = i18n.language, fractionDigits = 2, currency = 'USD') {
  if (typeof value === 'string') return formatTradeAmount(value, language, fractionDigits, currency)
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return new Intl.NumberFormat(localeForLanguage(language), {
    currency,
    maximumFractionDigits: fractionDigits,
    minimumFractionDigits: fractionDigits,
    style: 'currency',
  }).format(value)
}

/** Preserve the twelve-decimal trade amount while keeping each view's usual minimum precision. */
export function formatTradeAmount(value: string | null | undefined, language = i18n.language, minimumFractionDigits = 2, currency = 'USD') {
  if (!isCalculatedAmount(value)) return '—'
  const formatter = new Intl.NumberFormat(localeForLanguage(language), {
    currency, minimumFractionDigits, maximumFractionDigits: 12, style: 'currency',
  })
  // Intl formats decimal strings losslessly. TypeScript's declaration still omits that overload.
  return (formatter.format as unknown as (decimal: string) => string)(value)
}

export function formatCompactCurrency(value: number | string | null | undefined, language = i18n.language, fractionDigits = 2, currency = 'USD') {
  if (value == null || (typeof value === 'string' ? !isCalculatedAmount(value) : !Number.isFinite(value))) return '—'
  return new Intl.NumberFormat(localeForLanguage(language), {
    currency,
    maximumFractionDigits: fractionDigits,
    minimumFractionDigits: fractionDigits,
    notation: 'compact',
    style: 'currency',
  }).format(value as number)
}

export function formatSignedCurrency(value: number | string | null | undefined, language = i18n.language, fractionDigits = 2, currency = 'USD') {
  if (typeof value === 'string') {
    if (!isCalculatedAmount(value)) return '—'
    return `${value.startsWith('-') ? '-' : '+'}${formatTradeAmount(value.replace(/^-/, ''), language, fractionDigits, currency)}`
  }
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return `${value >= 0 ? '+' : '-'}${formatCurrency(Math.abs(value), language, fractionDigits, currency)}`
}

export function formatNumber(value: number | string | null | undefined, language = i18n.language, fractionDigits = 2) {
  if (value == null || (typeof value === 'string' ? !isDecimalAmount(value) : !Number.isFinite(value))) return '—'
  return new Intl.NumberFormat(localeForLanguage(language), {
    maximumFractionDigits: fractionDigits,
    minimumFractionDigits: fractionDigits,
  }).format(value as number)
}

export function formatQuantity(value: string, language = i18n.language) {
  if (!isDecimalAmount(value)) return '—'
  return new Intl.NumberFormat(localeForLanguage(language), { maximumFractionDigits: 8 }).format(value as unknown as number)
}

/** Unit quotes can have greater precision than the twelve-decimal ledger amounts. */
export function formatUnitPrice(value: string | null | undefined, language = i18n.language, currency = 'USD') {
  if (!isPositiveOperand(value)) return '—'
  return new Intl.NumberFormat(localeForLanguage(language), {
    style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: 28,
  }).format(value as unknown as number)
}

export function formatPercent(value: number | null | undefined, language = i18n.language, fractionDigits = 2, signed = false) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return new Intl.NumberFormat(localeForLanguage(language), {
    maximumFractionDigits: fractionDigits,
    minimumFractionDigits: fractionDigits,
    signDisplay: signed ? 'always' : 'auto',
    style: 'percent',
  }).format(value / 100)
}

export function formatSignedPercent(value: number | null | undefined, language = i18n.language, fractionDigits = 2) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return formatPercent(value, language, fractionDigits, true)
}

/** Format a wall-clock HH:mm value without changing its time zone. */
export function formatTime(value: string, language = i18n.language) {
  return new Intl.DateTimeFormat(localeForLanguage(language), {
    hour: 'numeric', minute: '2-digit', timeZone: 'UTC',
  }).format(new Date(`1970-01-01T${value}:00.000Z`))
}

export function formatDate(value: string | number | Date, language = i18n.language) {
  const date = value instanceof Date
    ? value
    : typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)
      ? new Date(`${value}T00:00:00.000Z`)
      : new Date(value)

  return new Intl.DateTimeFormat(localeForLanguage(language), {
    day: 'numeric',
    month: 'short',
    timeZone: 'UTC',
    year: 'numeric',
  }).format(date)
}
