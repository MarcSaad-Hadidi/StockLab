import { StockLogo, LogoAttribution } from '../market/StockLogo'
import { UnavailableState } from '../components/UnavailableState'
import { PerformanceLineChart } from '../components/charts/PerformanceLineChart'
import { Sidebar } from '../components/layout/Sidebar'
import { TopBar } from '../components/layout/TopBar'
import { getTrendClass, getTrendIcon, getTrendTone, type TrendTone } from '../components/trend/trend'
import { formatCurrency, formatNumber, formatPercent, formatSignedCurrency, formatSignedPercent } from '../i18n/formatters'
import { routeFor } from '../navigation/routes'
import { useTranslation } from 'react-i18next'
import { useEffect, useState, type ReactNode } from 'react'
import {
  backtestSummary,
  currentDecisions,
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
  | 'chevron-down'
  | 'chevron-right'
  | 'play'
  | 'settings'
  | 'trending-down'
  | 'trending-up'

function Icon({ name, size = 18 }: { name: IconName; size?: number }) {
  const common = { fill: 'none', stroke: 'currentColor', strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const, strokeWidth: 1.8 }
  const paths: Record<IconName, ReactNode> = {
    activity: <path d="M3 12h3l2.2-6 3.6 12 2.2-6H21" {...common} />,
    'chevron-down': <path d="m6 9 6 6 6-6" {...common} />,
    'chevron-right': <path d="m9 6 6 6-6 6" {...common} />,
    play: <path d="m8 5 11 7-11 7V5Z" {...common} />,
    settings: <><circle cx="12" cy="12" r="3" {...common} /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-1.7 1.7-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5v.2h-2.4v-.2a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1L8 17l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H6.7v-2.4h.2a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9L8 8.6l1.7-1.7.1.1a1.7 1.7 0 0 0 1.9.3 1.7 1.7 0 0 0 1-1.5v-.2h2.4v.2a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1 1.7 1.7-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.2V14h-.2a1.7 1.7 0 0 0-1.5 1Z" {...common} /></>,
    'trending-down': <><path d="M3 7 9 13l4-4 8 9" {...common} /><path d="M15 18h6v-6" {...common} /></>,
    'trending-up': <><path d="M3 17 9 11l4 4 8-9" {...common} /><path d="M15 6h6v6" {...common} /></>,
  }
  return <svg aria-hidden="true" className="icon" height={size} viewBox="0 0 24 24" width={size}>{paths[name]}</svg>
}

function StockMark({ symbol }: { symbol: string }) { return <StockLogo symbol={symbol} /> }

function ActionBadge({ action }: { action: TraderAction }) {
  const { t } = useTranslation()
  return <span className={`action-badge action-${action.toLowerCase()}`}>{t(`aiTrader.actions.${action.toLowerCase()}`)}</span>
}

function PanelHeading({ title, subtitle, action, onAction }: { title: string; subtitle?: string; action?: string; onAction?: () => void }) {
  return <div className="panel-heading"><div><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</div>{action && <button className="text-action" onClick={onAction} type="button">{action}<Icon name="chevron-right" size={14} /></button>}</div>
}

function MetricCard({ label, value, change, tone, trend = 'neutral' }: { label: string; value: string; change?: string; tone: string; trend?: TrendTone }) {
  const trendIcon = getTrendIcon(trend)
  return <article className={`metric-card metric-${tone}`}><span className="metric-label">{label}</span><strong>{value}</strong>{change && <small className={`metric-change ${getTrendClass(trend)}`}>{trendIcon && <Icon name={trendIcon} size={13} />} {change}</small>}</article>
}

function PerformanceChart() {
  const { i18n, t } = useTranslation()
  const labels = performanceLabels.map(label => t(label))
  return (
    <PerformanceLineChart
      ariaLabel={t('aiTrader.performanceChart')}
      formatValue={value => formatCurrency(value, i18n.language)}
      formatTick={value => formatCurrency(value, i18n.language, 0)}
      labels={labels}
      pointLabel={index => t('aiTrader.chartPoint', { label: labels[index], value: formatCurrency(performanceSeries[index], i18n.language) })}
      size="compact"
      unavailableMessage="businessData.portfolio"
      values={performanceSeries}
    />
  )
}

function PositionTable({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation()
  const rows = compact ? positions.slice(0, 5) : positions
  return <div className="table-scroll"><table className="data-table position-table"><thead><tr><th>{t('aiTrader.table.asset')}</th><th>{t('aiTrader.table.size')}</th><th>{t('aiTrader.table.entryPrice')}</th><th>{t('aiTrader.table.currentPrice')}</th><th>{t('aiTrader.table.pnlDollar')}</th><th>{t('aiTrader.table.pnlPercent')}</th></tr></thead><tbody>{rows.length === 0 && <tr><td colSpan={7}><UnavailableState message="businessData.ai" /></td></tr>}{rows.map((position) => <tr key={position.symbol}><td><div className="asset-cell"><StockMark symbol={position.symbol} /><span><strong>{position.symbol}</strong><small>{position.company}</small></span></div></td><td>{position.size}</td><td>{formatCurrency(position.entryPrice)}</td><td>{formatCurrency(position.currentPrice)}</td><td className={position.pnl >= 0 ? 'positive' : 'negative'}>{formatSignedCurrency(position.pnl)}</td><td><span className={position.pnlPercent >= 0 ? 'positive' : 'negative'}>{formatSignedPercent(position.pnlPercent)}</span></td></tr>)}</tbody></table></div>
}

function DecisionTable() {
  const { t } = useTranslation()
  return <div className="table-scroll"><table className="data-table decision-table"><thead><tr><th>{t('aiTrader.table.asset')}</th><th>{t('aiTrader.table.action')}</th><th>{t('aiTrader.table.confidence')}</th><th>{t('aiTrader.table.target')}</th><th>{t('aiTrader.table.stopLoss')}</th></tr></thead><tbody>{currentDecisions.length === 0 && <tr><td colSpan={5}><UnavailableState message="businessData.decisions" /></td></tr>}{currentDecisions.map((decision) => <tr key={decision.symbol}><td><div className="asset-cell compact-asset"><StockMark symbol={decision.symbol} /><span><strong>{decision.symbol}</strong><small>{decision.company}</small></span></div></td><td><ActionBadge action={decision.action} /></td><td>{formatPercent(decision.confidence, undefined, 0)}</td><td>{formatCurrency(decision.targetPrice)}</td><td>{formatCurrency(decision.stopLoss)}</td></tr>)}</tbody></table></div>
}

function RejectedTable() {
  const { t } = useTranslation()
  return <div className="table-scroll"><table className="data-table rejected-table"><thead><tr><th>{t('aiTrader.table.asset')}</th><th>{t('aiTrader.table.action')}</th><th>{t('aiTrader.table.reason')}</th><th>{t('aiTrader.table.confidence')}</th><th>{t('aiTrader.table.time')}</th></tr></thead><tbody>{rejectedDecisions.length === 0 && <tr><td colSpan={5}><UnavailableState message="businessData.decisions" /></td></tr>}{rejectedDecisions.map((decision) => <tr key={`${decision.symbol}-${decision.timeKey}`}><td><strong>{decision.symbol}</strong></td><td><ActionBadge action={decision.action} /></td><td className="reason-cell">{t(decision.reasonKey)}</td><td>{formatPercent(decision.confidence, undefined, 0)}</td><td className="time-cell">{t(decision.timeKey)}</td></tr>)}</tbody></table></div>
}

function TradesTable({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation()
  const rows = compact ? recentTrades.slice(0, 4) : recentTrades
  return <div className="table-scroll"><table className="data-table trades-table"><thead><tr><th>{t('aiTrader.table.asset')}</th><th>{t('aiTrader.table.side')}</th><th>{t('aiTrader.table.entryExit')}</th><th>{t('aiTrader.table.price')}</th><th>{t('aiTrader.table.pnlDollar')}</th><th>{t('aiTrader.table.pnlPercent')}</th><th>{t('aiTrader.table.time')}</th></tr></thead><tbody>{rows.length === 0 && <tr><td colSpan={7}><UnavailableState message="businessData.ai" /></td></tr>}{rows.map((trade) => <tr key={`${trade.symbol}-${trade.timeKey}`}><td><strong>{trade.symbol}</strong></td><td><ActionBadge action={trade.side} /></td><td>{t(`aiTrader.table.${trade.type.toLowerCase()}`)}</td><td>{formatCurrency(trade.price)}</td><td className={trade.pnl >= 0 ? 'positive' : 'negative'}>{formatSignedCurrency(trade.pnl)}</td><td className={trade.pnlPercent >= 0 ? 'positive' : 'negative'}>{formatSignedPercent(trade.pnlPercent)}</td><td className="time-cell">{t(trade.timeKey)}</td></tr>)}</tbody></table></div>
}

function Sparkline({ tone }: { tone?: string }) { return <div data-tone={tone}><UnavailableState message="businessData.ai" /></div> }

function PerformanceStatsPanel() {
  const { t } = useTranslation()
  return <section className="panel stats-panel"><PanelHeading title={t('aiTrader.performanceStatistics')} subtitle={t('aiTrader.recentBotPerformance')} /><div className="stats-content"><div className="stat-list"><div><span>{t('aiTrader.totalReturn')}</span><strong className="positive">{'—'}</strong></div><div><span>{t('aiTrader.accuracy')}</span><strong>{'—'}</strong></div><div><span>{t('aiTrader.winRate')}</span><strong>{'—'}</strong></div><div><span>{t('aiTrader.averageWin')}</span><strong className="positive">{'—'}</strong></div><div><span>{t('aiTrader.totalTrades')}</span><strong>{'—'}</strong></div><div><span>{t('aiTrader.bestTrade')}</span><strong className="positive">{'—'}</strong></div></div><Sparkline /></div></section>
}

function ModelPanel({ detailed = false }: { detailed?: boolean }) {
  const { t } = useTranslation()
  return <section className={`panel model-panel ${detailed ? 'model-panel-detailed' : ''}`}><PanelHeading title={t('aiTrader.modelVersion')} subtitle={t('aiTrader.modelHealth')} /><UnavailableState message="businessData.model" /><div className="model-metrics">{['accuracy','precision','recall','f1Score','trainedOn','nextRetrain'].map(key => <div key={key}><span>{t(`aiTrader.${key}`)}</span><strong>—</strong></div>)}</div><button disabled className="outline-button" type="button">{t('aiTrader.viewModelDetails')}</button></section>
}

function BacktestPanel({ detailed = false }: { detailed?: boolean }) {
  const { t } = useTranslation()
  return <section className={`panel backtest-panel ${detailed ? 'backtest-panel-detailed' : ''}`}><PanelHeading title={t('aiTrader.backtestingResults')} subtitle={t('aiTrader.historicalSimulation')} /><div className="backtest-header"><span>{t('aiTrader.lastSixMonths')}</span><Icon name="chevron-down" size={13} /></div><div className="backtest-content"><div className="backtest-metrics"><div><span>{t('aiTrader.totalReturn')}</span><strong className="positive">{formatSignedPercent(backtestSummary.totalReturn)}</strong></div><div><span>{t('aiTrader.winRate')}</span><strong>{formatPercent(backtestSummary.winRate, undefined, 1)}</strong></div><div><span>{t('aiTrader.maxDrawdown')}</span><strong className="negative">{formatSignedPercent(backtestSummary.maxDrawdown)}</strong></div><div><span>{t('aiTrader.sharpeRatio')}</span><strong>{formatNumber(backtestSummary.sharpeRatio)}</strong></div><div><span>{t('aiTrader.totalTrades')}</span><strong>{formatNumber(backtestSummary.totalTrades, undefined, 0)}</strong></div><div><span>{t('aiTrader.profitFactor')}</span><strong>{formatNumber(backtestSummary.profitFactor)}</strong></div></div><Sparkline tone="purple" /></div>{detailed && <div className="backtest-history"><div><span>{t('businessData.ai')}</span><strong className="positive">{'—'}</strong></div><div><span>{t('businessData.ai')}</span><strong className="positive">{'—'}</strong></div><div><span>{t('businessData.ai')}</span><strong className="positive">{'—'}</strong></div></div>}<button className="outline-button" disabled title={t('businessData.unavailable')} type="button">{t('aiTrader.viewFullBacktest')} <Icon name="chevron-right" size={13} /></button></section>
}

const tabs = ['Overview', 'Positions', 'Trades', 'Decisions', 'Performance', 'Model', 'Backtests'] as const
type TabName = typeof tabs[number]

function Overview({ setActiveTab }: { setActiveTab: (tab: TabName) => void }) {
  const { t } = useTranslation()
  return <><section className="metric-grid"><MetricCard change={'—'} label={t('aiTrader.initialCapital')} tone="blue" value={formatCurrency(traderSummary.initialCapital)} /><MetricCard change={formatSignedPercent(traderSummary.returnPercent)} label={t('aiTrader.currentValueMetric')} tone="green" trend={getTrendTone(traderSummary.returnPercent)} value={formatCurrency(traderSummary.currentValue)} /><MetricCard change={formatSignedPercent(traderSummary.returnPercent)} label={t('aiTrader.totalProfitLoss')} tone="purple" trend={getTrendTone(traderSummary.returnPercent)} value={formatSignedCurrency(traderSummary.profitLoss)} /><MetricCard change={'—'} label={t('aiTrader.winRate')} tone="orange" value={formatPercent(traderSummary.winRate, undefined, 1)} /><MetricCard change={formatSignedPercent(traderSummary.maxDrawdown)} label={t('aiTrader.maxDrawdown')} tone="red" trend={getTrendTone(traderSummary.maxDrawdown)} value={formatSignedPercent(traderSummary.maxDrawdown)} /></section><section className="panel performance-panel"><PanelHeading title={t('dashboard.performanceTitle')} subtitle={t('aiTrader.portfolioValueOverTime')} action={t('aiTrader.viewDetails')} onAction={() => setActiveTab('Performance')} /><div className="range-tabs" role="tablist"><button className="selected" type="button">{t('common.timeRanges.1M')}</button><button type="button">{t('common.timeRanges.3M')}</button><button type="button">{t('common.timeRanges.6M')}</button><button type="button">{t('common.timeRanges.1Y')}</button><button type="button">{t('common.timeRanges.ALL')}</button></div><PerformanceChart /></section><div className="overview-grid overview-grid-top"><section className="panel positions-panel"><PanelHeading title={t('aiTrader.openPositions', { count: positions.length })} subtitle={t('aiTrader.currentBotHoldings')} action={t('common.viewAll')} onAction={() => setActiveTab('Positions')} /><PositionTable compact /><span className="panel-footnote">{t('aiTrader.totalUnrealizedPnl')} <strong className="positive">{'—'} ({'—'})</strong></span></section><section className="panel decisions-panel"><PanelHeading title={t('aiTrader.currentDecisions')} subtitle={t('aiTrader.latestModelSignals')} action={t('common.viewAll')} onAction={() => setActiveTab('Decisions')} /><DecisionTable /></section><section className="panel rejected-panel"><PanelHeading title={t('aiTrader.rejectedDecisionsRisk')} subtitle={t('aiTrader.signalsBlockedBySafeguards')} action={t('common.viewAll')} onAction={() => setActiveTab('Decisions')} /><RejectedTable /></section></div><section className="panel trades-panel"><PanelHeading title={t('aiTrader.recentTrades')} subtitle={t('aiTrader.latestExecutedOrders')} action={t('common.viewAll')} onAction={() => setActiveTab('Trades')} /><TradesTable compact /></section><div className="overview-grid overview-grid-bottom"><PerformanceStatsPanel /><ModelPanel /><BacktestPanel /></div></>
}

function TabContent({ activeTab, setActiveTab }: { activeTab: TabName; setActiveTab: (tab: TabName) => void }) {
  const { t } = useTranslation()
  if (activeTab === 'Overview') return <Overview setActiveTab={setActiveTab} />
  if (activeTab === 'Positions') return <section className="panel standalone-panel"><PanelHeading title={t('aiTrader.positions')} subtitle={t('aiTrader.allOpenHoldings')} /><PositionTable /></section>
  if (activeTab === 'Trades') return <section className="panel standalone-panel"><PanelHeading title={t('aiTrader.trades')} subtitle={t('aiTrader.completeExecutionHistory')} /><TradesTable /></section>
  if (activeTab === 'Decisions') return <div className="standalone-grid"><section className="panel standalone-panel"><PanelHeading title={t('aiTrader.decisions')} subtitle={t('aiTrader.signalsProducedByModel')} /><DecisionTable /></section><section className="panel standalone-panel"><PanelHeading title={t('aiTrader.rejectedDecisions')} subtitle={t('aiTrader.signalsBlockedByRisk')} /><RejectedTable /></section></div>
  if (activeTab === 'Performance') return <><section className="panel standalone-panel"><PanelHeading title={t('dashboard.performanceTitle')} subtitle={t('aiTrader.detailedPerformance')} /><PerformanceChart /></section><PerformanceStatsPanel /></>
  if (activeTab === 'Model') return <div className="standalone-grid"><ModelPanel detailed /><section className="panel standalone-panel model-notes"><PanelHeading title={t('aiTrader.modelMonitoring')} subtitle={t('aiTrader.currentSafeguards')} /><div className="monitor-list"><div><span className="monitor-dot green" /><span><strong>{t('aiTrader.dataPipeline')}</strong><small>{t('businessData.ai')}</small></span><b>{t('businessData.ai')}</b></div><div><span className="monitor-dot blue" /><span><strong>{t('aiTrader.riskControls')}</strong><small>{t('businessData.ai')}</small></span><b>{t('businessData.ai')}</b></div><div><span className="monitor-dot purple" /><span><strong>{t('aiTrader.featureDrift')}</strong><small>{t('businessData.ai')}</small></span><b>{t('businessData.ai')}</b></div></div></section></div>
  return <div className="standalone-grid"><BacktestPanel detailed /><section className="panel standalone-panel backtest-notes"><PanelHeading title={t('aiTrader.backtestConfiguration')} subtitle={t('aiTrader.simulationSettings')} /><div className="config-list"><div><span>{t('aiTrader.startingCapital')}</span><strong>{'—'}</strong></div><div><span>{t('aiTrader.strategy')}</span><strong>{t('businessData.ai')}</strong></div><div><span>{t('aiTrader.period')}</span><strong>{t('businessData.ai')}</strong></div><div><span>{t('aiTrader.tradingUniverse')}</span><strong>{t('businessData.ai')}</strong></div></div><button className="primary-button" disabled title={t('businessData.unavailable')} type="button"><Icon name="play" size={14} /> {t('aiTrader.runNewBacktest')}</button></section></div>
}

export default function AITraderPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('aiTrader.title')} | StockLab`
  }, [i18n.language, t])
  const [activeTab, setActiveTab] = useState<TabName>('Overview')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  return <div className="ai-trader-page stocklab-layout"><Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} /><main className="ai-trader-main"><TopBar onMenuOpen={() => setSidebarOpen(true)} title={t('aiTrader.title')} /><div className="ai-trader-content"><section className="ai-trader-heading"><div><h1>{t('aiTrader.title')}</h1><p>{t('aiTrader.subtitle')}</p></div><div className="heading-actions"><button className="bot-status is-paused" disabled type="button">{t('businessData.ai')}</button><button className="secondary-button" onClick={() => window.location.assign(routeFor('profile'))} type="button"><Icon name="settings" size={13} /> {t('aiTrader.settings')}</button><button className="primary-button" disabled title={t('businessData.unavailable')} type="button"><Icon name="play" size={13} /> {t('aiTrader.startNewBacktest')}</button></div></section><div aria-label={t('aiTrader.sections')} className="ai-tabs" role="tablist">{tabs.map((tab) => <button aria-selected={activeTab === tab} className={activeTab === tab ? 'selected' : ''} key={tab} onClick={() => setActiveTab(tab)} role="tab" type="button">{t(`aiTrader.tabs.${tab.toLowerCase()}`)}</button>)}</div><TabContent activeTab={activeTab} setActiveTab={setActiveTab} /><p className="simulation-note"><Icon name="activity" size={13} /> {t('businessData.ai')}</p></div><LogoAttribution /></main></div>
}
