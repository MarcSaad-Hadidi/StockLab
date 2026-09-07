import { Sidebar } from '../components/layout/Sidebar'
import { formatCompactCurrency, formatCurrency, formatNumber, formatPercent, formatSignedCurrency, formatSignedPercent } from '../i18n/formatters'
import { routeFor } from '../navigation/routes'
import { useTranslation } from 'react-i18next'
import { useEffect, useState, type ReactNode } from 'react'
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

function StockMark({ symbol }: { symbol: string }) {
  const mark = symbol === 'MSFT' ? <><i /><i /><i /><i /></> : symbol === 'META' ? '∞' : symbol === 'GOOGL' ? 'G' : symbol.slice(0, 1)
  return <span aria-hidden="true" className={`stock-mark stock-mark-${symbol.toLowerCase()}`}>{mark}</span>
}

function ActionBadge({ action }: { action: TraderAction }) {
  const { t } = useTranslation()
  return <span className={`action-badge action-${action.toLowerCase()}`}>{t(`aiTrader.actions.${action.toLowerCase()}`)}</span>
}

function PanelHeading({ title, subtitle, action, onAction }: { title: string; subtitle?: string; action?: string; onAction?: () => void }) {
  return <div className="panel-heading"><div><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</div>{action && <button className="text-action" onClick={onAction} type="button">{action}<Icon name="chevron-right" size={14} /></button>}</div>
}

function MetricCard({ label, value, change, icon, tone }: { label: string; value: string; change?: string; icon: IconName; tone: string }) {
  return <article className={`metric-card metric-${tone}`}><span className="metric-icon"><Icon name={icon} size={17} /></span><span className="metric-label">{label}</span><strong>{value}</strong>{change && <small className={change.startsWith('-') ? 'negative' : 'positive'}>{change}</small>}</article>
}

function PerformanceChart() {
  const { t } = useTranslation()
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
  return (
    <div className="performance-chart-wrap">
      <div className="chart-summary">
        <div><span>{t('aiTrader.currentValue')}</span><strong>{formatCurrency(traderSummary.currentValue)}</strong></div>
        <div><b>{formatSignedCurrency(traderSummary.profitLoss)}</b><small>{t('aiTrader.allTime', { percent: formatNumber(traderSummary.returnPercent) })}</small></div>
      </div>
      <svg aria-label={t('aiTrader.performanceChart')} className="performance-chart" role="img" viewBox={`0 0 ${width} ${height}`}>
        <defs><linearGradient id="ai-trader-fill" x1="0" x2="0" y1="0" y2="1"><stop offset="0%" stopColor="#2f7bf0" stopOpacity=".2" /><stop offset="100%" stopColor="#2f7bf0" stopOpacity="0" /></linearGradient></defs>
        {[0, 1, 2, 3].map((tick) => {
          const y = padding.top + (tick / 3) * usableHeight
          const value = max - (tick / 3) * (max - min)
          return <g key={tick}><line className="chart-grid-line" x1={padding.left} x2={width - padding.right} y1={y} y2={y} /><text className="chart-y-label" textAnchor="end" x={width - padding.right} y={y - 5}>{formatCompactCurrency(value)}</text></g>
        })}
        <path className="chart-area" d={areaPath} fill="url(#ai-trader-fill)" />
        <path className="chart-line" d={linePath} />
        {activePoint && <line className="chart-crosshair" x1={activePoint.x} x2={activePoint.x} y1={padding.top} y2={height - padding.bottom} />}
        {points.map((point, index) => <g className="chart-point-group" key={`${point.x}-${point.value}`} onPointerEnter={() => setActiveIndex(index)} onPointerLeave={() => setActiveIndex(null)}><circle className="chart-point-hit" cx={point.x} cy={point.y} r="20" /><circle className="chart-point" cx={point.x} cy={point.y} r={index === points.length - 1 ? 4 : 2.5} /></g>)}
        {activePoint && activeIndex !== null && <g className="chart-tooltip" pointerEvents="none" transform={`translate(${tooltipX} ${tooltipY})`}><rect height="43" rx="6" width="118" /><text x="9" y="16">{t(performanceLabels[Math.min(performanceLabels.length - 1, Math.round(activeIndex / 2))])}</text><text className="chart-tooltip-value" x="9" y="33">{formatCurrency(activePoint.value)}</text></g>}
        {performanceLabels.map((label, index) => <text className="chart-x-label" key={label} textAnchor={index === 0 ? 'start' : index === performanceLabels.length - 1 ? 'end' : 'middle'} x={padding.left + (index / (performanceLabels.length - 1)) * usableWidth} y={height - 6}>{t(label)}</text>)}
      </svg>
    </div>
  )
}

function PositionTable({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation()
  const rows = compact ? positions.slice(0, 5) : positions
  return <div className="table-scroll"><table className="data-table position-table"><thead><tr><th>{t('aiTrader.table.asset')}</th><th>{t('aiTrader.table.size')}</th><th>{t('aiTrader.table.entryPrice')}</th><th>{t('aiTrader.table.currentPrice')}</th><th>{t('aiTrader.table.pnlDollar')}</th><th>{t('aiTrader.table.pnlPercent')}</th></tr></thead><tbody>{rows.map((position) => <tr key={position.symbol}><td><div className="asset-cell"><StockMark symbol={position.symbol} /><span><strong>{position.symbol}</strong><small>{position.company}</small></span></div></td><td>{position.size}</td><td>{formatCurrency(position.entryPrice)}</td><td>{formatCurrency(position.currentPrice)}</td><td className={position.pnl >= 0 ? 'positive' : 'negative'}>{formatSignedCurrency(position.pnl)}</td><td><span className={position.pnlPercent >= 0 ? 'positive' : 'negative'}>{formatSignedPercent(position.pnlPercent)}</span></td></tr>)}</tbody></table></div>
}

function DecisionTable() {
  const { t } = useTranslation()
  return <div className="table-scroll"><table className="data-table decision-table"><thead><tr><th>{t('aiTrader.table.asset')}</th><th>{t('aiTrader.table.action')}</th><th>{t('aiTrader.table.confidence')}</th><th>{t('aiTrader.table.target')}</th><th>{t('aiTrader.table.stopLoss')}</th></tr></thead><tbody>{currentDecisions.map((decision) => <tr key={decision.symbol}><td><div className="asset-cell compact-asset"><StockMark symbol={decision.symbol} /><span><strong>{decision.symbol}</strong><small>{decision.company}</small></span></div></td><td><ActionBadge action={decision.action} /></td><td>{formatPercent(decision.confidence, undefined, 0)}</td><td>{formatCurrency(decision.targetPrice)}</td><td>{formatCurrency(decision.stopLoss)}</td></tr>)}</tbody></table></div>
}

function RejectedTable() {
  const { t } = useTranslation()
  const reasonKeys = { 'Volatility above threshold': 'aiTrader.reasons.volatilityThreshold', 'High volatility risk': 'aiTrader.reasons.highVolatilityRisk', 'Liquidity below minimum': 'aiTrader.reasons.liquidityMinimum', 'News sentiment negative': 'aiTrader.reasons.newsSentiment' } as const
  return <div className="table-scroll"><table className="data-table rejected-table"><thead><tr><th>{t('aiTrader.table.asset')}</th><th>{t('aiTrader.table.action')}</th><th>{t('aiTrader.table.reason')}</th><th>{t('aiTrader.table.confidence')}</th><th>{t('aiTrader.table.time')}</th></tr></thead><tbody>{rejectedDecisions.map((decision) => <tr key={`${decision.symbol}-${decision.timeKey}`}><td><strong>{decision.symbol}</strong></td><td><ActionBadge action={decision.action} /></td><td className="reason-cell">{t(reasonKeys[decision.reason as keyof typeof reasonKeys])}</td><td>{formatPercent(decision.confidence, undefined, 0)}</td><td className="time-cell">{t(decision.timeKey)}</td></tr>)}</tbody></table></div>
}

function TradesTable({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation()
  const rows = compact ? recentTrades.slice(0, 4) : recentTrades
  return <div className="table-scroll"><table className="data-table trades-table"><thead><tr><th>{t('aiTrader.table.asset')}</th><th>{t('aiTrader.table.side')}</th><th>{t('aiTrader.table.entryExit')}</th><th>{t('aiTrader.table.price')}</th><th>{t('aiTrader.table.pnlDollar')}</th><th>{t('aiTrader.table.pnlPercent')}</th><th>{t('aiTrader.table.time')}</th></tr></thead><tbody>{rows.map((trade) => <tr key={`${trade.symbol}-${trade.timeKey}`}><td><strong>{trade.symbol}</strong></td><td><ActionBadge action={trade.side} /></td><td>{t(`aiTrader.table.${trade.type.toLowerCase()}`)}</td><td>{formatCurrency(trade.price)}</td><td className={trade.pnl >= 0 ? 'positive' : 'negative'}>{formatSignedCurrency(trade.pnl)}</td><td className={trade.pnlPercent >= 0 ? 'positive' : 'negative'}>{formatSignedPercent(trade.pnlPercent)}</td><td className="time-cell">{t(trade.timeKey)}</td></tr>)}</tbody></table></div>
}

function Sparkline({ tone = 'blue' }: { tone?: 'blue' | 'purple' }) {
  const values = tone === 'purple' ? [14, 17, 15, 18, 19, 23, 21, 25, 22, 28, 26] : [15, 14, 18, 16, 20, 19, 24, 22, 26, 25, 29]
  const points = values.map((value, index) => `${(index / (values.length - 1)) * 180},${38 - value}`).join(' ')
  return <svg aria-hidden="true" className={`sparkline sparkline-${tone}`} viewBox="0 0 180 40"><polyline fill="none" points={points} stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2.2" /></svg>
}

function PerformanceStatsPanel() {
  const { t } = useTranslation()
  return <section className="panel stats-panel"><PanelHeading title={t('aiTrader.performanceStatistics')} subtitle={t('aiTrader.recentBotPerformance')} /><div className="stats-content"><div className="stat-list"><div><span>{t('aiTrader.totalReturn')}</span><strong className="positive">{formatSignedPercent(12.85)}</strong></div><div><span>{t('aiTrader.accuracy')}</span><strong>{formatPercent(72.4, undefined, 1)}</strong></div><div><span>{t('aiTrader.winRate')}</span><strong>{formatPercent(68.7, undefined, 1)}</strong></div><div><span>{t('aiTrader.averageWin')}</span><strong className="positive">{formatSignedPercent(2.35)}</strong></div><div><span>{t('aiTrader.totalTrades')}</span><strong>{formatNumber(42, undefined, 0)}</strong></div><div><span>{t('aiTrader.bestTrade')}</span><strong className="positive">{formatSignedPercent(8.42)}</strong></div></div><Sparkline /></div></section>
}

function ModelPanel({ detailed = false }: { detailed?: boolean }) {
  const { t } = useTranslation()
  return <section className={`panel model-panel ${detailed ? 'model-panel-detailed' : ''}`}><PanelHeading title={t('aiTrader.modelVersion')} subtitle={t('aiTrader.modelHealth')} /><div className="model-version-row"><span className="model-badge"><Icon name="sparkles" size={15} /></span><div><strong>{t('aiTrader.modelLabel')} {currentModel.version}</strong><small>{t('aiTrader.currentProductionModel')}</small></div><b className="current-badge">{t('aiTrader.current')}</b></div><div className="model-metrics"><div><span>{t('aiTrader.accuracy')}</span><strong>{formatPercent(currentModel.accuracy, undefined, 1)}</strong></div><div><span>{t('aiTrader.precision')}</span><strong>{formatPercent(currentModel.precision, undefined, 1)}</strong></div><div><span>{t('aiTrader.recall')}</span><strong>{formatPercent(currentModel.recall, undefined, 1)}</strong></div><div><span>{t('aiTrader.f1Score')}</span><strong>{formatPercent(currentModel.f1Score, undefined, 1)}</strong></div><div><span>{t('aiTrader.trainedOn')}</span><strong>{currentModel.trainedOnKey ? t(currentModel.trainedOnKey) : currentModel.trainedOn}</strong></div><div><span>{t('aiTrader.nextRetrain')}</span><strong>{t('aiTrader.nextRetrainValue')}</strong></div></div>{detailed && <div className="model-history">{['v3.2.1', 'v3.1.0', 'v3.0.2'].map((version, index) => { const accuracy = index === 0 ? currentModel.accuracy : index === 1 ? 70.8 : 68.5; return <div key={version}><span>{version}</span><small>{index === 0 ? t('aiTrader.current') : t('aiTrader.archived')} · {formatPercent(accuracy, undefined, 1)}</small><Icon name="chevron-right" size={14} /></div> })}</div>}<button className="outline-button" onClick={() => undefined} type="button">{t('aiTrader.viewModelDetails')} <Icon name="chevron-right" size={13} /></button></section>
}

function BacktestPanel({ detailed = false }: { detailed?: boolean }) {
  const { t } = useTranslation()
  return <section className={`panel backtest-panel ${detailed ? 'backtest-panel-detailed' : ''}`}><PanelHeading title={t('aiTrader.backtestingResults')} subtitle={t('aiTrader.historicalSimulation')} /><div className="backtest-header"><span>{t('aiTrader.lastSixMonths')}</span><Icon name="chevron-down" size={13} /></div><div className="backtest-content"><div className="backtest-metrics"><div><span>{t('aiTrader.totalReturn')}</span><strong className="positive">{formatSignedPercent(backtestSummary.totalReturn)}</strong></div><div><span>{t('aiTrader.winRate')}</span><strong>{formatPercent(backtestSummary.winRate, undefined, 1)}</strong></div><div><span>{t('aiTrader.maxDrawdown')}</span><strong className="negative">{formatSignedPercent(backtestSummary.maxDrawdown)}</strong></div><div><span>{t('aiTrader.sharpeRatio')}</span><strong>{formatNumber(backtestSummary.sharpeRatio)}</strong></div><div><span>{t('aiTrader.totalTrades')}</span><strong>{formatNumber(backtestSummary.totalTrades, undefined, 0)}</strong></div><div><span>{t('aiTrader.profitFactor')}</span><strong>{formatNumber(backtestSummary.profitFactor)}</strong></div></div><Sparkline tone="purple" /></div>{detailed && <div className="backtest-history"><div><span>{t('aiTrader.strategies.momentumMay')}</span><strong className="positive">{formatSignedPercent(28.12)}</strong></div><div><span>{t('aiTrader.strategies.balancedApr')}</span><strong className="positive">{formatSignedPercent(19.64)}</strong></div><div><span>{t('aiTrader.strategies.baselineMar')}</span><strong className="positive">{formatSignedPercent(14.22)}</strong></div></div>}<button className="outline-button" onClick={() => undefined} type="button">{t('aiTrader.viewFullBacktest')} <Icon name="chevron-right" size={13} /></button></section>
}

const tabs = ['Overview', 'Positions', 'Trades', 'Decisions', 'Performance', 'Model', 'Backtests'] as const
type TabName = typeof tabs[number]

function Overview({ setActiveTab }: { setActiveTab: (tab: TabName) => void }) {
  const { t } = useTranslation()
  return <><section className="metric-grid"><MetricCard change={formatSignedCurrency(12846.37)} icon="wallet" label={t('aiTrader.initialCapital')} tone="blue" value={formatCurrency(traderSummary.initialCapital)} /><MetricCard change={formatSignedPercent(traderSummary.returnPercent)} icon="chart" label={t('aiTrader.currentValueMetric')} tone="green" value={formatCurrency(traderSummary.currentValue)} /><MetricCard change={formatSignedPercent(traderSummary.returnPercent)} icon="trending-up" label={t('aiTrader.totalProfitLoss')} tone="purple" value={formatSignedCurrency(traderSummary.profitLoss)} /><MetricCard change={formatSignedPercent(3.4, undefined, 1)} icon="activity" label={t('aiTrader.winRate')} tone="orange" value={formatPercent(traderSummary.winRate, undefined, 1)} /><MetricCard change={`↓ ${formatPercent(1.12)}`} icon="pie-chart" label={t('aiTrader.maxDrawdown')} tone="red" value={formatSignedPercent(traderSummary.maxDrawdown)} /></section><section className="panel performance-panel"><PanelHeading title={t('dashboard.performanceTitle')} subtitle={t('aiTrader.portfolioValueOverTime')} action={t('aiTrader.viewDetails')} onAction={() => setActiveTab('Performance')} /><div className="range-tabs" role="tablist"><button className="selected" type="button">1M</button><button type="button">3M</button><button type="button">6M</button><button type="button">1Y</button><button type="button">ALL</button></div><PerformanceChart /></section><div className="overview-grid overview-grid-top"><section className="panel positions-panel"><PanelHeading title={t('aiTrader.openPositions', { count: 5 })} subtitle={t('aiTrader.currentBotHoldings')} action={t('common.viewAll')} onAction={() => setActiveTab('Positions')} /><PositionTable compact /><span className="panel-footnote">{t('aiTrader.totalUnrealizedPnl')} <strong className="positive">{formatSignedCurrency(27472.77)} ({formatSignedPercent(24.36)})</strong></span></section><section className="panel decisions-panel"><PanelHeading title={t('aiTrader.currentDecisions')} subtitle={t('aiTrader.latestModelSignals')} action={t('common.viewAll')} onAction={() => setActiveTab('Decisions')} /><DecisionTable /></section><section className="panel rejected-panel"><PanelHeading title={t('aiTrader.rejectedDecisionsRisk')} subtitle={t('aiTrader.signalsBlockedBySafeguards')} action={t('common.viewAll')} onAction={() => setActiveTab('Decisions')} /><RejectedTable /></section></div><section className="panel trades-panel"><PanelHeading title={t('aiTrader.recentTrades')} subtitle={t('aiTrader.latestExecutedOrders')} action={t('common.viewAll')} onAction={() => setActiveTab('Trades')} /><TradesTable compact /></section><div className="overview-grid overview-grid-bottom"><PerformanceStatsPanel /><ModelPanel /><BacktestPanel /></div></>
}

function TabContent({ activeTab, setActiveTab }: { activeTab: TabName; setActiveTab: (tab: TabName) => void }) {
  const { t } = useTranslation()
  if (activeTab === 'Overview') return <Overview setActiveTab={setActiveTab} />
  if (activeTab === 'Positions') return <section className="panel standalone-panel"><PanelHeading title={t('aiTrader.positions')} subtitle={t('aiTrader.allOpenHoldings')} /><PositionTable /></section>
  if (activeTab === 'Trades') return <section className="panel standalone-panel"><PanelHeading title={t('aiTrader.trades')} subtitle={t('aiTrader.completeExecutionHistory')} /><TradesTable /></section>
  if (activeTab === 'Decisions') return <div className="standalone-grid"><section className="panel standalone-panel"><PanelHeading title={t('aiTrader.decisions')} subtitle={t('aiTrader.signalsProducedByModel')} /><DecisionTable /></section><section className="panel standalone-panel"><PanelHeading title={t('aiTrader.rejectedDecisions')} subtitle={t('aiTrader.signalsBlockedByRisk')} /><RejectedTable /></section></div>
  if (activeTab === 'Performance') return <><section className="panel standalone-panel"><PanelHeading title={t('dashboard.performanceTitle')} subtitle={t('aiTrader.detailedPerformance')} /><PerformanceChart /></section><PerformanceStatsPanel /></>
  if (activeTab === 'Model') return <div className="standalone-grid"><ModelPanel detailed /><section className="panel standalone-panel model-notes"><PanelHeading title={t('aiTrader.modelMonitoring')} subtitle={t('aiTrader.currentSafeguards')} /><div className="monitor-list"><div><span className="monitor-dot green" /><span><strong>{t('aiTrader.dataPipeline')}</strong><small>{t('aiTrader.marketDataReceived')}</small></span><b>{t('aiTrader.healthy')}</b></div><div><span className="monitor-dot blue" /><span><strong>{t('aiTrader.riskControls')}</strong><small>{t('aiTrader.constraintsActive')}</small></span><b>{t('aiTrader.enabled')}</b></div><div><span className="monitor-dot purple" /><span><strong>{t('aiTrader.featureDrift')}</strong><small>{t('aiTrader.noSignificantDrift')}</small></span><b>{t('aiTrader.stable')}</b></div></div></section></div>
  return <div className="standalone-grid"><BacktestPanel detailed /><section className="panel standalone-panel backtest-notes"><PanelHeading title={t('aiTrader.backtestConfiguration')} subtitle={t('aiTrader.simulationSettings')} /><div className="config-list"><div><span>{t('aiTrader.startingCapital')}</span><strong>{formatCurrency(100000)}</strong></div><div><span>{t('aiTrader.strategy')}</span><strong>{t('aiTrader.strategies.momentumRisk')}</strong></div><div><span>{t('aiTrader.period')}</span><strong>{t('aiTrader.periodValue')}</strong></div><div><span>{t('aiTrader.tradingUniverse')}</span><strong>{t('aiTrader.strategies.usLargeCap')}</strong></div></div><button className="primary-button" onClick={() => undefined} type="button"><Icon name="play" size={14} /> {t('aiTrader.runNewBacktest')}</button></section></div>
}

export default function AITraderPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('aiTrader.title')} | StockLab`
  }, [i18n.language, t])
  const [activeTab, setActiveTab] = useState<TabName>('Overview')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [botActive, setBotActive] = useState(true)
  const [toast, setToast] = useState('')
  const showToast = (message: string) => { setToast(message); window.setTimeout(() => setToast(''), 2300) }
  return <div className="ai-trader-page stocklab-layout"><Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} /><main className="ai-trader-main"><header className="ai-trader-topbar"><button aria-label={t('common.openNavigation')} className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={20} /></button><div className="breadcrumb"><strong>{t('aiTrader.title')}</strong></div><div className="topbar-actions"><label className="global-search"><Icon name="search" size={16} /><input aria-label={t('common.searchStocks')} placeholder={t('common.searchStocksEtfsNewsPlaceholder')} /></label><button aria-label={t('common.notifications')} className="icon-button notification-button" onClick={() => showToast(t('common.notificationsCaughtUp'))} type="button"><Icon name="bell" size={18} /><i>2</i></button><button aria-label={t('common.openMessages')} className="icon-button mail-button" onClick={() => showToast(t('common.noNewMessages'))} type="button"><Icon name="mail" size={17} /></button><button aria-label={t('common.openAccountMenu')} className="topbar-account" onClick={() => window.location.assign(routeFor('profile'))} type="button"><span className="topbar-avatar">MS</span><Icon name="chevron-down" size={14} /></button></div></header><div className="ai-trader-content"><section className="ai-trader-heading"><div><h1>{t('aiTrader.title')}</h1><p>{t('aiTrader.subtitle')}</p></div><div className="heading-actions"><button className={`bot-status ${botActive ? 'is-active' : 'is-paused'}`} onClick={() => { setBotActive((current) => !current); showToast(botActive ? t('aiTrader.pausedToast') : t('aiTrader.activeToast')) }} type="button"><i /> {t('aiTrader.title')} {botActive ? t('aiTrader.botActive') : t('aiTrader.botPaused')} <Icon name="chevron-down" size={13} /></button><button className="secondary-button" onClick={() => window.location.assign(routeFor('profile'))} type="button"><Icon name="settings" size={13} /> {t('aiTrader.settings')}</button><button className="primary-button" onClick={() => { setActiveTab('Backtests'); showToast(t('aiTrader.backtestOpenedToast')) }} type="button"><Icon name="play" size={13} /> {t('aiTrader.startNewBacktest')}</button></div></section><div aria-label={t('aiTrader.sections')} className="ai-tabs" role="tablist">{tabs.map((tab) => <button aria-selected={activeTab === tab} className={activeTab === tab ? 'selected' : ''} key={tab} onClick={() => setActiveTab(tab)} role="tab" type="button">{t(`aiTrader.tabs.${tab.toLowerCase()}`)}</button>)}</div><TabContent activeTab={activeTab} setActiveTab={setActiveTab} /><p className="simulation-note"><Icon name="activity" size={13} /> {t('aiTrader.simulationNote')}</p></div></main><div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast}</div></div>
}
