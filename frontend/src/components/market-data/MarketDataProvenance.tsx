import { useTranslation } from 'react-i18next'
import { localeForLanguage } from '../../i18n/formatters'
import type { QuoteProvenance } from '../../api/marketDataApi'
import './marketDataProvenance.css'

function usableTime(value: string | null | undefined): value is string {
  return typeof value === 'string' && /(?:Z|[+-]\d{2}:\d{2})$/i.test(value) && Number.isFinite(Date.parse(value))
}

export function MarketDataProvenance({ source, asOfUtc, receivedAtUtc, compact = false, sourceOnly = false }: QuoteProvenance & { compact?: boolean; sourceOnly?: boolean }) {
  const { t, i18n } = useTranslation()
  const label = source === 'Mock' ? t('marketProvenance.mock') : source === 'TwelveData' ? 'Twelve Data'
    : source === 'AlphaVantage' ? 'Alpha Vantage' : t('marketProvenance.unknownSource')
  const formatter = new Intl.DateTimeFormat(localeForLanguage(i18n.language), {
    dateStyle: 'medium', timeStyle: 'medium', timeZone: 'UTC',
  })
  const format = (value: string) => `${formatter.format(new Date(value))} UTC`
  return <span data-market-provenance className={`market-data-provenance${compact ? ' market-data-provenance-compact' : ''}`}>
    <span className={`market-data-source${source === 'Mock' ? ' market-data-source-mock' : ''}`}>{t('marketProvenance.source', { source: label })}</span>
    {!sourceOnly && <span>{usableTime(asOfUtc)
      ? <>{t('marketProvenance.observedAt')} <time dateTime={asOfUtc}>{format(asOfUtc)}</time></>
      : t('marketProvenance.unknownObservation')}</span>}
    {!sourceOnly && !compact && usableTime(receivedAtUtc) && <span>{t('marketProvenance.receivedAt')} <time dateTime={receivedAtUtc}>{format(receivedAtUtc)}</time></span>}
  </span>
}

export function MarketDataSources({ items }: { items: Array<QuoteProvenance | null | undefined> }) {
  const { t } = useTranslation()
  const sources = Array.from(new Set(items.filter((item): item is QuoteProvenance => Boolean(item)).map(item => item.source ?? null)))
  return sources.length > 0 ? <div className="market-data-sources"><span>{t('marketProvenance.pricingSources')}</span>
    {sources.map(source => <MarketDataProvenance key={source ?? 'unknown'} source={source} sourceOnly />)}</div> : null
}
