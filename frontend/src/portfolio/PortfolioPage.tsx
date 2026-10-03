import { StockLogo, LogoAttribution } from '../market/StockLogo'
import { UnavailableState } from '../components/UnavailableState'
import { PerformanceLineChart } from '../components/charts/PerformanceLineChart'
import { Sidebar } from '../components/layout/Sidebar'
import { TopBar } from '../components/layout/TopBar'
import { formatCompactCurrency, formatCurrency, formatNumber, formatPercent, formatSignedCurrency, formatSignedPercent } from '../i18n/formatters'
import { useTranslation } from 'react-i18next'
import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { performanceSeries } from './portfolioData'
import { usePortfolioData, type PortfolioPosition } from './usePortfolioData'
import './portfolio.css'

type IconName = 'grid' | 'globe' | 'briefcase' | 'sliders' | 'star' | 'bell' | 'brain' | 'user' | 'logout' | 'search' | 'chevron' | 'download' | 'arrowUp' | 'arrowDown' | 'info' | 'menu'
type TimeRange = keyof typeof performanceSeries

function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, ReactNode> = {
    grid: <><rect x="4" y="4" width="6" height="6" rx="1" /><rect x="14" y="4" width="6" height="6" rx="1" /><rect x="4" y="14" width="6" height="6" rx="1" /><rect x="14" y="14" width="6" height="6" rx="1" /></>,
    globe: <><circle cx="12" cy="12" r="8.5" /><path d="M3.5 12h17M12 3.5c2.1 2.3 3.1 5.1 3.1 8.5s-1 6.2-3.1 8.5c-2.1-2.3-3.1-5.1-3.1-8.5s1-6.2 3.1-8.5Z" /></>,
    briefcase: <><rect x="3.5" y="7.5" width="17" height="12.5" rx="2" /><path d="M8 7.5V5.9a1.8 1.8 0 0 1 1.8-1.8h4.4A1.8 1.8 0 0 1 16 5.9v1.6M3.5 12.5h17M11 12.5v2h2v-2" /></>,
    sliders: <><path d="M4 7h16M4 17h16" /><circle cx="9" cy="7" r="2" /><circle cx="15" cy="17" r="2" /></>,
    star: <path d="m12 3 2.7 5.5 6.1.9-4.4 4.3 1 6.1-5.4-2.9-5.4 2.9 1-6.1-4.4-4.3 6.1-.9L12 3Z" />,
    bell: <><path d="M18 9a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9ZM10 21h4" /><circle cx="19" cy="5" r="3" fill="#4353f5" stroke="#fff" strokeWidth="1.5" /></>,
    brain: <><path d="M9.2 5.1a3.2 3.2 0 0 0-5.1 2.6 3.1 3.1 0 0 0 .5 1.7A3.4 3.4 0 0 0 5.8 16a3.1 3.1 0 0 0 3.4 3.2c.7 0 1.3-.2 1.8-.5V5.4a3.2 3.2 0 0 0-1.8-.3Z" /><path d="M14.8 5.1a3.2 3.2 0 0 1 5.1 2.6 3.1 3.1 0 0 1-.5 1.7 3.4 3.4 0 0 1-1.2 6.6 3.1 3.1 0 0 1-3.4 3.2c-.7 0-1.3-.2-1.8-.5V5.4a3.2 3.2 0 0 1 1.8-.3ZM12 8.3h2M12 12h2M12 15.7h2" /></>,
    user: <><circle cx="12" cy="8" r="3.2" /><path d="M5.2 20a6.8 6.8 0 0 1 13.6 0" /></>,
    logout: <><path d="M10 4H5.5A1.5 1.5 0 0 0 4 5.5v13A1.5 1.5 0 0 0 5.5 20H10M14 8l4 4-4 4M18 12H9" /></>,
    search: <><circle cx="10.8" cy="10.8" r="6.3" /><path d="m16 16 4.5 4.5" /></>,
    chevron: <path d="m9 6 6 6-6 6" />,
    download: <><path d="M12 4v10M8 10l4 4 4-4M5 20h14" /></>,
    arrowUp: <path d="m5 15 5-5 3 3 6-7" />,
    arrowDown: <path d="m5 9 5 5 3-3 6 7" />,
    info: <><circle cx="12" cy="12" r="8.5" /><path d="M12 10.5v5M12 7.5v.2" /></>,
    menu: <><path d="M4 7h16M4 12h16M4 17h16" /></>,
  }

  return <svg className="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.55" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}

function MetricCard({ label, value, detail, tone = 'neutral' }: { label: string; value: string; detail?: string; tone?: 'neutral' | 'positive' | 'negative' }) {
  return <article className={`metric-card ${tone}`}><p>{label}</p><strong>{value}</strong>{detail && <span>{detail}</span>}</article>
}

function toneFor(value: number | null | undefined): 'neutral' | 'positive' | 'negative' {
  if (value === null || value === undefined || !Number.isFinite(value)) return 'neutral'
  return value < 0 ? 'negative' : value > 0 ? 'positive' : 'neutral'
}

function PerformanceChart({ range }: { range: TimeRange }) {
  const { i18n, t } = useTranslation()
  const series = performanceSeries[range]
  const labels = series.labels.map((label) => new Intl.DateTimeFormat(i18n.language === 'fr' ? 'fr-FR' : 'en-US', { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(`${label}T00:00:00.000Z`)))
  return (
    <PerformanceLineChart
      ariaLabel={t('portfolio.performanceChart', { range: t(`common.timeRanges.${range}`) })}
      formatValue={value => formatCompactCurrency(value * 1000, i18n.language)}
      labels={labels}
      pointLabel={index => t('portfolio.chartPoint', { label: labels[index], value: formatCompactCurrency(series.values[index] * 1000, i18n.language) })}
      unavailableMessage="businessData.portfolio"
      values={series.values}
    />
  )
}

function AllocationPanel({ positions, cashBalance, totalValue, currency }: { positions: PortfolioPosition[]; cashBalance: number | null; totalValue: number | null; currency: string }) {
  const { i18n, t } = useTranslation()
  if (cashBalance === null || totalValue === null) {
    return <section className="panel allocation-panel"><div className="panel-header"><h2>{t('portfolio.assetAllocation')}</h2></div><UnavailableState message="businessData.portfolio" /></section>
  }
  const invested = positions.reduce((total, position) => total + (position.marketValue ?? position.quantity * position.averagePrice), 0)
  const investedPercent = totalValue > 0 ? Math.min(100, (invested / totalValue) * 100) : 0
  const cashPercent = totalValue > 0 ? Math.max(0, 100 - investedPercent) : 0
  const background = totalValue > 0
    ? `conic-gradient(#4d6ff5 0 ${investedPercent}%, #ff9a63 ${investedPercent}% 100%)`
    : '#e2e8f0'
  return <section className="panel allocation-panel"><div className="panel-header"><h2>{t('portfolio.assetAllocation')}</h2></div><div className="allocation-body"><div className="donut" style={{ background }}><div><strong>{formatCompactCurrency(totalValue, i18n.language, 2, currency)}</strong><span>{t('common.totalValue')}</span></div></div><ul className="allocation-legend"><li><i className="us-stocks" /><span>{t('portfolio.investedCapital')}</span><strong>{formatPercent(investedPercent, i18n.language, 1)}</strong></li><li><i className="cash" /><span>{t('portfolio.cash')}</span><strong>{formatPercent(cashPercent, i18n.language, 1)}</strong></li></ul></div><p className="allocation-caption">{formatCurrency(cashBalance, i18n.language, 2, currency)} {t('portfolio.availableCash').toLowerCase()}</p></section>
}

function SymbolBadge({ position }: { position: PortfolioPosition }) { return <StockLogo symbol={position.symbol} /> }

function PositionsTable({ positions, currency }: { positions: PortfolioPosition[]; currency: string }) {
  const { i18n, t } = useTranslation()
  const [showAll, setShowAll] = useState(true)
  const visiblePositions = showAll ? positions : positions.slice(0, 4)
  const totalMarketValue = positions.length > 0 && positions.every(position => position.marketValue !== null)
    ? positions.reduce((total, position) => total + (position.marketValue ?? 0), 0)
    : null
  return <section className="panel positions-panel"><div className="panel-header positions-header"><h2>{t('portfolio.positions')} <span>({formatNumber(positions.length, undefined, 0)})</span></h2><div className="table-actions"><button type="button" onClick={() => setShowAll((visible) => !visible)}>{showAll ? t('portfolio.showLess') : t('common.viewAll')}</button><button disabled title={t('businessData.unavailable')} type="button"><Icon name="download" /> {t('common.download')}</button></div></div><div className="table-wrap"><table><thead><tr><th>{t('market.columns.symbol')}</th><th>{t('market.columns.company')}</th><th>{t('common.quantity')}</th><th>{t('portfolio.avgPrice')}</th><th>{t('portfolio.currentPrice')}</th><th>{t('portfolio.marketValue')}</th><th>P&amp;L</th><th>P&amp;L %</th><th>{t('portfolio.weight')}</th></tr></thead><tbody>{visiblePositions.length === 0 && <tr><td colSpan={9}><UnavailableState message="businessData.portfolio" /></td></tr>}{visiblePositions.map((position) => <tr key={position.symbol}><td><div className="symbol-cell"><SymbolBadge position={position} /><strong>{position.symbol}</strong></div></td><td>{position.symbol === 'CASH' ? t('portfolio.cash') : position.name}</td><td>{formatNumber(position.quantity, undefined, 8)}</td><td>{formatCurrency(position.averagePrice, i18n.language, 2, currency)}</td><td>{formatCurrency(position.currentPrice, i18n.language, 2, currency)}</td><td>{formatCurrency(position.marketValue, i18n.language, 2, currency)}</td><td className={position.pnl !== null && position.pnl < 0 ? 'negative' : position.pnl !== null ? 'positive' : ''}>{position.pnl === null ? '—' : formatSignedCurrency(position.pnl, i18n.language, 2, currency)}</td><td className={position.pnlPercent !== null && position.pnlPercent < 0 ? 'negative' : position.pnlPercent !== null ? 'positive' : ''}>{position.pnlPercent === null ? '—' : formatSignedPercent(position.pnlPercent)}</td><td>{formatPercent(position.weight, undefined, 1)}</td></tr>)}</tbody><tfoot><tr><td colSpan={5}>{t('common.total')}</td><td>{formatCurrency(totalMarketValue, i18n.language, 2, currency)}</td><td colSpan={3}> </td></tr></tfoot></table></div></section>
}

export default function PortfolioPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('common.navigation.portfolio')} | StockLab`
  }, [i18n.language, t])
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [range, setRange] = useState<TimeRange>('3M')
  const { data, isLoading } = usePortfolioData()
  const positions = data?.positions ?? []
  const currency = data?.currency ?? 'USD'
  const pnlTone = toneFor(data?.pnl)
  const returnTone = toneFor(data?.returnPercent)

  return <div className="portfolio-app stocklab-layout"><Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} /><div className="portfolio-main"><TopBar onMenuOpen={() => setSidebarOpen(true)} title={t('common.navigation.portfolio')} /><main className="portfolio-content" aria-busy={isLoading}><section className="metrics-grid"><MetricCard label={t('portfolio.totalPortfolioValue')} value={formatCurrency(data?.totalValue, i18n.language, 2, currency)} detail={data?.pnl === null || data?.pnl === undefined ? undefined : formatSignedCurrency(data.pnl, i18n.language, 2, currency)} tone={pnlTone} /><MetricCard label={t('portfolio.availableCash')} value={formatCurrency(data?.cashBalance, i18n.language, 2, currency)} /><MetricCard label={t('portfolio.investedCapital')} value={formatCurrency(data?.investedValue, i18n.language, 2, currency)} /><MetricCard label={t('portfolio.totalReturnAllTime')} value={formatSignedPercent(data?.returnPercent, i18n.language)} detail={data?.initialCapital === null || data?.initialCapital === undefined ? undefined : t('portfolio.sinceInitialCapital')} tone={returnTone} /></section><section className="overview-grid"><section className="panel performance-panel"><div className="panel-header"><h2>{t('portfolio.performanceTitle')} <Icon name="info" /></h2></div><div className="range-tabs" role="tablist" aria-label={t('common.performanceTimeRange')}>{(Object.keys(performanceSeries) as TimeRange[]).map((option) => <button key={option} type="button" className={range === option ? 'selected' : ''} aria-selected={range === option} onClick={() => setRange(option)} role="tab">{t(`common.timeRanges.${option}`)}</button>)}</div><PerformanceChart key={range} range={range} /></section><AllocationPanel positions={positions} cashBalance={data?.cashBalance ?? null} totalValue={data?.totalValue ?? null} currency={currency} /></section><PositionsTable positions={positions} currency={currency} /><LogoAttribution /></main></div></div>
}
