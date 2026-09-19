import { useTranslation } from 'react-i18next'
import type { StockFundamentals, StockNews, StockQuote } from '../api/marketDataApi'
import { localeForLanguage } from '../i18n/formatters'
import { money } from './stockDetailsData'
import { companyHeadlines } from './companyNews'

export function StockDataPanel({ tab, quote, fundamentals, news }: {
  tab: string; quote?: StockQuote; fundamentals?: StockFundamentals; news?: StockNews
}) {
  const { t, i18n } = useTranslation()
  const locale = localeForLanguage(i18n.language)
  const currency = quote?.currency ?? fundamentals?.currency ?? null
  const price = (n: number | null | undefined) => money(n, currency, locale)
  const number = (n: number | null | undefined) => n == null ? '—' : n.toLocaleString(locale, { maximumFractionDigits: 2 })
  const percent = (n: number | null | undefined) => n == null ? '—' : new Intl.NumberFormat(locale, { style: 'percent', minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(n)
  const grid = (rows: [string, string][]) => <dl className="stock-data-grid">{rows.map(([label, value]) => <div key={label}><dt>{t(label)}</dt><dd>{value}</dd></div>)}</dl>
  const target = fundamentals?.analystTargetPrice
  const gap = target != null && target > 0 && quote?.price ? target / quote.price - 1 : null
  const weekLow = quote?.fiftyTwoWeek?.low ?? fundamentals?.fiftyTwoWeekLow
  const weekHigh = quote?.fiftyTwoWeek?.high ?? fundamentals?.fiftyTwoWeekHigh
  const headlines = companyHeadlines(news, quote?.symbol ?? news?.symbol ?? '', quote?.name ?? fundamentals?.name ?? '')
  return <section className="stock-data-panel">
    <h2>{t(`stockDetails.tabs.${tab}`)}</h2>
    {tab === 'news' ? <>
      <p className="stock-data-source">{t('stockPanels.newsSource')}</p>
      {news && headlines.length === 0 && <p>{t('stockPanels.noNews')}</p>}
      <ul className="stock-news-list">{headlines.map(article => <li key={article.url}>
        <p><span>{article.source}</span><time dateTime={article.publishedAtUtc}>{new Date(article.publishedAtUtc).toLocaleString(locale)}</time></p>
        <a href={article.url} target="_blank" rel="noopener noreferrer">{article.title} ↗</a>
      </li>)}</ul>
    </> : tab === 'financials' ? <>
      <p className="stock-data-source">{t('stockPanels.financialSource')}</p>
      {fundamentals?.sector && <p>{fundamentals.sector}{fundamentals.industry && ` · ${fundamentals.industry}`}</p>}
      {grid([
        ['stockPanels.revenue', price(fundamentals?.revenueTtm)], ['stockPanels.grossProfit', price(fundamentals?.grossProfitTtm)],
        ['stockPanels.ebitda', price(fundamentals?.ebitda)], ['stockDetails.stats.eps', price(fundamentals?.epsTtm)],
        ['stockPanels.profitMargin', percent(fundamentals?.profitMargin)], ['stockPanels.operatingMargin', percent(fundamentals?.operatingMarginTtm)],
        ['stockPanels.roe', percent(fundamentals?.returnOnEquityTtm)], ['stockPanels.revenueGrowth', percent(fundamentals?.quarterlyRevenueGrowthYoy)],
        ['stockPanels.earningsGrowth', percent(fundamentals?.quarterlyEarningsGrowthYoy)], ['stockDetails.stats.dividendYield', percent(fundamentals?.dividendYield)],
      ])}
    </> : tab === 'keyMetrics' ? <>
      <p className="stock-data-source">{t('stockPanels.metricsSource')}</p>
      {grid([
        ['stockDetails.stats.marketCap', price(fundamentals?.marketCap)], ['stockDetails.stats.peRatio', number(fundamentals?.peRatio)],
        ['stockPanels.peg', number(fundamentals?.pegRatio)], ['stockDetails.metrics.beta', number(fundamentals?.beta)],
        ['stockDetails.metrics.open', price(quote?.open)], ['stockDetails.metrics.previousClose', price(quote?.previousClose)],
        ['stockDetails.stats.volume', number(quote?.volume)], ['marketApi.averageVolume', number(quote?.averageVolume)],
        ['stockDetails.stats.weekRange', `${price(weekLow)} – ${price(weekHigh)}`], ['stockPanels.bookValue', price(fundamentals?.bookValue)],
        ['stockPanels.average50', price(fundamentals?.fiftyDayMovingAverage)], ['stockPanels.average200', price(fundamentals?.twoHundredDayMovingAverage)],
      ])}
    </> : tab === 'forecast' ? <>
      <p className="stock-data-source">{t('stockPanels.forecastSource')}</p>
      {grid([['stockPanels.currentPrice', price(quote?.price)], ['stockDetails.metrics.analystTarget', price(target)], ['stockPanels.targetGap', percent(gap)]])}
      <h3>{t('stockDetails.metrics.analystRating')}</h3>
      {grid(Object.entries(fundamentals?.analystRatings ?? { strongBuy: null, buy: null, hold: null, sell: null, strongSell: null }).map(([key, count]) => [`marketApi.ratings.${key}`, number(count)]))}
      <p className="stock-data-note">{t('stockPanels.forecastNote')}</p>
    </> : <>
      <p className="stock-data-source">{t('stockPanels.insightsSource')}</p>
      <ul className="stock-insights-list">
        <li>{t('stockPanels.priceFact', { price: price(quote?.price), change: percent(quote?.changePercent == null ? null : quote.changePercent / 100) })}</li>
        <li>{t('stockPanels.volumeFact', { volume: number(quote?.volume), average: number(quote?.averageVolume) })}</li>
        <li>{t('stockPanels.rangeFact', { low: price(weekLow), high: price(weekHigh) })}</li>
        <li>{t('stockPanels.valuationFact', { pe: number(fundamentals?.peRatio), eps: price(fundamentals?.epsTtm) })}</li>
      </ul>
      <p className="stock-data-note">{t('stockPanels.insightsNote')}</p>
    </>}
  </section>
}
