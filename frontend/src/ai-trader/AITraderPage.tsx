import { StockLogo } from '../market/StockLogo'
import { UnavailableState } from '../components/UnavailableState'
import { Sidebar } from '../components/layout/Sidebar'
import { formatCurrency, formatNumber, formatPercent, formatSignedCurrency, formatSignedPercent } from '../i18n/formatters'
import { routeFor } from '../navigation/routes'
import { useTranslation } from 'react-i18next'
import { useEffect, useState, type ReactNode } from 'react'
import {
  backtestSummary,
  currentDecisions,
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

function StockMark({ symbol }: { symbol: string }) { return <StockLogo symbol={symbol} /> }

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

function PerformanceChart() { return <UnavailableState message="businessData.portfolio" /> }

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
  return <><section className="metric-grid"><MetricCard change={'—'} icon="wallet" label={t('aiTrader.initialCapital')} tone="blue" value={formatCurrency(traderSummary.initialCapital)} /><MetricCard change={formatSignedPercent(traderSummary.returnPercent)} icon="chart" label={t('aiTrader.currentValueMetric')} tone="green" value={formatCurrency(traderSummary.currentValue)} /><MetricCard change={formatSignedPercent(traderSummary.returnPercent)} icon="trending-up" label={t('aiTrader.totalProfitLoss')} tone="purple" value={formatSignedCurrency(traderSummary.profitLoss)} /><MetricCard change={'—'} icon="activity" label={t('aiTrader.winRate')} tone="orange" value={formatPercent(traderSummary.winRate, undefined, 1)} /><MetricCard change={`↓ ${'—'}`} icon="pie-chart" label={t('aiTrader.maxDrawdown')} tone="red" value={formatSignedPercent(traderSummary.maxDrawdown)} /></section><section className="panel performance-panel"><PanelHeading title={t('dashboard.performanceTitle')} subtitle={t('aiTrader.portfolioValueOverTime')} action={t('aiTrader.viewDetails')} onAction={() => setActiveTab('Performance')} /><div className="range-tabs" role="tablist"><button className="selected" type="button">{t('common.timeRanges.1M')}</button><button type="button">{t('common.timeRanges.3M')}</button><button type="button">{t('common.timeRanges.6M')}</button><button type="button">{t('common.timeRanges.1Y')}</button><button type="button">{t('common.timeRanges.ALL')}</button></div><PerformanceChart /></section><div className="overview-grid overview-grid-top"><section className="panel positions-panel"><PanelHeading title={t('aiTrader.openPositions', { count: positions.length })} subtitle={t('aiTrader.currentBotHoldings')} action={t('common.viewAll')} onAction={() => setActiveTab('Positions')} /><PositionTable compact /><span className="panel-footnote">{t('aiTrader.totalUnrealizedPnl')} <strong className="positive">{'—'} ({'—'})</strong></span></section><section className="panel decisions-panel"><PanelHeading title={t('aiTrader.currentDecisions')} subtitle={t('aiTrader.latestModelSignals')} action={t('common.viewAll')} onAction={() => setActiveTab('Decisions')} /><DecisionTable /></section><section className="panel rejected-panel"><PanelHeading title={t('aiTrader.rejectedDecisionsRisk')} subtitle={t('aiTrader.signalsBlockedBySafeguards')} action={t('common.viewAll')} onAction={() => setActiveTab('Decisions')} /><RejectedTable /></section></div><section className="panel trades-panel"><PanelHeading title={t('aiTrader.recentTrades')} subtitle={t('aiTrader.latestExecutedOrders')} action={t('common.viewAll')} onAction={() => setActiveTab('Trades')} /><TradesTable compact /></section><div className="overview-grid overview-grid-bottom"><PerformanceStatsPanel /><ModelPanel /><BacktestPanel /></div></>
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
  const [toast, setToast] = useState('')
  const showToast = (message: string) => { setToast(message); window.setTimeout(() => setToast(''), 2300) }
  return <div className="ai-trader-page stocklab-layout"><Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} /><main className="ai-trader-main"><header className="ai-trader-topbar"><button aria-label={t('common.openNavigation')} className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={20} /></button><div className="breadcrumb"><strong>{t('aiTrader.title')}</strong></div><div className="topbar-actions"><label className="global-search"><Icon name="search" size={16} /><input aria-label={t('common.searchStocks')} placeholder={t('common.searchStocksEtfsNewsPlaceholder')} /></label><button aria-label={t('common.notifications')} className="icon-button notification-button" onClick={() => showToast('common.notificationsCaughtUp')} type="button"><Icon name="bell" size={18} /></button><button aria-label={t('common.openMessages')} className="icon-button mail-button" onClick={() => showToast('common.noNewMessages')} type="button"><Icon name="mail" size={17} /></button><button aria-label={t('common.openAccountMenu')} className="topbar-account" onClick={() => window.location.assign(routeFor('profile'))} type="button"><span className="topbar-avatar">MS</span><Icon name="chevron-down" size={14} /></button></div></header><div className="ai-trader-content"><section className="ai-trader-heading"><div><h1>{t('aiTrader.title')}</h1><p>{t('aiTrader.subtitle')}</p></div><div className="heading-actions"><button className="bot-status is-paused" disabled type="button">{t('businessData.ai')}</button><button className="secondary-button" onClick={() => window.location.assign(routeFor('profile'))} type="button"><Icon name="settings" size={13} /> {t('aiTrader.settings')}</button><button className="primary-button" disabled title={t('businessData.unavailable')} type="button"><Icon name="play" size={13} /> {t('aiTrader.startNewBacktest')}</button></div></section><div aria-label={t('aiTrader.sections')} className="ai-tabs" role="tablist">{tabs.map((tab) => <button aria-selected={activeTab === tab} className={activeTab === tab ? 'selected' : ''} key={tab} onClick={() => setActiveTab(tab)} role="tab" type="button">{t(`aiTrader.tabs.${tab.toLowerCase()}`)}</button>)}</div><TabContent activeTab={activeTab} setActiveTab={setActiveTab} /><p className="simulation-note"><Icon name="activity" size={13} /> {t('businessData.ai')}</p></div></main><div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast ? t(toast) : ''}</div></div>
}
