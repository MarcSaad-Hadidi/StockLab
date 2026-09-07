import { i18n } from './i18n'

export function localeForLanguage(language = i18n.language) {
  return language === 'fr' ? 'fr-FR' : 'en-US'
}

export function formatCurrency(value: number | null | undefined, language = i18n.language, fractionDigits = 2) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return new Intl.NumberFormat(localeForLanguage(language), {
    currency: 'USD',
    maximumFractionDigits: fractionDigits,
    minimumFractionDigits: fractionDigits,
    style: 'currency',
  }).format(value)
}

export function formatCompactCurrency(value: number, language = i18n.language, fractionDigits = 2) {
  return new Intl.NumberFormat(localeForLanguage(language), {
    currency: 'USD',
    maximumFractionDigits: fractionDigits,
    minimumFractionDigits: fractionDigits,
    notation: 'compact',
    style: 'currency',
  }).format(value)
}

export function formatSignedCurrency(value: number, language = i18n.language, fractionDigits = 2) {
  return `${value >= 0 ? '+' : '-'}${formatCurrency(Math.abs(value), language, fractionDigits)}`
}

export function formatNumber(value: number, language = i18n.language, fractionDigits = 2) {
  return new Intl.NumberFormat(localeForLanguage(language), {
    maximumFractionDigits: fractionDigits,
    minimumFractionDigits: fractionDigits,
  }).format(value)
}

export function formatPercent(value: number, language = i18n.language, fractionDigits = 2, signed = false) {
  return new Intl.NumberFormat(localeForLanguage(language), {
    maximumFractionDigits: fractionDigits,
    minimumFractionDigits: fractionDigits,
    signDisplay: signed ? 'always' : 'auto',
    style: 'percent',
  }).format(value / 100)
}

export function formatSignedPercent(value: number, language = i18n.language, fractionDigits = 2) {
  return formatPercent(value, language, fractionDigits, true)
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
