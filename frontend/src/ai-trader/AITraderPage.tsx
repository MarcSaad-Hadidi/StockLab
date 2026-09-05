import { routeFor } from '../navigation/routes'
import { useMemo, useState, type ReactNode } from 'react'
import {
  backtestSummary,
  currentDecisions,
  currentModel,
  performanceLabels,
  performanceSeries,
  positions,
  recentTrades,
  rejectedDecisions,
  traderSummary,
  type TraderAction,
} from './aiTraderData'
import './ai-trader.css'

type IconName =
  | 'activity'
  | 'bell'
  | 'briefcase'
  | 'chart'
  | 'chevron-down'
  | 'chevron-right'
  | 'clock'
  | 'edit'
  | 'grid'
  | 'mail'
  | 'menu'
  | 'pause'
  | 'pie-chart'
  | 'play'
  | 'search'
  | 'settings'
  | 'sparkles'
  | 'star'
  | 'trending-up'
  | 'user'
  | 'wallet'
  | 'x'

function Icon({ name, size = 18 }: { name: IconName; size?: number }) {
  const common = { fill: 'none', stroke: 'currentColor', strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const, strokeWidth: 1.8 }
  const paths: Record<IconName, ReactNode> = {
    activity: <path d="M3 12h3l2.2-6 3.6 12 2.2-6H21" {...common} />,
    bell: <><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9" {...common} /><path d="M10 21h4" {...common} /></>,
    briefcase: <><rect x="3" y="7" width="18" height="13" rx="2" {...common} /><path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 12h18M10 12v2h4v-2" {...common} /></>,
    chart: <><path d="M4 19V5M4 19h17" {...common} /><path d="m7 15 3-4 3 2 5-7" {...common} /><path d="M16 6h2v2" {...common} /></>,
    'chevron-down': <path d="m6 9 6 6 6-6" {...common} />,
    'chevron-right': <path d="m9 6 6 6-6 6" {...common} />,
    clock: <><circle cx="12" cy="12" r="8.5" {...common} /><path d="M12 7v5l3 2" {...common} /></>,
    edit: <><path d="m4 16.5-.8 3.3 3.3-.8L18 7.5 15.5 5 4 16.5Z" {...common} /><path d="m13.8 6.7 2.5 2.5M17.2 4.1l2.7 2.7" {...common} /></>,
    grid: <><rect x="3" y="3" width="7" height="7" rx="1" {...common} /><rect x="14" y="3" width="7" height="7" rx="1" {...common} /><rect x="3" y="14" width="7" height="7" rx="1" {...common} /><rect x="14" y="14" width="7" height="7" rx="1" {...common} /></>,
    mail: <><rect x="3" y="5" width="18" height="14" rx="2" {...common} /><path d="m4 7 8 6 8-6" {...common} /></>,
    menu: <path d="M4 7h16M4 12h16M4 17h16" {...common} />,
    pause: <><path d="M8 5v14M16 5v14" {...common} /></>,
    'pie-chart': <><path d="M12 3v9h9" {...common} /><path d="M20.5 15A9 9 0 1 1 9 3.5" {...common} /></>,
    play: <path d="m8 5 11 7-11 7V5Z" {...common} />,
    search: <><circle cx="10.8" cy="10.8" r="6.8" {...common} /><path d="m16 16 4.5 4.5" {...common} /></>,
    settings: <><circle cx="12" cy="12" r="3" {...common} /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-1.7 1.7-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5v.2h-2.4v-.2a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1L8 17l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H6.7v-2.4h.2a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9L8 8.6l1.7-1.7.1.1a1.7 1.7 0 0 0 1.9.3 1.7 1.7 0 0 0 1-1.5v-.2h2.4v.2a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1 1.7 1.7-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.2V14h-.2a1.7 1.7 0 0 0-1.5 1Z" {...common} /></>,
    sparkles: <><path d="m12 3 1.2 4.8L18 9l-4.8 1.2L12 15l-1.2-4.8L6 9l4.8-1.2L12 3ZM19 15l.6 2.4L22 18l-2.4.6L19 21l-.6-2.4L16 18l2.4-.6L19 15Z" {...common} /></>,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z" {...common} />,
    'trending-up': <><path d="M3 17 9 11l4 4 8-9" {...common} /><path d="M15 6h6v6" {...common} /></>,
    user: <><circle cx="12" cy="8" r="3.2" {...common} /><path d="M5.2 20a6.8 6.8 0 0 1 13.6 0" {...common} /></>,
    wallet: <><path d="M4 6.5A2.5 2.5 0 0 1 6.5 4H19a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5.5A2.5 2.5 0 0 1 3 17.5v-9A2.5 2.5 0 0 1 4 6.5Z" {...common} /><path d="M21 10h-5a2 2 0 0 0 0 4h5M16.5 12h.01" {...common} /></>,
    x: <path d="m6 6 12 12M18 6 6 18" {...common} />,
  }
  return <svg aria-hidden="true" className="icon" height={size} viewBox="0 0 24 24" width={size}>{paths[name]}</svg>
}

function Brand() {
  return <div aria-label="StockLab" className="brand"><span aria-hidden="true" className="brand-mark"><i /><i /><i /></span><span>Stock<span>Lab</span></span></div>
}

function StockMark({ symbol }: { symbol: string }) {
  const mark = symbol === 'MSFT' ? <><i /><i /><i /><i /></> : symbol === 'META' ? '∞' : symbol === 'GOOGL' ? 'G' : symbol.slice(0, 1)
  return <span aria-hidden="true" className={`stock-mark stock-mark-${symbol.toLowerCase()}`}>{mark}</span>
}

function formatCurrency(value: number) {
  return `$${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function formatSignedCurrency(value: number) {
  return `${value >= 0 ? '+' : '-'}${formatCurrency(Math.abs(value))}`
}

function formatSignedPercent(value: number) {
  return `${value >= 0 ? '+' : ''}${value.toFixed(2)}%`
}

function ActionBadge({ action }: { action: TraderAction }) {
  return <span className={`action-badge action-${action.toLowerCase()}`}>{action}</span>
}

function PanelHeading({ title, subtitle, action, onAction }: { title: string; subtitle?: string; action?: string; onAction?: () => void }) {
  return <div className="panel-heading"><div><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</div>{action && <button className="text-action" onClick={onAction} type="button">{action}<Icon name="chevron-right" size={14} /></button>}</div>
}

function MetricCard({ label, value, change, icon, tone }: { label: string; value: string; change?: string; icon: IconName; tone: string }) {
  return <article className={`metric-card metric-${tone}`}><span className="metric-icon"><Icon name={icon} size={17} /></span><span className="metric-label">{label}</span><strong>{value}</strong>{change && <small className={change.startsWith('-') ? 'negative' : 'positive'}>{change}</small>}</article>
}

function PerformanceChart() {
  const [activeIndex, setActiveIndex] = useState<number | null>(null)
  const width = 800
  const height = 235
  const padding = { top: 17, right: 15, bottom: 30, left: 8 }
  const min = Math.min(...performanceSeries) - 1000
  const max = Math.max(...performanceSeries) + 1000
  const usableWidth = width - padding.left - padding.right
  const usableHeight = height - padding.top - padding.bottom
  const points = performanceSeries.map((value, index) => ({
    x: padding.left + (index / (performanceSeries.length - 1)) * usableWidth,
    y: padding.top + ((max - value) / (max - min)) * usableHeight,
    value,
  }))
  const linePath = points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x} ${point.y}`).join(' ')
  const areaPath = `${linePath} L ${points.at(-1)?.x ?? width} ${height - padding.bottom} L ${points[0].x} ${height - padding.bottom} Z`
  const activePoint = activeIndex === null ? null : points[activeIndex]
  const tooltipX = activePoint ? Math.min(Math.max(activePoint.x - 55, 8), width - 118) : 0
  const tooltipY = activePoint ? Math.max(9, activePoint.y - 52) : 0
  return <div className="performance-chart-wrap"><div className="chart-summary"><div><span>Current value</span><strong>{formatCurrency(traderSummary.currentValue)}</strong></div><div><b>+{formatCurrency(traderSummary.profitLoss)}</b><small>+{traderSummary.returnPercent.toFixed(2)}% all time</small></div></div><svg aria-label="AI Trader performance chart" className="performance-chart" role="img" viewBox={`0 0 ${width} ${height}`}><defs><linearGradient id="ai-trader-fill" x1="0" x2="0" y1="0" y2="1"><stop offset="0%" stopColor="#2f7bf0" stopOpacity=".2" /><stop offset="100%" stopColor="#2f7bf0" stopOpacity="0" /></linearGradient></defs>{[0, 1, 2, 3].map((tick) => { const y = padding.top + (tick / 3) * usableHeight; const value = max - (tick / 3) * (max - min); return <g key={tick}><line className="chart-grid-line" x1={padding.left} x2={width - padding.right} y1={y} y2={y} /><text className="chart-y-label" textAnchor="end" x={width - padding.right} y={y - 5}>{`$${Math.round(value / 1000)}K`}</text></g> })}<path className="chart-area" d={areaPath} fill="url(#ai-trader-fill)" /><path className="chart-line" d={linePath} />{activePoint && <line className="chart-crosshair" x1={activePoint.x} x2={activePoint.x} y1={padding.top} y2={height - padding.bottom} />}{points.map((point, index) => <g className="chart-point-group" key={`${point.x}-${point.value}`} onPointerEnter={() => setActiveIndex(index)} onPointerLeave={() => setActiveIndex(null)}><circle className="chart-point-hit" cx={point.x} cy={point.y} r="20" /><circle className="chart-point" cx={point.x} cy={point.y} r={index === points.length - 1 ? 4 : 2.5} /></g>)}{activePoint && activeIndex !== null && <g className="chart-tooltip" pointerEvents="none" transform={`translate(${tooltipX} ${tooltipY})`}><rect height="43" rx="6" width="118" /><text x="9" y="16">{performanceLabels[Math.min(performanceLabels.length - 1, Math.round(activeIndex / 2))]}</text><text className="chart-tooltip-value" x="9" y="33">{formatCurrency(activePoint.value)}</text></g>}{performanceLabels.map((label, index) => <text className="chart-x-label" key={label} textAnchor={index === 0 ? 'start' : index === performanceLabels.length - 1 ? 'end' : 'middle'} x={padding.left + (index / (performanceLabels.length - 1)) * usableWidth} y={height - 6}>{label}</text>)}</svg></div>
}

function PositionTable({ compact = false }: { compact?: boolean }) {
  const rows = compact ? positions.slice(0, 5) : positions
  return <div className="table-scroll"><table className="data-table position-table"><thead><tr><th>Asset</th><th>Size</th><th>Entry Price</th><th>Current Price</th><th>P&amp;L ($)</th><th>P&amp;L (%)</th></tr></thead><tbody>{rows.map((position) => <tr key={position.symbol}><td><div className="asset-cell"><StockMark symbol={position.symbol} /><span><strong>{position.symbol}</strong><small>{position.company}</small></span></div></td><td>{position.size}</td><td>{formatCurrency(position.entryPrice)}</td><td>{formatCurrency(position.currentPrice)}</td><td className={position.pnl >= 0 ? 'positive' : 'negative'}>{formatSignedCurrency(position.pnl)}</td><td><span className={position.pnlPercent >= 0 ? 'positive' : 'negative'}>{formatSignedPercent(position.pnlPercent)}</span></td></tr>)}</tbody></table></div>
}

function DecisionTable() {
  return <div className="table-scroll"><table className="data-table decision-table"><thead><tr><th>Asset</th><th>Action</th><th>Confidence</th><th>Target</th><th>Stop Loss</th></tr></thead><tbody>{currentDecisions.map((decision) => <tr key={decision.symbol}><td><div className="asset-cell compact-asset"><StockMark symbol={decision.symbol} /><span><strong>{decision.symbol}</strong><small>{decision.company}</small></span></div></td><td><ActionBadge action={decision.action} /></td><td>{decision.confidence}%</td><td>{formatCurrency(decision.targetPrice)}</td><td>{formatCurrency(decision.stopLoss)}</td></tr>)}</tbody></table></div>
}

function RejectedTable() {
  return <div className="table-scroll"><table className="data-table rejected-table"><thead><tr><th>Asset</th><th>Action</th><th>Reason</th><th>Confidence</th><th>Time</th></tr></thead><tbody>{rejectedDecisions.map((decision) => <tr key={`${decision.symbol}-${decision.time}`}><td><strong>{decision.symbol}</strong></td><td><ActionBadge action={decision.action} /></td><td className="reason-cell">{decision.reason}</td><td>{decision.confidence}%</td><td className="time-cell">{decision.time}</td></tr>)}</tbody></table></div>
}

function TradesTable({ compact = false }: { compact?: boolean }) {
  const rows = compact ? recentTrades.slice(0, 4) : recentTrades
  return <div className="table-scroll"><table className="data-table trades-table"><thead><tr><th>Asset</th><th>Side</th><th>Entry / Exit</th><th>Price</th><th>P&amp;L ($)</th><th>P&amp;L (%)</th><th>Time</th></tr></thead><tbody>{rows.map((trade) => <tr key={`${trade.symbol}-${trade.time}`}><td><strong>{trade.symbol}</strong></td><td><ActionBadge action={trade.side} /></td><td>{trade.type}</td><td>{formatCurrency(trade.price)}</td><td className={trade.pnl >= 0 ? 'positive' : 'negative'}>{formatSignedCurrency(trade.pnl)}</td><td className={trade.pnlPercent >= 0 ? 'positive' : 'negative'}>{formatSignedPercent(trade.pnlPercent)}</td><td className="time-cell">{trade.time}</td></tr>)}</tbody></table></div>
}

function Sparkline({ tone = 'blue' }: { tone?: 'blue' | 'purple' }) {
  const values = tone === 'purple' ? [14, 17, 15, 18, 19, 23, 21, 25, 22, 28, 26] : [15, 14, 18, 16, 20, 19, 24, 22, 26, 25, 29]
  const points = values.map((value, index) => `${(index / (values.length - 1)) * 180},${38 - value}`).join(' ')
  return <svg aria-hidden="true" className={`sparkline sparkline-${tone}`} viewBox="0 0 180 40"><polyline fill="none" points={points} stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.2" /></svg>
}

function PerformanceStatsPanel() {
  return <section className="panel stats-panel"><PanelHeading title="Performance Statistics" subtitle="Recent bot performance" /><div className="stats-content"><div className="stat-list"><div><span>Total Return</span><strong className="positive">+12.85%</strong></div><div><span>Accuracy</span><strong>72.4%</strong></div><div><span>Win Rate</span><strong>68.7%</strong></div><div><span>Avg. Win</span><strong className="positive">+2.35%</strong></div><div><span>Total Trades</span><strong>42</strong></div><div><span>Best Trade</span><strong className="positive">+8.42%</strong></div></div><Sparkline /></div></section>
}

function ModelPanel({ detailed = false }: { detailed?: boolean }) {
  return <section className={`panel model-panel ${detailed ? 'model-panel-detailed' : ''}`}><PanelHeading title="Model Version" subtitle="Machine learning model health" /><div className="model-version-row"><span className="model-badge"><Icon name="sparkles" size={15} /></span><div><strong>Model {currentModel.version}</strong><small>Current production model</small></div><b className="current-badge">Current</b></div><div className="model-metrics"><div><span>Accuracy</span><strong>{currentModel.accuracy}</strong></div><div><span>Precision</span><strong>{currentModel.precision}</strong></div><div><span>Recall</span><strong>{currentModel.recall}</strong></div><div><span>F1 Score</span><strong>{currentModel.f1Score}</strong></div><div><span>Trained On</span><strong>{currentModel.trainedOn}</strong></div><div><span>Next Retrain</span><strong>May 27, 2024</strong></div></div>{detailed && <div className="model-history">{['v3.2.1', 'v3.1.0', 'v3.0.2'].map((version, index) => <div key={version}><span>{version}</span><small>{index === 0 ? 'Current' : 'Archived'} · {index === 0 ? currentModel.accuracy : index === 1 ? '70.8%' : '68.5%'}</small><Icon name="chevron-right" size={14} /></div>)}</div>}<button className="outline-button" onClick={() => undefined} type="button">View Model Details <Icon name="chevron-right" size={13} /></button></section>
}

function BacktestPanel({ detailed = false }: { detailed?: boolean }) {
  return <section className={`panel backtest-panel ${detailed ? 'backtest-panel-detailed' : ''}`}><PanelHeading title="Backtesting Results" subtitle="Historical strategy simulation" /><div className="backtest-header"><span>Last 6 Months</span><Icon name="chevron-down" size={13} /></div><div className="backtest-content"><div className="backtest-metrics"><div><span>Total Return</span><strong className="positive">{backtestSummary.totalReturn}</strong></div><div><span>Win Rate</span><strong>{backtestSummary.winRate}</strong></div><div><span>Max Drawdown</span><strong className="negative">{backtestSummary.maxDrawdown}</strong></div><div><span>Sharpe Ratio</span><strong>{backtestSummary.sharpeRatio}</strong></div><div><span>Total Trades</span><strong>{backtestSummary.totalTrades}</strong></div><div><span>Profit Factor</span><strong>{backtestSummary.profitFactor}</strong></div></div><Sparkline tone="purple" /></div>{detailed && <div className="backtest-history"><div><span>Momentum strategy · May 2024</span><strong className="positive">+28.12%</strong></div><div><span>Balanced strategy · Apr 2024</span><strong className="positive">+19.64%</strong></div><div><span>Baseline model · Mar 2024</span><strong className="positive">+14.22%</strong></div></div>}<button className="outline-button" onClick={() => undefined} type="button">View Full Backtest <Icon name="chevron-right" size={13} /></button></section>
}

const tabs = ['Overview', 'Positions', 'Trades', 'Decisions', 'Performance', 'Model', 'Backtests'] as const
type TabName = typeof tabs[number]

function Overview({ setActiveTab }: { setActiveTab: (tab: TabName) => void }) {
  return <><section className="metric-grid"><MetricCard change="+$12,846.37" icon="wallet" label="Initial Capital" tone="blue" value={formatCurrency(traderSummary.initialCapital)} /><MetricCard change="+12.85%" icon="chart" label="Current Value" tone="green" value={formatCurrency(traderSummary.currentValue)} /><MetricCard change="+12.85%" icon="trending-up" label="Total Profit / Loss" tone="purple" value={formatSignedCurrency(traderSummary.profitLoss)} /><MetricCard change="+3.4%" icon="activity" label="Win Rate" tone="orange" value={`${traderSummary.winRate}%`} /><MetricCard change="↓ 1.12%" icon="pie-chart" label="Max Drawdown" tone="red" value={`${traderSummary.maxDrawdown.toFixed(2)}%`} /></section><section className="panel performance-panel"><PanelHeading title="Performance" subtitle="AI Trader portfolio value over time" action="View details" onAction={() => setActiveTab('Performance')} /><div className="range-tabs" role="tablist"><button className="selected" type="button">1M</button><button type="button">3M</button><button type="button">6M</button><button type="button">1Y</button><button type="button">ALL</button></div><PerformanceChart /></section><div className="overview-grid overview-grid-top"><section className="panel positions-panel"><PanelHeading title="Open Positions (5)" subtitle="Current bot holdings" action="View all" onAction={() => setActiveTab('Positions')} /><PositionTable compact /><span className="panel-footnote">Total Unrealized P&amp;L <strong className="positive">+{formatCurrency(27472.77)} (+24.36%)</strong></span></section><section className="panel decisions-panel"><PanelHeading title="Current Decisions" subtitle="Latest model signals" action="View all" onAction={() => setActiveTab('Decisions')} /><DecisionTable /></section><section className="panel rejected-panel"><PanelHeading title="Rejected Decisions (Risk System)" subtitle="Signals blocked by safeguards" action="View all" onAction={() => setActiveTab('Decisions')} /><RejectedTable /></section></div><section className="panel trades-panel"><PanelHeading title="Recent Trades" subtitle="Latest executed orders" action="View all" onAction={() => setActiveTab('Trades')} /><TradesTable compact /></section><div className="overview-grid overview-grid-bottom"><PerformanceStatsPanel /><ModelPanel /><BacktestPanel /></div></>
}

function TabContent({ activeTab, setActiveTab }: { activeTab: TabName; setActiveTab: (tab: TabName) => void }) {
  if (activeTab === 'Overview') return <Overview setActiveTab={setActiveTab} />
  if (activeTab === 'Positions') return <section className="panel standalone-panel"><PanelHeading title="Positions" subtitle="All currently open AI Trader holdings" /><PositionTable /></section>
  if (activeTab === 'Trades') return <section className="panel standalone-panel"><PanelHeading title="Trades" subtitle="Complete simulated execution history" /><TradesTable /></section>
  if (activeTab === 'Decisions') return <div className="standalone-grid"><section className="panel standalone-panel"><PanelHeading title="Decisions" subtitle="Signals produced by the current model" /><DecisionTable /></section><section className="panel standalone-panel"><PanelHeading title="Rejected Decisions" subtitle="Signals blocked by the risk system" /><RejectedTable /></section></div>
  if (activeTab === 'Performance') return <><section className="panel standalone-panel"><PanelHeading title="Performance" subtitle="Detailed simulated portfolio performance" /><PerformanceChart /></section><PerformanceStatsPanel /></>
  if (activeTab === 'Model') return <div className="standalone-grid"><ModelPanel detailed /><section className="panel standalone-panel model-notes"><PanelHeading title="Model monitoring" subtitle="Current safeguards and data health" /><div className="monitor-list"><div><span className="monitor-dot green" /><span><strong>Data pipeline</strong><small>Market data received 2 minutes ago</small></span><b>Healthy</b></div><div><span className="monitor-dot blue" /><span><strong>Risk controls</strong><small>All portfolio constraints are active</small></span><b>Enabled</b></div><div><span className="monitor-dot purple" /><span><strong>Feature drift</strong><small>No significant drift detected</small></span><b>Stable</b></div></div></section></div>
  return <div className="standalone-grid"><BacktestPanel detailed /><section className="panel standalone-panel backtest-notes"><PanelHeading title="Backtest configuration" subtitle="Simulation settings used for this preview" /><div className="config-list"><div><span>Starting capital</span><strong>$100,000.00</strong></div><div><span>Strategy</span><strong>Momentum + risk controls</strong></div><div><span>Period</span><strong>Dec 2023 – May 2024</strong></div><div><span>Trading universe</span><strong>US large cap equities</strong></div></div><button className="primary-button" onClick={() => undefined} type="button"><Icon name="play" size={14} /> Run New Backtest</button></section></div>
}

const navigation = [
  { label: 'Dashboard', icon: 'grid' as IconName, href: routeFor('dashboard') },
  { label: 'Market', icon: 'chart' as IconName, href: routeFor('market') },
  { label: 'Portfolio', icon: 'briefcase' as IconName, href: routeFor('portfolio') },
  { label: 'Transactions', icon: 'activity' as IconName, href: routeFor('transactions') },
  { label: 'Watchlist', icon: 'star' as IconName, href: routeFor('watchlist') },
  { label: 'Alerts', icon: 'bell' as IconName, href: routeFor('alerts') },
]

export default function AITraderPage() {
  const [activeTab, setActiveTab] = useState<TabName>('Overview')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [botActive, setBotActive] = useState(true)
  const [toast, setToast] = useState('')
  const filteredNavigation = useMemo(() => navigation, [])
  const showToast = (message: string) => { setToast(message); window.setTimeout(() => setToast(''), 2300) }
  return <div className={`ai-trader-page ${sidebarOpen ? 'sidebar-open' : ''}`}><button aria-label="Close navigation" className="sidebar-backdrop" onClick={() => setSidebarOpen(false)} type="button" /><aside className="ai-trader-sidebar"><Brand /><nav aria-label="Primary navigation" className="sidebar-nav"><span className="nav-label">Overview</span>{filteredNavigation.slice(0, 2).map((item) => <a className="nav-item" href={item.href} key={item.label} onClick={() => setSidebarOpen(false)}><Icon name={item.icon} size={16} /><span>{item.label}</span></a>)}<span className="nav-label nav-label-spaced">Your trading</span>{filteredNavigation.slice(2).map((item) => <a className="nav-item" href={item.href} key={item.label} onClick={() => setSidebarOpen(false)}><Icon name={item.icon} size={16} /><span>{item.label}</span></a>)}<span className="nav-label nav-label-spaced">AI</span><a aria-current="page" className="nav-item active" href={routeFor('ai-trader')} onClick={() => setSidebarOpen(false)}><Icon name="sparkles" size={16} /><span>AI Trader</span><b className="new-badge">New</b></a><span className="nav-label nav-label-spaced">Account</span><a className="nav-item" href={routeFor('profile')} onClick={() => setSidebarOpen(false)}><Icon name="user" size={16} /><span>Profile</span></a><button className="nav-item nav-button" onClick={() => window.location.assign(routeFor('logout'))} type="button"><Icon name="activity" size={16} /><span>Logout</span></button></nav><div className="sidebar-footer"><p>StockLab preview</p></div></aside><main className="ai-trader-main"><header className="ai-trader-topbar"><button aria-label="Open navigation" className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={20} /></button><div className="breadcrumb"><strong>AI Trader</strong><span>—</span><a href={routeFor('ai-trader')}>/ai-trader</a></div><div className="topbar-actions"><label className="global-search"><Icon name="search" size={16} /><input aria-label="Search stocks" placeholder="Search stocks, ETFs, news..." /></label><button aria-label="Notifications" className="icon-button notification-button" onClick={() => showToast('You are all caught up.')} type="button"><Icon name="bell" size={18} /><i>2</i></button><button aria-label="Open messages" className="icon-button mail-button" onClick={() => showToast('No new messages.')} type="button"><Icon name="mail" size={17} /></button><button aria-label="Open account menu" className="topbar-account" onClick={() => window.location.assign(routeFor('profile'))} type="button"><span className="topbar-avatar">MS</span><Icon name="chevron-down" size={14} /></button></div></header><div className="ai-trader-content"><section className="ai-trader-heading"><div><h1>AI Trader</h1><p>AI-powered trading bot using machine learning and real-time market data.</p></div><div className="heading-actions"><button className={`bot-status ${botActive ? 'is-active' : 'is-paused'}`} onClick={() => { setBotActive((current) => !current); showToast(botActive ? 'AI Trader paused for this preview.' : 'AI Trader is active again.') }} type="button"><i /> Bot {botActive ? 'Active' : 'Paused'} <Icon name="chevron-down" size={13} /></button><button className="secondary-button" onClick={() => window.location.assign(routeFor('profile'))} type="button"><Icon name="settings" size={13} /> Settings</button><button className="primary-button" onClick={() => { setActiveTab('Backtests'); showToast('Backtest workspace opened.') }} type="button"><Icon name="play" size={13} /> Start New Backtest</button></div></section><div aria-label="AI Trader sections" className="ai-tabs" role="tablist">{tabs.map((tab) => <button aria-selected={activeTab === tab} className={activeTab === tab ? 'selected' : ''} key={tab} onClick={() => setActiveTab(tab)} role="tab" type="button">{tab}</button>)}</div><TabContent activeTab={activeTab} setActiveTab={setActiveTab} /><p className="simulation-note"><Icon name="activity" size={13} /> All AI Trader metrics, decisions, and backtests are simulated. No Python or ML service is connected.</p></div></main><div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast}</div></div>
}
