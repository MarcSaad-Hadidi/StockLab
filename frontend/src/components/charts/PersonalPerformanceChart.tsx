import { useTranslation } from 'react-i18next'
import type { PortfolioPerformanceRange } from '../../api/portfolioApi'
import { usePortfolioPerformanceHistory } from '../../portfolio/usePortfolioPerformanceHistory'
import { formatCompactCurrency, formatCurrency, formatTradeAmount, localeForLanguage } from '../../i18n/formatters'
import { MarketDataSources } from '../market-data/MarketDataProvenance'
import { PerformanceLineChart } from './PerformanceLineChart'
import './personal-performance-chart.css'

export function PersonalPerformanceChart({ range, ariaLabel }: { range: PortfolioPerformanceRange; ariaLabel: string }) {
  const { t, i18n } = useTranslation()
  const { data, isLoading, error, retry } = usePortfolioPerformanceHistory(range)
  const fullDate = new Intl.DateTimeFormat(localeForLanguage(i18n.language), {
    dateStyle: 'medium', timeStyle: 'medium', timeZone: 'UTC',
  })
  const date = (value: string) => `${fullDate.format(new Date(value))} UTC`
  if (isLoading) return <div className="financial-chart-state personal-history-state" aria-busy="true" role="status">{t('personalHistory.loading')}</div>
  if (error) return <div className="financial-chart-state personal-history-state" role="alert">
    <p>{t(error.code === 'unauthorized' ? 'stockDetails.tradeErrors.unauthorized' : 'personalHistory.loadError')}</p>
    {error.code === 'unauthorized' ? <a href="/login">{t('login.signIn')}</a>
      : <button type="button" onClick={retry}>{t('watchlist.retry')}</button>}
  </div>
  if (!data || data.points.length === 0) return <div className="financial-chart-state personal-history-state" role="status"><p>{t('personalHistory.empty')}</p></div>
  const points = data.points
  const amount = (index: number) => formatTradeAmount(points[index].totalValue, i18n.language, 2, data.currency)
  const labels = points.map(point => date(point.recordedAtUtc))
  const axisDate = new Intl.DateTimeFormat(localeForLanguage(i18n.language), range === '1D'
    ? { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: 'UTC' }
    : range === 'ALL' ? { month: 'short', year: '2-digit', timeZone: 'UTC' }
      : { day: 'numeric', month: 'short', timeZone: 'UTC' })
  return <div className="personal-performance-history">
    {points.length === 1 ? <div className="financial-chart-state personal-history-state" role="status">
      <p>{t('personalHistory.insufficient')}</p>
      <p>{t('personalHistory.lastObservation', { amount: amount(0), date: labels[0] })}</p>
    </div> : <PerformanceLineChart ariaLabel={ariaLabel}
      values={points.map(point => Number(point.totalValue))} labels={labels}
      formatValue={value => formatCurrency(value, i18n.language, 2, data.currency)}
      formatPointValue={amount} formatTick={value => formatCompactCurrency(value, i18n.language, 2, data.currency)}
      formatAxisLabel={index => axisDate.format(new Date(points[index].recordedAtUtc))}
      pointLabel={index => `${labels[index]}: ${amount(index)}`}
      unavailableMessage="personalHistory.empty" showLatestValue curve="linear" />}
    <div className="personal-history-footer">
      <p>{t('personalHistory.coverage', { count: data.sampleCount, from: date(data.firstRecordedAtUtc!), to: date(data.lastRecordedAtUtc!) })}
        {data.sampleCount > points.length && <> {t('personalHistory.downsampled', { count: points.length })}</>}</p>
      <button type="button" onClick={retry}>{t('personalHistory.refresh')}</button>
    </div>
    <MarketDataSources items={points.filter(point => point.source !== 'Cash').map(point => ({
      source: point.source === 'Mock' ? 'Mock' : 'TwelveData', asOfUtc: point.quoteAsOfUtc, receivedAtUtc: point.recordedAtUtc,
    }))} />
  </div>
}
