import { useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { aiTraderApi, type AiTraderApi } from '../api/aiTraderApi'
import { LogoAttribution } from '../market/StockLogo'
import { Sidebar } from '../components/layout/Sidebar'
import { TopBar } from '../components/layout/TopBar'
import { getTrendClass, getTrendIcon, getTrendTone, type TrendTone } from '../components/trend/trend'
import { formatPercent, formatSignedPercent } from '../i18n/formatters'
import { formatAiCurrency as formatCurrency, formatAiSignedCurrency as formatSignedCurrency, fractionPoints, tabs, type EquityRange, type TabName } from './aiTraderData'
import { useAiTraderData, type AiTraderDataState } from './useAiTraderData'
import { BacktestPanel, DecisionTable, DetailPanel, EquityPanel, ModelPanel, PanelHeading, PerformanceStatsPanel, PositionTable, RejectedTable, ResourceContent, TradesTable, type DetailSelection } from './AiTraderPanels'
import './ai-trader.css'

type IconName = 'activity' | 'trending-down' | 'trending-up'
function Icon({ name, size = 18 }: { name: IconName; size?: number }) {
  const common = { fill: 'none', stroke: 'currentColor', strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const, strokeWidth: 1.8 }
  const paths: Record<IconName, ReactNode> = {
    activity: <path d="M3 12h3l2.2-6 3.6 12 2.2-6H21" {...common} />,
    'trending-down': <><path d="M3 7 9 13l4-4 8 9" {...common} /><path d="M15 18h6v-6" {...common} /></>,
    'trending-up': <><path d="M3 17 9 11l4 4 8-9" {...common} /><path d="M15 6h6v6" {...common} /></>,
  }
  return <svg aria-hidden="true" className="icon" height={size} viewBox="0 0 24 24" width={size}>{paths[name]}</svg>
}
function MetricCard({ label, value, change, tone, trend = 'neutral' }: { label: string; value: string; change?: string; tone: string; trend?: TrendTone }) {
  const trendIcon = getTrendIcon(trend)
  return <article className={`metric-card metric-${tone}`}><span className="metric-label">{label}</span><strong title={value}>{value}</strong>{change && <small className={`metric-change ${getTrendClass(trend)}`}>{trendIcon && <Icon name={trendIcon} size={13} />} {change}</small>}</article>
}
function OverviewMetrics({ resources, retry }: AiTraderDataState) {
  const { t, i18n } = useTranslation()
  return <ResourceContent resource={resources.performance} retry={() => retry('performance')}>{data => {
    const percent = fractionPoints(data.totalReturn)
    return <section className="metric-grid">
      <MetricCard label={t('aiTrader.initialCapital')} tone="blue" value={formatCurrency(data.initialCapital, i18n.language, 2, data.currency)} />
      <MetricCard label={t('aiTrader.currentValueMetric')} tone="green" value={formatCurrency(data.totalValue, i18n.language, 2, data.currency)} change={formatSignedPercent(percent, i18n.language)} trend={getTrendTone(percent)} />
      <MetricCard label={t('aiTrader.totalProfitLoss')} tone="purple" value={formatSignedCurrency(data.totalPnL, i18n.language, 2, data.currency)} change={formatSignedPercent(percent, i18n.language)} trend={getTrendTone(percent)} />
      <MetricCard label={t('aiTrader.winRate')} tone="orange" value={formatPercent(fractionPoints(data.winRate), i18n.language, 1)} />
      <MetricCard label={t('aiTrader.maxDrawdown')} tone="red" value={formatPercent(fractionPoints(data.maxDrawdown), i18n.language)} />
    </section>
  }}</ResourceContent>
}
type ContentProps = { data: AiTraderDataState; activeTab: TabName; setActiveTab: (tab: TabName) => void; range: EquityRange; setRange: (range: EquityRange) => void; selectDetail: (selection: DetailSelection) => void }
function TabContent({ data, activeTab, setActiveTab, range, setRange, selectDetail }: ContentProps) {
  const { t, i18n } = useTranslation()
  const props = { resources: data.resources, retry: data.retry }
  const chart = <EquityPanel {...props} range={range} setRange={setRange} />
  if (activeTab === 'Positions') return <section className="panel standalone-panel"><PanelHeading title={t('aiTrader.positions')} subtitle={t('aiTrader.allOpenHoldings')} /><PositionTable {...props} /></section>
  if (activeTab === 'Trades') return <section className="panel standalone-panel"><PanelHeading title={t('aiTrader.recentTrades')} subtitle={t('aiTrader.latestExecutedOrders')} /><TradesTable {...props} selectDetail={selectDetail} /></section>
  if (activeTab === 'Decisions') return <div className="standalone-grid"><section className="panel standalone-panel"><PanelHeading title={t('aiTrader.decisions')} subtitle={t('aiTrader.signalsProducedByModel')} /><DecisionTable {...props} selectDetail={selectDetail} /><p className="ai-coverage-note">{t('aiTrader.recentLimit', { count: 200 })}</p></section><section className="panel standalone-panel"><PanelHeading title={t('aiTrader.rejectedDecisions')} subtitle={t('aiTrader.signalsBlockedByRisk')} /><RejectedTable {...props} /><p className="ai-coverage-note">{t('aiTrader.recentLimit', { count: 200 })}</p></section></div>
  if (activeTab === 'Performance') return <>{chart}<PerformanceStatsPanel {...props} /></>
  if (activeTab === 'Model') return <div className="standalone-grid"><ModelPanel {...props} detailed /></div>
  if (activeTab === 'Backtests') return <div className="standalone-grid"><BacktestPanel {...props} detailed /></div>
  const performance = data.resources.performance.data
  return <><OverviewMetrics {...data} />{chart}
    <div className="overview-grid overview-grid-top">
      <section className="panel positions-panel"><PanelHeading title={data.resources.positions.data ? t('aiTrader.openPositions', { count: data.resources.positions.data.length }) : t('aiTrader.positions')} subtitle={t('aiTrader.currentBotHoldings')} action={t('common.viewAll')} onAction={() => setActiveTab('Positions')} /><PositionTable {...props} compact />
        {performance && <span className="panel-footnote">{t('aiTrader.totalUnrealizedPnl')} <strong>{formatSignedCurrency(performance.unrealizedPnL, i18n.language, 2, performance.currency)}</strong></span>}</section>
      <section className="panel decisions-panel"><PanelHeading title={t('aiTrader.currentDecisions')} subtitle={t('aiTrader.latestModelSignals')} action={t('common.viewAll')} onAction={() => setActiveTab('Decisions')} /><DecisionTable {...props} selectDetail={selectDetail} compact /></section>
      <section className="panel rejected-panel"><PanelHeading title={t('aiTrader.rejectedDecisionsRisk')} subtitle={t('aiTrader.signalsBlockedBySafeguards')} action={t('common.viewAll')} onAction={() => setActiveTab('Decisions')} /><RejectedTable {...props} compact /></section>
    </div>
    <section className="panel trades-panel"><PanelHeading title={t('aiTrader.recentTrades')} subtitle={t('aiTrader.latestExecutedOrders')} action={t('common.viewAll')} onAction={() => setActiveTab('Trades')} /><TradesTable {...props} selectDetail={selectDetail} compact /></section>
    <div className="overview-grid overview-grid-bottom"><PerformanceStatsPanel {...props} /><ModelPanel {...props} onDetails={() => setActiveTab('Model')} /><BacktestPanel {...props} onDetails={() => setActiveTab('Backtests')} /></div>
  </>
}
export default function AITraderPage({ api = aiTraderApi }: { api?: AiTraderApi } = {}) {
  const { i18n, t } = useTranslation()
  useEffect(() => { document.title = `${t('aiTrader.title')} | StockLab` }, [i18n.language, t])
  const [activeTab, setActiveTab] = useState<TabName>('Overview')
  const [range, setRange] = useState<EquityRange>('1M')
  const [selection, setSelection] = useState<DetailSelection | null>(null)
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const data = useAiTraderData(activeTab, api)
  const changeTab = (tab: TabName) => { setActiveTab(tab); setSelection(null) }
  return <div className="ai-trader-page stocklab-layout"><Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} /><main className="ai-trader-main"><TopBar onMenuOpen={() => setSidebarOpen(true)} title={t('aiTrader.title')} />
    <div className="ai-trader-content"><section className="ai-trader-heading"><div><h1>{t('aiTrader.title')}</h1><p>{t('aiTrader.subtitle')}</p></div><div className="heading-actions">
      <button className="secondary-button" disabled={data.isLoading || data.resources.performance.status === 'unauthorized'} onClick={data.refresh} type="button">{t(data.isLoading ? 'aiTrader.refreshing' : 'aiTrader.refresh')}</button>
      <button className="primary-button" disabled title={t('aiTrader.backtestReadOnly')} type="button">{t('aiTrader.startNewBacktest')}</button></div></section>
      <div aria-label={t('aiTrader.sections')} className="ai-tabs" role="tablist">{tabs.map(tab => <button aria-selected={activeTab === tab} aria-controls="ai-tab-content" id={`ai-tab-${tab}`} className={activeTab === tab ? 'selected' : ''} key={tab} tabIndex={activeTab === tab ? 0 : -1} onClick={() => changeTab(tab)} onKeyDown={event => {
        const index = tabs.indexOf(tab)
        const next = event.key === 'ArrowRight' ? tabs[(index + 1) % tabs.length] : event.key === 'ArrowLeft' ? tabs[(index + tabs.length - 1) % tabs.length] : event.key === 'Home' ? tabs[0] : event.key === 'End' ? tabs[tabs.length - 1] : null
        if (next) { event.preventDefault(); changeTab(next); document.getElementById(`ai-tab-${next}`)?.focus() }
      }} role="tab" type="button">{t(`aiTrader.tabs.${tab.toLowerCase()}`)}</button>)}</div>
      <div id="ai-tab-content" role="tabpanel" aria-labelledby={`ai-tab-${activeTab}`}><TabContent data={data} activeTab={activeTab} setActiveTab={changeTab} range={range} setRange={setRange} selectDetail={setSelection} />
        {selection && data.resources.performance.status !== 'unauthorized' && <DetailPanel selection={selection} api={api} close={() => setSelection(null)} selectDetail={setSelection} rejectSession={data.rejectSession} />}</div>
      <p className="simulation-note"><Icon name="activity" size={13} /> {t('aiTrader.simulationNote')}</p>
    </div><LogoAttribution /></main></div>
}
