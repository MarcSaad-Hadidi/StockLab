import { StockLogo } from '../market/StockLogo'
import { UnavailableState } from '../components/UnavailableState'
import { Sidebar } from '../components/layout/Sidebar'
import { formatTime, formatCurrency, formatPercent, formatSignedCurrency, formatSignedPercent } from '../i18n/formatters'
import { routeFor } from '../navigation/routes'
import { useTranslation } from 'react-i18next'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  aiPerformance,
  metrics,
  positions,
  transactions,
  type IconName,
  type PerformanceRange,
  type Position,
  type Transaction,
  type WatchlistItem,
  watchlist,
} from './dashboardData'
import { getGreetingPeriod } from './dashboardGreeting'
import { startDashboardGreetingTimer } from './dashboardGreetingTimer'

type IconProps = {
  name: IconName
  size?: number
  strokeWidth?: number
  className?: string
}

function Icon({ name, size = 20, strokeWidth = 1.8, className }: IconProps) {
  const common = {
    fill: 'none',
    stroke: 'currentColor',
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    strokeWidth,
  }

  const paths: Record<IconName, ReactNode> = {
    activity: <><path d="M3 12h3l2.2-6 3.6 12 2.2-6H21" {...common} /></>,
    bell: <><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9" {...common} /><path d="M10 21h4" {...common} /></>,
    briefcase: <><rect x="3" y="7" width="18" height="13" rx="2" {...common} /><path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 12h18M10 12v2h4v-2" {...common} /></>,
    chart: <><path d="M4 19V5M4 19h17" {...common} /><path d="m7 15 3-4 3 2 5-7" {...common} /><path d="M16 6h2v2" {...common} /></>,
    'chevron-down': <path d="m6 9 6 6 6-6" {...common} />,
    'chevron-right': <path d="m9 6 6 6-6 6" {...common} />,
    clock: <><circle cx="12" cy="12" r="8.5" {...common} /><path d="M12 7v5l3 2" {...common} /></>,
    close: <><path d="m6 6 12 12M18 6 6 18" {...common} /></>,
    grid: <><rect x="3" y="3" width="7" height="7" rx="1" {...common} /><rect x="14" y="3" width="7" height="7" rx="1" {...common} /><rect x="3" y="14" width="7" height="7" rx="1" {...common} /><rect x="14" y="14" width="7" height="7" rx="1" {...common} /></>,
    menu: <><path d="M4 7h16M4 12h16M4 17h16" {...common} /></>,
    more: <><circle cx="5" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="12" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="19" cy="12" r="1" fill="currentColor" stroke="none" /></>,
    'pie-chart': <><path d="M12 3v9h9" {...common} /><path d="M20.5 15A9 9 0 1 1 9 3.5" {...common} /></>,
    search: <><circle cx="10.8" cy="10.8" r="6.8" {...common} /><path d="m16 16 4.5 4.5" {...common} /></>,
    settings: <><circle cx="12" cy="12" r="3" {...common} /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-1.7 1.7-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5v.2h-2.4v-.2a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1L8 17l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H6.7v-2.4h.2a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9L8 8.6l1.7-1.7.1.1a1.7 1.7 0 0 0 1.9.3 1.7 1.7 0 0 0 1-1.5v-.2h2.4v.2a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1 1.7 1.7-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.2V14h-.2a1.7 1.7 0 0 0-1.5 1Z" {...common} /></>,
    sparkles: <><path d="m12 3 1.2 4.8L18 9l-4.8 1.2L12 15l-1.2-4.8L6 9l4.8-1.2L12 3ZM19 15l.6 2.4L22 18l-2.4.6L19 21l-.6-2.4L16 18l2.4-.6L19 15Z" {...common} /></>,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z" {...common} />,
    'trending-up': <><path d="M3 17 9 11l4 4 8-9" {...common} /><path d="M15 6h6v6" {...common} /></>,
    wallet: <><path d="M4 6.5A2.5 2.5 0 0 1 6.5 4H19a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5.5A2.5 2.5 0 0 1 3 17.5v-9A2 2 0 0 1 5 6.5h14" {...common} /><path d="M21 10h-5a2 2 0 0 0 0 4h5M16.5 12h.01" {...common} /></>,
    x: <><path d="m6 6 12 12M18 6 6 18" {...common} /></>,
  }

  return (
    <svg aria-hidden="true" className={className} height={size} viewBox="0 0 24 24" width={size}>
      {paths[name]}
    </svg>
  )
}

function StockMark({ symbol, size = 'medium' }: { symbol: string; size?: 'small' | 'medium' }) { return <span data-size={size}><StockLogo symbol={symbol} /></span> }

function PerformanceChart() { return <UnavailableState message="businessData.portfolio" /> }

function Sparkline() { return <UnavailableState message="businessData.ai" /> }

function PanelHeading({ title, subtitle, action, id, destination }: { title: string; subtitle?: string; action?: string; id?: string; destination?: string }) {
  return (
    <div className="panel-heading">
      <div>
        <h2 id={id}>{title}</h2>
        {subtitle && <p>{subtitle}</p>}
      </div>
      {action && <button className="text-action" onClick={() => destination && window.location.assign(routeFor(destination))} type="button">{action}<Icon name="chevron-right" size={16} /></button>}
    </div>
  )
}

function MetricCard({ metric }: { metric: (typeof metrics)[number] }) {
  const { t } = useTranslation()
  return (
    <article className="metric-card">
      <div className={`metric-icon metric-icon-${metric.tone}`}><Icon name={metric.icon} size={20} /></div>
      <p>{t(metric.label)}</p>
      <strong>{metric.label === 'dashboard.metrics.return' ? formatPercent(metric.value) : formatCurrency(metric.value)}</strong>
      <span className="metric-change"><Icon name="trending-up" size={13} /> {formatSignedPercent(metric.change)} <em>{t(metric.detail)}</em></span>
    </article>
  )
}

function AllocationBar({ allocation }: { allocation: number }) {
  const { t } = useTranslation()
  return <span aria-label={t('dashboard.allocation', { allocation: formatPercent(allocation, undefined, 1) })} className="allocation-bar"><i style={{ width: `${Math.min(allocation * 3.2, 100)}%` }} /></span>
}

function PositionRow({ position }: { position: Position }) {
  const { t } = useTranslation()
  return (
    <tr>
      <td><div className="asset-cell"><StockMark size="small" symbol={position.symbol} /><span><strong>{position.symbol}</strong><small>{position.company}</small></span></div></td>
      <td>{t('dashboard.shares', { count: position.shares })}</td>
      <td><strong>{formatCurrency(position.value)}</strong><small className="muted-line">{formatCurrency(position.price)}</small></td>
      <td><div className="allocation-cell"><AllocationBar allocation={position.allocation} /><small>{formatPercent(position.allocation, undefined, 1)}</small></div></td>
      <td><span className={`change-pill ${position.tone}`}>{formatSignedPercent(position.change)}</span></td>
    </tr>
  )
}

function WatchlistRow({ item }: { item: WatchlistItem }) {
  const { t } = useTranslation()
  return (
    <li className="watchlist-row">
      <StockMark size="small" symbol={item.symbol} />
      <div className="watchlist-name"><strong>{item.symbol}</strong><small>{item.company}</small></div>
      <div className="watchlist-price"><strong>{formatCurrency(item.price)}</strong><span className={item.tone}>{formatSignedPercent(item.change)}</span></div>
      <button aria-label={t('dashboard.openDetails', { symbol: item.symbol })} className="icon-button row-more" onClick={() => undefined} type="button"><Icon name="more" size={18} /></button>
    </li>
  )
}

function TransactionRow({ transaction }: { transaction: Transaction }) {
  const { i18n, t } = useTranslation()
  const localizedTime = transaction.time ? formatTime(transaction.time, i18n.language) : ''
  return (
    <li className="transaction-row">
      <StockMark size="small" symbol={transaction.symbol} />
      <div className="transaction-name"><strong>{transaction.symbol}</strong><small>{transaction.company}</small></div>
      <div className={`transaction-type ${transaction.type.toLowerCase()}`}><span className="transaction-dot" />{t(`common.${transaction.type.toLowerCase()}`)}</div>
      <div className="transaction-amount"><strong>{formatCurrency(transaction.amount)}</strong><small>{t('dashboard.shares', { count: transaction.shares })}</small></div>
      <small className="transaction-time">{t(transaction.timeKey, { time: localizedTime })}</small>
    </li>
  )
}

const ranges: PerformanceRange[] = ['1D', '1W', '1M', '3M', '1Y', 'YTD', 'ALL']

export function DashboardPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `StockLab — ${t('common.navigation.dashboard')}`
  }, [i18n.language, t])
  const [range, setRange] = useState<PerformanceRange>('1M')
  const [query, setQuery] = useState('')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [toastKey, setToastKey] = useState('')
  const dashboardUserName = ''
  const [greetingPeriod, setGreetingPeriod] = useState(() => getGreetingPeriod(new Date()))

  useEffect(() => {
    return startDashboardGreetingTimer(() => setGreetingPeriod(getGreetingPeriod(new Date())), dashboardUserName)
  }, [])

  const greeting = t(`businessData.greeting.${greetingPeriod}`)

  const filteredWatchlist = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase()
    if (!normalizedQuery) return watchlist
    return watchlist.filter((item) => `${item.symbol} ${item.company}`.toLowerCase().includes(normalizedQuery))
  }, [query])

  const showToast = (key: string) => {
    setToastKey(key)
    window.setTimeout(() => setToastKey(''), 2200)
  }

  return (
    <div className="dashboard-page stocklab-layout">
      <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      <main className="dashboard-main">
        <header className="dashboard-topbar">
          <button aria-label={t('common.openNavigation')} className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={22} /></button>
          <div className="breadcrumb"><span>{t('common.workspace')}</span><Icon name="chevron-right" size={14} /><strong>{t('common.navigation.dashboard')}</strong></div>
          <div className="topbar-actions"><label className="global-search"><Icon name="search" size={17} /><input aria-label={t('common.searchStocks')} onChange={(event) => setQuery(event.target.value)} placeholder={t('dashboard.searchPlaceholder')} value={query} /></label><button aria-label={t('common.notifications')} className="icon-button notification-button" onClick={() => showToast('common.notificationsCaughtUp')} type="button"><Icon name="bell" size={19} /><i /></button><span className="topbar-avatar">GA</span></div>
        </header>

        <div className="dashboard-content">
          <section className="welcome-row"><div><p className="eyebrow">{new Intl.DateTimeFormat(i18n.language, { dateStyle: 'full' }).format(new Date())}</p><h1>{greeting} <span>👋</span></h1><p className="welcome-copy">{t('dashboard.welcome')}</p></div><button className="primary-button" disabled title={t('businessData.unavailable')} type="button"><span>+</span> {t('common.addInvestment')}</button></section>

          <section aria-label={t('dashboard.portfolioSummary')} className="metrics-grid">{metrics.map((metric) => <MetricCard key={metric.label} metric={metric} />)}</section>

          <div className="dashboard-grid dashboard-grid-top">
            <section aria-labelledby="performance-title" className="panel performance-panel"><PanelHeading id="performance-title" subtitle={t('dashboard.performanceSubtitle')} title={t('dashboard.performanceTitle')} /><div className="range-tabs" role="tablist" aria-label={t('common.performanceTimeRange')}>{ranges.map((item) => <button aria-selected={range === item} className={range === item ? 'selected' : ''} key={item} onClick={() => setRange(item)} role="tab" type="button">{t(`common.timeRanges.${item}`)}</button>)}</div><PerformanceChart key={range} /></section>
            <section aria-labelledby="watchlist-title" className="panel watchlist-panel"><PanelHeading action={t('common.viewAll')} destination="watchlist" id="watchlist-title" subtitle={t('dashboard.watchlistSubtitle')} title={t('common.navigation.watchlist')} /><div className="watchlist-filter"><Icon name="search" size={15} /><input aria-label={t('dashboard.filterWatchlist')} onChange={(event) => setQuery(event.target.value)} placeholder={t('dashboard.filterPlaceholder')} value={query} /></div>{filteredWatchlist.length > 0 ? <ul className="watchlist-list">{filteredWatchlist.map((item) => <WatchlistRow item={item} key={item.symbol} />)}</ul> : <div className="empty-state">{t('businessData.watchlist')}</div>}<button className="add-watchlist" onClick={() => showToast('businessData.unavailable')} type="button"><span>+</span> {t('dashboard.addToWatchlist')}</button></section>
          </div>

          <div className="dashboard-grid dashboard-grid-bottom">
            <section aria-labelledby="positions-title" className="panel positions-panel"><PanelHeading action={t('dashboard.viewPortfolio')} destination="portfolio" id="positions-title" subtitle={t('dashboard.positionsSubtitle')} title={t('dashboard.keyPositions')} /><div className="table-scroll"><table><thead><tr><th>{t('common.asset')}</th><th>{t('dashboard.holdings')}</th><th>{t('common.value')}</th><th>{t('common.allocation')}</th><th>{t('dashboard.today')}</th></tr></thead><tbody>{positions.length === 0 && <tr><td colSpan={5}><UnavailableState message="businessData.portfolio" /></td></tr>}{positions.map((position) => <PositionRow key={position.symbol} position={position} />)}</tbody></table></div></section>
            <section aria-labelledby="transactions-title" className="panel transactions-panel"><PanelHeading action={t('common.viewAll')} destination="transactions" id="transactions-title" subtitle={t('dashboard.transactionsSubtitle')} title={t('dashboard.recentTransactions')} /><ul className="transaction-list">{transactions.length === 0 && <li><UnavailableState message="businessData.transactions" /></li>}{transactions.map((transaction) => <TransactionRow key={`${transaction.symbol}-${transaction.timeKey}`} transaction={transaction} />)}</ul></section>
          </div>

          <section aria-labelledby="ai-trader-title" className="panel ai-panel"><div className="ai-heading"><div className="ai-title"><span className="ai-badge"><Icon name="sparkles" size={18} /></span><div><h2 id="ai-trader-title">{t('common.navigation.aiTrader')}</h2><p>{t('dashboard.aiSubtitle')}</p></div><span className="status-badge"><i /> {t('businessData.unavailable')}</span></div><button className="text-action" onClick={() => window.location.assign(routeFor('ai-trader'))} type="button">{t('dashboard.openAiTrader')} <Icon name="chevron-right" size={16} /></button></div><div className="ai-content"><div className="ai-stat ai-stat-primary"><span>{t('dashboard.aiReturn')}</span><strong>{formatSignedPercent(aiPerformance.return)}</strong><small><Icon name="trending-up" size={13} /> {t('businessData.unavailable')}</small></div><div className="ai-stat"><span>{t('dashboard.netPnl')}</span><strong>{formatSignedCurrency(aiPerformance.pnl)}</strong><small>{t('businessData.unavailable')}</small></div><div className="ai-stat"><span>{t('dashboard.winRate')}</span><strong>{formatPercent(aiPerformance.winRate, undefined, 1)}</strong><small>{t('dashboard.tradesExecuted', { count: aiPerformance.trades })}</small></div><div className="ai-chart-wrap"><span>{t('dashboard.sevenDayPerformance')}</span><Sparkline /><div className="ai-chart-labels"><small>{t('common.days.mon')}</small><small>{t('dashboard.today')}</small></div></div></div></section>
          <p className="simulation-note"><span><Icon name="activity" size={14} /> {t('businessData.unavailable')}</span> {t('businessData.backendPending')}</p>
        </div>
      </main>
      <div aria-live="polite" className={`toast ${toastKey ? 'visible' : ''}`}>{toastKey ? t(toastKey) : ''}</div>
    </div>
  )
}
