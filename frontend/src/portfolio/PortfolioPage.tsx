import { isCurrentPage, routeFor } from '../navigation/routes'
import { useState } from 'react'
import type { ReactNode } from 'react'
import { performanceSeries, positions } from './portfolioData'
import type { Position } from './portfolioData'
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

function Brand() {
  return <div className="brand" aria-label="StockLab"><span className="brand-mark" aria-hidden="true"><i /><i /><i /></span><span>Stock<span>Lab</span></span></div>
}

function MetricCard({ label, value, detail, tone = 'neutral' }: { label: string; value: string; detail?: string; tone?: 'neutral' | 'positive' }) {
  return <article className={`metric-card ${tone}`}><p>{label}</p><strong>{value}</strong>{detail && <span>{detail}</span>}</article>
}

function PerformanceChart({ range }: { range: TimeRange }) {
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
  const points = series.values.map((value, index) => ({
    x: padding.left + (index / Math.max(series.values.length - 1, 1)) * usableWidth,
    y: padding.top + ((max - value) / Math.max(max - min, 1)) * usableHeight,
  }))
  const linePath = points.map((point, index) => (index === 0 ? 'M' : 'L') + ' ' + point.x + ' ' + point.y).join(' ')
  const baseline = height - padding.bottom
  const areaPath = linePath + ' L ' + (points.at(-1)?.x ?? width) + ' ' + baseline + ' L ' + (points[0]?.x ?? padding.left) + ' ' + baseline + ' Z'
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
  const formatCurrency = (value: number) => '$' + value.toFixed(2) + 'K'

  return <div className="chart-wrap"><div className="chart-summary"><div><span className="chart-eyebrow">Portfolio value</span><strong>{formatCurrency(series.values.at(-1) ?? 0)}</strong></div><div className="chart-change"><span>{series.change}</span><small>{series.changeLabel}</small></div></div><svg aria-label={'Portfolio performance chart for ' + range} className="performance-chart" key={range} role="img" viewBox={'0 0 ' + width + ' ' + height}><defs><linearGradient id={'portfolio-fill-' + range} x1="0" x2="0" y1="0" y2="1"><stop offset="0%" stopColor="#2f7bf0" stopOpacity=".22" /><stop offset="100%" stopColor="#2f7bf0" stopOpacity="0" /></linearGradient></defs>{yTicks.map((tick) => { const y = padding.top + (tick / (yTicks.length - 1)) * usableHeight; const value = max - (tick / (yTicks.length - 1)) * (max - min); return <g key={'y-tick-' + tick}><line className="chart-grid-line" x1={padding.left} x2={width - padding.right} y1={y} y2={y} /><text className="chart-y-label" textAnchor="end" x={width - padding.right} y={y - 7}>{formatCurrency(value)}</text></g> })}<path className="chart-area" d={areaPath} fill={'url(#portfolio-fill-' + range + ')'} /><path className="chart-line" d={linePath} />{points.map((point, index) => <g aria-label={series.labels[index] + ': ' + formatCurrency(series.values[index]) + ' portfolio value'} aria-pressed={pinnedIndex === index} className={'chart-point-group ' + (activeIndex === index ? 'active' : '')} key={point.x + '-' + index} onBlur={() => setFocusedIndex(null)} onClick={() => setPinnedIndex((current) => current === index ? null : index)} onFocus={() => setFocusedIndex(index)} onKeyDown={(event) => { if (event.key === 'Escape') setPinnedIndex(null); if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); setPinnedIndex((current) => current === index ? null : index) } }} onPointerEnter={() => setHoveredIndex(index)} onPointerLeave={() => setHoveredIndex(null)} role="button" tabIndex={0}><circle className="chart-point-hit" cx={point.x} cy={point.y} r={pointHitRadius} /><circle className="chart-point" cx={point.x} cy={point.y} r={index === points.length - 1 ? 4 : 2.5} style={{ animationDelay: (index * 45) + 'ms' }} /></g>)}{activePoint && <line className="chart-crosshair" x1={activePoint.x} x2={activePoint.x} y1={padding.top} y2={baseline} />}{activePoint && activeIndex !== null && activeValue !== null && <g className="chart-tooltip" pointerEvents="none" transform={'translate(' + tooltipX + ' ' + tooltipY + ')'}><rect height={tooltipHeight} rx="7" width={tooltipWidth} /><text className="chart-tooltip-label" x="10" y="18">{series.labels[activeIndex]}</text><text className="chart-tooltip-value" x="10" y="38">{formatCurrency(activeValue)}</text></g>}{series.labels.map((label, index) => <text className="chart-x-label" key={label + '-' + index} textAnchor={index === 0 ? 'start' : index === series.labels.length - 1 ? 'end' : 'middle'} x={points[index].x} y={height - 7}>{label}</text>)}</svg><span className="chart-tooltip-announcement" aria-live="polite">{activeIndex !== null && activeValue !== null ? series.labels[activeIndex] + ': ' + formatCurrency(activeValue) : ''}</span></div>
}

function AllocationPanel() {
  return <section className="panel allocation-panel"><div className="panel-header"><h2>Asset Allocation</h2></div><div className="allocation-body"><div className="donut"><div><strong>$128.55K</strong><span>Total Value</span></div></div><ul className="allocation-legend"><li><i className="us-stocks" /><span>US Stocks</span><strong>68.4%</strong></li><li><i className="etfs" /><span>ETFs</span><strong>16.7%</strong></li><li><i className="crypto" /><span>Crypto</span><strong>6.3%</strong></li><li><i className="cash" /><span>Cash</span><strong>8.6%</strong></li></ul></div><a className="allocation-link" href="#allocation">View Full Allocation <Icon name="chevron" /></a></section>
}

function SymbolBadge({ position }: { position: Position }) {
  return <span className={`symbol-badge ${position.tone}`}>{position.symbol === 'CASH' ? '$' : position.symbol.slice(0, 1)}</span>
}

function formatMoney(value: number | null) {
  if (value === null) return '—'
  return `$${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function PositionsTable() {
  const [showAll, setShowAll] = useState(true)
  const visiblePositions = showAll ? positions : positions.slice(0, 4)
  return <section className="panel positions-panel"><div className="panel-header positions-header"><h2>Positions <span>(7)</span></h2><div className="table-actions"><button type="button" onClick={() => setShowAll((visible) => !visible)}>{showAll ? 'Show Less' : 'View All'}</button><button type="button"><Icon name="download" /> Download</button></div></div><div className="table-wrap"><table><thead><tr><th>Symbol</th><th>Name</th><th>Quantity</th><th>Avg. Price</th><th>Current Price</th><th>Market Value</th><th>P&amp;L</th><th>P&amp;L %</th><th>Weight</th></tr></thead><tbody>{visiblePositions.map((position) => <tr key={position.symbol}><td><div className="symbol-cell"><SymbolBadge position={position} /><strong>{position.symbol}</strong></div></td><td>{position.name}</td><td>{position.quantity ?? '—'}</td><td>{formatMoney(position.averagePrice)}</td><td>{formatMoney(position.currentPrice)}</td><td>{formatMoney(position.marketValue)}</td><td className={position.pnl !== null && position.pnl < 0 ? 'negative' : position.pnl !== null ? 'positive' : ''}>{formatMoney(position.pnl)}</td><td className={position.pnlPercent !== null && position.pnlPercent < 0 ? 'negative' : position.pnlPercent !== null ? 'positive' : ''}>{position.pnlPercent === null ? '—' : `${position.pnlPercent > 0 ? '+' : ''}${position.pnlPercent.toFixed(2)}%`}</td><td>{position.weight.toFixed(1)}%</td></tr>)}</tbody><tfoot><tr><td colSpan={5}>Total</td><td>$116,117.14</td><td className="positive">+$7,812.45</td><td className="positive">+6.47%</td><td>100%</td></tr></tfoot></table></div></section>
}

const navSections = [
  { label: '', items: [{ icon: 'grid' as IconName, text: 'Dashboard' }, { icon: 'globe' as IconName, text: 'Market' }, { icon: 'briefcase' as IconName, text: 'Portfolio', active: true }, { icon: 'sliders' as IconName, text: 'Transactions' }, { icon: 'star' as IconName, text: 'Watchlist' }, { icon: 'bell' as IconName, text: 'Alerts' }] },
  { label: 'AI', items: [{ icon: 'brain' as IconName, text: 'AI Trader' }] },
  { label: 'ACCOUNT', items: [{ icon: 'user' as IconName, text: 'Profile' }, { icon: 'logout' as IconName, text: 'Logout' }] },
]

function Sidebar() {
  return <aside className="portfolio-sidebar"><Brand /><nav>{navSections.map((section) => <div className="nav-section" key={section.label || 'main'}>{section.label && <p className="nav-section-label">{section.label}</p>}{section.items.map((item) => <a className={`nav-item ${isCurrentPage(item.text, window.location.pathname) ? 'active' : ''}`} aria-current={isCurrentPage(item.text, window.location.pathname) ? 'page' : undefined} href={routeFor(item.text)} key={item.text}><Icon name={item.icon} /><span>{item.text}</span></a>)}</div>)}</nav><div className="sidebar-footer"><span className="market-status" /><span>Market open</span></div></aside>
}

export default function PortfolioPage() {
  const [range, setRange] = useState<TimeRange>('3M')

  return <div className="portfolio-app"><Sidebar /><div className="portfolio-main"><header className="portfolio-topbar"><div className="topbar-title"><button className="mobile-menu" type="button" aria-label="Open navigation"><Icon name="menu" /></button><h1>Portfolio</h1></div><div className="topbar-actions"><label className="search-box"><Icon name="search" /><input type="search" placeholder="Search stocks, ETFs, news..." aria-label="Search stocks, ETFs, news" /></label><button className="notification-button" type="button" aria-label="Notifications"><Icon name="bell" /></button><button className="avatar-button" type="button" aria-label="Open profile"><span>MS</span><Icon name="chevron" /></button></div></header><main className="portfolio-content"><section className="metrics-grid"><MetricCard label="Total Portfolio Value" value="$128,547.32" detail="↗ $7,812.45 (6.47%)" tone="positive" /><MetricCard label="Available Cash" value="$12,430.18" /><MetricCard label="Invested Capital" value="$116,117.14" /><MetricCard label="Total Return (YTD)" value="+$7,812.45" detail="6.47%" tone="positive" /></section><section className="overview-grid"><section className="panel performance-panel"><div className="panel-header"><h2>Portfolio Performance <Icon name="info" /></h2></div><div className="range-tabs" role="tablist" aria-label="Performance time range">{(Object.keys(performanceSeries) as TimeRange[]).map((option) => <button key={option} type="button" className={range === option ? 'selected' : ''} aria-selected={range === option} onClick={() => setRange(option)} role="tab">{option}</button>)}</div><PerformanceChart key={range} range={range} /></section><AllocationPanel /></section><PositionsTable /></main></div></div>
}
