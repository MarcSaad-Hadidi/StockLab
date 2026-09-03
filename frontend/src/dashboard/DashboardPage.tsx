import { useMemo, useState, type ReactNode } from 'react'
import {
  aiPerformance,
  metrics,
  navigation,
  performanceSeries,
  positions,
  secondaryNavigation,
  transactions,
  type IconName,
  type PerformanceRange,
  type Position,
  type Transaction,
  type WatchlistItem,
  watchlist,
} from './dashboardData'

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

function StockMark({ symbol, size = 'medium' }: { symbol: string; size?: 'small' | 'medium' }) {
  return (
    <span aria-hidden="true" className={`stock-mark stock-mark-${symbol.toLowerCase()} stock-mark-${size}`}>
      {symbol === 'MSFT' ? <><i /><i /><i /><i /></> : symbol.slice(0, 1)}
    </span>
  )
}

function formatCurrency(value: number) {
  return `$${value.toFixed(2)}K`
}

function PerformanceChart({ range }: { range: PerformanceRange }) {
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null)
  const [focusedIndex, setFocusedIndex] = useState<number | null>(null)
  const [pinnedIndex, setPinnedIndex] = useState<number | null>(null)
  const series = performanceSeries[range]
  const width = 760
  const height = 250
  const padding = { top: 18, right: 14, bottom: 34, left: 14 }
  const min = Math.min(...series.values) - 0.7
  const max = Math.max(...series.values) + 0.7
  const usableWidth = width - padding.left - padding.right
  const usableHeight = height - padding.top - padding.bottom
  const points = series.values.map((value, index) => {
    const x = padding.left + (index / (series.values.length - 1)) * usableWidth
    const y = padding.top + ((max - value) / (max - min)) * usableHeight
    return { x, y, value }
  })
  const linePath = points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x} ${point.y}`).join(' ')
  const areaPath = `${linePath} L ${points.at(-1)?.x ?? width} ${height - padding.bottom} L ${points[0].x} ${height - padding.bottom} Z`
  const yTicks = [0, 1, 2, 3]
  const activeIndex = hoveredIndex ?? focusedIndex ?? pinnedIndex
  const activePoint = activeIndex === null ? null : points[activeIndex]
  const activeValue = activeIndex === null ? null : series.values[activeIndex]
  const pointHitRadius = 44
  const tooltipWidth = 134
  const tooltipHeight = 52
  const tooltipX = activePoint ? Math.min(Math.max(activePoint.x - tooltipWidth / 2, padding.left), width - padding.right - tooltipWidth) : 0
  const tooltipY = activePoint
    ? Math.min(Math.max(activePoint.y < tooltipHeight + 24 ? activePoint.y + 17 : activePoint.y - tooltipHeight - 17, 8), height - tooltipHeight - 8)
    : 0

  return (
    <div className="chart-wrap">
      <div className="chart-summary">
        <div>
          <span className="chart-eyebrow">Portfolio value</span>
          <strong>{formatCurrency(series.values.at(-1) ?? 0)}</strong>
        </div>
        <div className="chart-change">
          <span>{series.change}</span>
          <small>{series.changeLabel}</small>
        </div>
      </div>
      <svg aria-label={`Portfolio performance chart for ${range}`} className="performance-chart" key={range} role="img" viewBox={`0 0 ${width} ${height}`}>
        <defs>
          <linearGradient id={`portfolio-fill-${range}`} x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" stopColor="#2f7bf0" stopOpacity=".22" />
            <stop offset="100%" stopColor="#2f7bf0" stopOpacity="0" />
          </linearGradient>
        </defs>
        {yTicks.map((tick) => {
          const y = padding.top + (tick / (yTicks.length - 1)) * usableHeight
          const value = max - (tick / (yTicks.length - 1)) * (max - min)
          return (
            <g key={tick}>
              <line className="chart-grid-line" x1={padding.left} x2={width - padding.right} y1={y} y2={y} />
              <text className="chart-y-label" textAnchor="end" x={width - padding.right} y={y - 7}>{formatCurrency(value)}</text>
            </g>
          )
        })}
        <path className="chart-area" d={areaPath} fill={`url(#portfolio-fill-${range})`} />
        <path className="chart-line" d={linePath} />
        {activePoint && <line className="chart-crosshair" x1={activePoint.x} x2={activePoint.x} y1={padding.top} y2={height - padding.bottom} />}
        {points.map((point, index) => (
          <g
            aria-label={`${series.labels[index]}: ${formatCurrency(point.value)} portfolio value`}
            aria-pressed={pinnedIndex === index}
            className={`chart-point-group ${activeIndex === index ? 'active' : ''}`}
            key={`${point.x}-${index}`}
            onBlur={() => setFocusedIndex(null)}
            onClick={() => setPinnedIndex((current) => current === index ? null : index)}
            onFocus={() => setFocusedIndex(index)}
            onKeyDown={(event) => {
              if (event.key === 'Escape') setPinnedIndex(null)
              if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault()
                setPinnedIndex((current) => current === index ? null : index)
              }
            }}
            onPointerEnter={() => setHoveredIndex(index)}
            onPointerLeave={() => setHoveredIndex(null)}
            role="button"
            tabIndex={0}
          >
            <circle className="chart-point-hit" cx={point.x} cy={point.y} r={pointHitRadius} />
            <circle className="chart-point" cx={point.x} cy={point.y} key={`${point.x}-${index}`} r={index === points.length - 1 ? 4 : 2.5} style={{ animationDelay: `${index * 45}ms` }} />
          </g>
        ))}
        {activePoint && activeIndex !== null && activeValue !== null && <g className="chart-tooltip" pointerEvents="none" transform={`translate(${tooltipX} ${tooltipY})`}>
          <rect height={tooltipHeight} rx="7" width={tooltipWidth} />
          <text className="chart-tooltip-label" x="10" y="18">{series.labels[activeIndex]}</text>
          <text className="chart-tooltip-value" x="10" y="38">{formatCurrency(activeValue)}</text>
        </g>}
        {series.labels.map((label, index) => {
          const point = points[index]
          return <text className="chart-x-label" key={label} textAnchor={index === 0 ? 'start' : index === series.labels.length - 1 ? 'end' : 'middle'} x={point.x} y={height - 7}>{label}</text>
        })}
      </svg>
      <span aria-live="polite" className="chart-tooltip-announcement">{activeIndex !== null && activeValue !== null ? `${series.labels[activeIndex]}: ${formatCurrency(activeValue)}` : ''}</span>
    </div>
  )
}

function Sparkline() {
  const width = 178
  const height = 56
  const min = Math.min(...aiPerformance.values)
  const max = Math.max(...aiPerformance.values)
  const points = aiPerformance.values.map((value, index) => {
    const x = (index / (aiPerformance.values.length - 1)) * width
    const y = height - ((value - min + 1) / (max - min + 2)) * height
    return `${x},${y}`
  })
  return (
    <svg aria-label="AI Trader performance sparkline" className="ai-sparkline" role="img" viewBox={`0 0 ${width} ${height}`}>
      <defs>
        <linearGradient id="ai-fill" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="#8b6df6" stopOpacity=".26" />
          <stop offset="100%" stopColor="#8b6df6" stopOpacity="0" />
        </linearGradient>
      </defs>
      <polyline className="ai-sparkline-area" fill="url(#ai-fill)" points={`0,${height} ${points.join(' ')} ${width},${height}`} stroke="none" />
      <polyline className="ai-sparkline-line" fill="none" points={points.join(' ')} stroke="#896df1" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.5" />
      <circle cx={width} cy={Number(points.at(-1)?.split(',')[1])} fill="#896df1" r="3.5" />
    </svg>
  )
}

function PanelHeading({ title, subtitle, action, id }: { title: string; subtitle?: string; action?: string; id?: string }) {
  return (
    <div className="panel-heading">
      <div>
        <h2 id={id}>{title}</h2>
        {subtitle && <p>{subtitle}</p>}
      </div>
      {action && <button className="text-action" type="button">{action}<Icon name="chevron-right" size={16} /></button>}
    </div>
  )
}

function MetricCard({ metric }: { metric: (typeof metrics)[number] }) {
  return (
    <article className="metric-card">
      <div className={`metric-icon metric-icon-${metric.tone}`}><Icon name={metric.icon} size={20} /></div>
      <p>{metric.label}</p>
      <strong>{metric.value}</strong>
      <span className="metric-change"><Icon name="trending-up" size={13} /> {metric.change} <em>{metric.detail}</em></span>
    </article>
  )
}

function AllocationBar({ allocation }: { allocation: number }) {
  return <span aria-label={`${allocation}% allocation`} className="allocation-bar"><i style={{ width: `${Math.min(allocation * 3.2, 100)}%` }} /></span>
}

function PositionRow({ position }: { position: Position }) {
  return (
    <tr>
      <td><div className="asset-cell"><StockMark size="small" symbol={position.symbol} /><span><strong>{position.symbol}</strong><small>{position.company}</small></span></div></td>
      <td>{position.shares}</td>
      <td><strong>{position.value}</strong><small className="muted-line">{position.price}</small></td>
      <td><div className="allocation-cell"><AllocationBar allocation={position.allocation} /><small>{position.allocation}%</small></div></td>
      <td><span className={`change-pill ${position.tone}`}>{position.change}</span></td>
    </tr>
  )
}

function WatchlistRow({ item }: { item: WatchlistItem }) {
  return (
    <li className="watchlist-row">
      <StockMark size="small" symbol={item.symbol} />
      <div className="watchlist-name"><strong>{item.symbol}</strong><small>{item.company}</small></div>
      <div className="watchlist-price"><strong>{item.price}</strong><span className={item.tone}>{item.change}</span></div>
      <button aria-label={`Open ${item.symbol} details`} className="icon-button row-more" onClick={() => undefined} type="button"><Icon name="more" size={18} /></button>
    </li>
  )
}

function TransactionRow({ transaction }: { transaction: Transaction }) {
  return (
    <li className="transaction-row">
      <StockMark size="small" symbol={transaction.symbol} />
      <div className="transaction-name"><strong>{transaction.symbol}</strong><small>{transaction.company}</small></div>
      <div className={`transaction-type ${transaction.type.toLowerCase()}`}><span className="transaction-dot" />{transaction.type}</div>
      <div className="transaction-amount"><strong>{transaction.amount}</strong><small>{transaction.shares}</small></div>
      <small className="transaction-time">{transaction.time}</small>
    </li>
  )
}

const ranges: PerformanceRange[] = ['1D', '1W', '1M', '3M', '1Y', 'YTD', 'ALL']

export function DashboardPage() {
  const [range, setRange] = useState<PerformanceRange>('1M')
  const [query, setQuery] = useState('')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [toast, setToast] = useState('')
  const [activeNav, setActiveNav] = useState('Dashboard')

  const filteredWatchlist = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase()
    if (!normalizedQuery) return watchlist
    return watchlist.filter((item) => `${item.symbol} ${item.company}`.toLowerCase().includes(normalizedQuery))
  }, [query])

  const showToast = (message: string) => {
    setToast(message)
    window.setTimeout(() => setToast(''), 2200)
  }

  const handleNavClick = (label: string) => {
    setActiveNav(label)
    setSidebarOpen(false)
    if (label !== 'Dashboard') showToast(`${label} view is coming soon.`)
  }

  return (
    <div className={`dashboard-page ${sidebarOpen ? 'sidebar-open' : ''}`}>
      <button aria-label="Close navigation" className="sidebar-backdrop" onClick={() => setSidebarOpen(false)} type="button" />
      <aside className="dashboard-sidebar">
        <div className="brand"><span className="brand-mark"><i /><i /><i /></span><span>Stock<span>Lab</span></span></div>
        <div className="workspace-switcher"><span className="workspace-avatar">GH</span><span><strong>Ghaith's portfolio</strong><small>Personal account</small></span><Icon name="chevron-down" size={15} /></div>
        <nav aria-label="Primary navigation" className="sidebar-nav">
          <span className="nav-label">Overview</span>
          {navigation.map((item) => (
            <button aria-current={activeNav === item.label ? 'page' : undefined} className={`nav-item ${activeNav === item.label ? 'active' : ''}`} key={item.label} onClick={() => handleNavClick(item.label)} type="button">
              <Icon name={item.icon} size={18} /><span>{item.label}</span>
              {item.label === 'AI Trader' && <span className="new-badge">New</span>}
            </button>
          ))}
          <span className="nav-label nav-label-spaced">Manage</span>
          {secondaryNavigation.map((item) => <button className="nav-item" key={item.label} onClick={() => handleNavClick(item.label)} type="button"><Icon name={item.icon} size={18} /><span>{item.label}</span></button>)}
        </nav>
        <div className="sidebar-footer"><div className="help-card"><span className="help-icon"><Icon name="sparkles" size={17} /></span><span><strong>Need a hand?</strong><small>Explore StockLab tips</small></span><Icon name="chevron-right" size={16} /></div><div className="user-card"><span className="user-avatar">GA</span><span><strong>Ghaith Alali</strong><small>Free plan</small></span><button aria-label="More profile options" className="icon-button" type="button"><Icon name="more" size={18} /></button></div></div>
      </aside>

      <main className="dashboard-main">
        <header className="dashboard-topbar">
          <button aria-label="Open navigation" className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={22} /></button>
          <div className="breadcrumb"><span>Workspace</span><Icon name="chevron-right" size={14} /><strong>Dashboard</strong></div>
          <div className="topbar-actions"><label className="global-search"><Icon name="search" size={17} /><input aria-label="Search stocks" onChange={(event) => setQuery(event.target.value)} placeholder="Search stocks..." value={query} /></label><button aria-label="Notifications" className="icon-button notification-button" onClick={() => showToast('You are all caught up.')} type="button"><Icon name="bell" size={19} /><i /></button><span className="topbar-avatar">GA</span></div>
        </header>

        <div className="dashboard-content">
          <section className="welcome-row"><div><p className="eyebrow">Monday, June 1, 2026</p><h1>Good morning, Ghaith <span>✦</span></h1><p className="welcome-copy">Here’s what’s happening with your portfolio today.</p></div><button className="primary-button" onClick={() => showToast('New investment flow opened.')} type="button"><span>+</span> Add investment</button></section>

          <section aria-label="Portfolio summary" className="metrics-grid">{metrics.map((metric) => <MetricCard key={metric.label} metric={metric} />)}</section>

          <div className="dashboard-grid dashboard-grid-top">
            <section aria-labelledby="performance-title" className="panel performance-panel"><PanelHeading id="performance-title" subtitle="Your portfolio value over time" title="Portfolio performance" /><div className="range-tabs" role="tablist" aria-label="Performance time range">{ranges.map((item) => <button aria-selected={range === item} className={range === item ? 'selected' : ''} key={item} onClick={() => setRange(item)} role="tab" type="button">{item}</button>)}</div><PerformanceChart key={range} range={range} /></section>
            <section aria-labelledby="watchlist-title" className="panel watchlist-panel"><PanelHeading action="View all" id="watchlist-title" subtitle="Stocks you’re keeping an eye on" title="Watchlist" /><div className="watchlist-filter"><Icon name="search" size={15} /><input aria-label="Filter watchlist" onChange={(event) => setQuery(event.target.value)} placeholder="Filter by symbol or name" value={query} /></div>{filteredWatchlist.length > 0 ? <ul className="watchlist-list">{filteredWatchlist.map((item) => <WatchlistRow item={item} key={item.symbol} />)}</ul> : <div className="empty-state">No stocks match “{query}”.</div>}<button className="add-watchlist" onClick={() => showToast('Search the market to add a stock.')} type="button"><span>+</span> Add to watchlist</button></section>
          </div>

          <div className="dashboard-grid dashboard-grid-bottom">
            <section aria-labelledby="positions-title" className="panel positions-panel"><PanelHeading action="View portfolio" id="positions-title" subtitle="Your biggest holdings by value" title="Key positions" /><div className="table-scroll"><table><thead><tr><th>Asset</th><th>Holdings</th><th>Value</th><th>Allocation</th><th>Today</th></tr></thead><tbody>{positions.map((position) => <PositionRow key={position.symbol} position={position} />)}</tbody></table></div></section>
            <section aria-labelledby="transactions-title" className="panel transactions-panel"><PanelHeading action="View all" id="transactions-title" subtitle="Your latest activity" title="Recent transactions" /><ul className="transaction-list">{transactions.map((transaction) => <TransactionRow key={`${transaction.symbol}-${transaction.time}`} transaction={transaction} />)}</ul></section>
          </div>

          <section aria-labelledby="ai-trader-title" className="panel ai-panel"><div className="ai-heading"><div className="ai-title"><span className="ai-badge"><Icon name="sparkles" size={18} /></span><div><h2 id="ai-trader-title">AI Trader</h2><p>Autonomous portfolio insights and signals</p></div><span className="status-badge"><i /> Live</span></div><button className="text-action" onClick={() => showToast('AI Trader details opened.')} type="button">Open AI Trader <Icon name="chevron-right" size={16} /></button></div><div className="ai-content"><div className="ai-stat ai-stat-primary"><span>AI return</span><strong>{aiPerformance.return}</strong><small><Icon name="trending-up" size={13} /> outperforming the market</small></div><div className="ai-stat"><span>Net P&L</span><strong>{aiPerformance.pnl}</strong><small>Since activation</small></div><div className="ai-stat"><span>Win rate</span><strong>{aiPerformance.winRate}</strong><small>{aiPerformance.trades} trades executed</small></div><div className="ai-chart-wrap"><span>7-day performance</span><Sparkline /><div className="ai-chart-labels"><small>Mon</small><small>Today</small></div></div></div></section>
          <p className="simulation-note"><span><Icon name="activity" size={14} /> Simulated data</span> Connect a brokerage account to see your live portfolio.</p>
        </div>
      </main>
      <div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast}</div>
    </div>
  )
}
