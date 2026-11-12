import { MarketRequestStatus } from '../market/MarketRequestStatus'
import { marketDataApi } from '../api/marketDataClient'
import { useMarketRequest } from '../market/useMarketRequest'
import { StockLogo, LogoAttribution } from '../market/StockLogo'
import { Sidebar } from '../components/layout/Sidebar'
import { TopBar } from '../components/layout/TopBar'
import { formatCurrency, formatDate } from '../i18n/formatters'
import { useTranslation } from 'react-i18next'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { assetOptions, initialAlerts, type AlertCondition, type AlertStatus, type PriceAlert } from './alertsData'
import './alerts.css'

type IconName =
  | 'activity'
  | 'bell'
  | 'briefcase'
  | 'chart'
  | 'chevron-down'
  | 'chevron-right'
  | 'edit'
  | 'filter'
  | 'grid'
  | 'logout'
  | 'mail'
  | 'menu'
  | 'pause'
  | 'play'
  | 'plus'
  | 'search'
  | 'settings'
  | 'shield'
  | 'star'
  | 'trash'
  | 'user'
  | 'x'

function Icon({ name, size = 18 }: { name: IconName; size?: number }) {
  const common = {
    fill: 'none',
    stroke: 'currentColor',
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    strokeWidth: 1.8,
  }

  const paths: Record<IconName, ReactNode> = {
    activity: <path d="M3 12h3l2.2-6 3.6 12 2.2-6H21" {...common} />,
    bell: <><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9" {...common} /><path d="M10 21h4" {...common} /></>,
    briefcase: <><rect x="3" y="7" width="18" height="13" rx="2" {...common} /><path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 12h18M10 12v2h4v-2" {...common} /></>,
    chart: <><path d="M4 19V5M4 19h17" {...common} /><path d="m7 15 3-4 3 2 5-7" {...common} /><path d="M16 6h2v2" {...common} /></>,
    'chevron-down': <path d="m6 9 6 6 6-6" {...common} />,
    'chevron-right': <path d="m9 6 6 6-6 6" {...common} />,
    edit: <><path d="m4 16.5-.8 3.3 3.3-.8L18 7.5 15.5 5 4 16.5Z" {...common} /><path d="m13.8 6.7 2.5 2.5M17.2 4.1l2.7 2.7" {...common} /></>,
    filter: <path d="M4 6h16M7 12h10M10 18h4" {...common} />,
    grid: <><rect x="3" y="3" width="7" height="7" rx="1" {...common} /><rect x="14" y="3" width="7" height="7" rx="1" {...common} /><rect x="3" y="14" width="7" height="7" rx="1" {...common} /><rect x="14" y="14" width="7" height="7" rx="1" {...common} /></>,
    logout: <><path d="M10 4H5.5A1.5 1.5 0 0 0 4 5.5v13A1.5 1.5 0 0 0 5.5 20H10M14 8l4 4-4 4M18 12H9" {...common} /></>,
    mail: <><rect x="3" y="5" width="18" height="14" rx="2" {...common} /><path d="m4 7 8 6 8-6" {...common} /></>,
    menu: <path d="M4 7h16M4 12h16M4 17h16" {...common} />,
    pause: <><path d="M8 5v14M16 5v14" {...common} /></>,
    play: <path d="m8 5 11 7-11 7V5Z" {...common} />,
    plus: <path d="M12 5v14M5 12h14" {...common} />,
    search: <><circle cx="10.8" cy="10.8" r="6.8" {...common} /><path d="m16 16 4.5 4.5" {...common} /></>,
    settings: <><circle cx="12" cy="12" r="3" {...common} /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-1.7 1.7-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5v.2h-2.4v-.2a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1L8 17l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H6.7v-2.4h.2a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9L8 8.6l1.7-1.7.1.1a1.7 1.7 0 0 0 1.9.3 1.7 1.7 0 0 0 1-1.5v-.2h2.4v.2a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1 1.7 1.7-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.2V14h-.2a1.7 1.7 0 0 0-1.5 1Z" {...common} /></>,
    shield: <path d="M12 3 5 6v5c0 4.7 2.8 8.1 7 10 4.2-1.9 7-5.3 7-10V6l-7-3Z" {...common} />,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z" {...common} />,
    trash: <><path d="M4 7h16M10 11v6M14 11v6" {...common} /><path d="M6 7l1 14h10l1-14M9 7V4h6v3" {...common} /></>,
    user: <><circle cx="12" cy="8" r="3.2" {...common} /><path d="M5.2 20a6.8 6.8 0 0 1 13.6 0" {...common} /></>,
    x: <path d="m6 6 12 12M18 6 6 18" {...common} />,
  }

  return <svg aria-hidden="true" className="icon" height={size} viewBox="0 0 24 24" width={size}>{paths[name]}</svg>
}

function StockMark({ symbol }: { symbol: string }) { return <StockLogo symbol={symbol} /> }

function ConditionBadge({ condition }: { condition: AlertCondition }) {
  const { t } = useTranslation()
  return <span className={`condition-badge condition-${condition}`}><b>{condition === 'above' ? '↑' : '↓'}</b>{t(`alerts.conditions.${condition}`)}</span>
}

function StatusBadge({ status }: { status: AlertStatus }) {
  const { t } = useTranslation()
  return <span className={`status-badge status-${status}`}><i />{t(`alerts.statuses.${status}`)}</span>
}

function AlertRow({ alert }: { alert: PriceAlert }) {
  const { t } = useTranslation()
  const toggleLabel = alert.status === 'active' ? t('alerts.disableAlert', { symbol: alert.symbol }) : t('alerts.enableAlert', { symbol: alert.symbol })
  return <article className="alert-row">
    <div className="alert-asset"><StockMark symbol={alert.symbol} /><span><strong>{alert.symbol}</strong><small>{alert.name}</small></span></div>
    <div className="alert-cell"><span className="cell-label">{t('common.condition')}</span><ConditionBadge condition={alert.condition} /></div>
    <div className="alert-cell alert-number"><span className="cell-label">{t('common.targetPrice')}</span><strong>{formatCurrency(alert.targetPrice)}</strong></div>
    <div className="alert-cell alert-number"><span className="cell-label">{t('alerts.lastPrice')}</span><strong>{formatCurrency(alert.lastPrice)}</strong></div>
    <div className="alert-cell"><span className="cell-label">{t('common.status')}</span><StatusBadge status={alert.status} /></div>
    <div className="alert-cell alert-date"><span className="cell-label">{t('common.created')}</span><span>{formatDate(alert.createdAt)}</span></div>
    <div className="alert-actions"><button aria-label={t('alerts.editAlert', { symbol: alert.symbol })} className="table-action" disabled aria-describedby="alerts-unavailable-description" title={t('common.edit')} type="button"><Icon name="edit" size={14} /></button><button aria-label={toggleLabel} className={`table-action ${alert.status === 'active' ? 'action-disable' : 'action-enable'}`} disabled aria-describedby="alerts-unavailable-description" title={toggleLabel} type="button"><Icon name={alert.status === 'active' ? 'pause' : 'play'} size={14} /></button><button aria-label={t('alerts.deleteAlert', { symbol: alert.symbol })} className="table-action action-delete" disabled aria-describedby="alerts-unavailable-description" title={t('common.delete')} type="button"><Icon name="trash" size={14} /></button></div>
  </article>
}

// This screen is a preview until the alerts API integration is implemented.
// Native disabled controls prevent keyboard, pointer and implicit submissions.
function AlertForm() {
  const { t } = useTranslation()
  const [search, setSearch] = useState('')
  const loadSearch = useCallback((signal: AbortSignal) => marketDataApi.search(search, signal), [search])
  const results = useMarketRequest(search.trim(), loadSearch, search.trim().length > 0, 450)
  return <div className="alert-form" id="create-alert-form">
    <div className="alert-form-field alert-preview-search">
      <label htmlFor="alert-market-search">{t('common.searchStocks')}</label>
      <p id="alert-search-description">{t('alerts.searchPreviewHint')}</p>
      <input id="alert-market-search" aria-label={t('common.searchStocks')} aria-describedby="alert-search-description" placeholder={t('common.searchStocks')} value={search} onChange={event => setSearch(event.target.value)} />
      <MarketRequestStatus {...results} />
      {search.trim() && !results.loading && !results.error && results.data?.length === 0 && <p>{t('market.noStocksFound')}</p>}
      {results.data && results.data.length > 0 && <ul className="alert-preview-results">{results.data.map(asset => <li key={asset.symbol}>{asset.symbol} — {asset.companyName}</li>)}</ul>}
    </div>
    <fieldset aria-describedby="alerts-unavailable-description" className="alert-preview-fields" disabled>
      <legend className="visually-hidden">{t('alerts.createAlert')}</legend>
      <div className="alert-form-grid">
        <div className="alert-form-field">
          <label htmlFor="inline-alert-asset">{t('alerts.selectAsset')}</label>
          <div className="select-wrap"><select id="inline-alert-asset" defaultValue=""><option value="">—</option></select><Icon name="chevron-down" size={14} /></div>
        </div>
        <fieldset className="alert-form-field alert-form-condition">
          <legend>{t('common.condition')}</legend>
          <div className="condition-options"><button className="condition-option" type="button"><span>↑</span> {t('common.above')}</button><button className="condition-option" type="button"><span>↓</span> {t('common.below')}</button></div>
        </fieldset>
        <div className="alert-form-field alert-form-target">
          <label htmlFor="inline-alert-target">{t('common.targetPrice')}</label>
          <div className="price-input"><span>$</span><input id="inline-alert-target" inputMode="decimal" type="number" /></div>
        </div>
        <fieldset className="alert-form-field notify-fieldset">
          <legend>{t('alerts.notifyVia')}</legend>
          <div className="notify-options"><label><input type="checkbox" /> <span>{t('alerts.inApp')}</span></label><label><input type="checkbox" /> <span>{t('common.email')}</span></label><label><input type="checkbox" /> <span>{t('alerts.pushNotification')}</span></label></div>
        </fieldset>
      </div>
      <div className="alert-form-actions"><button className="modal-primary" disabled type="button">{t('alerts.createAlert')}</button></div>
    </fieldset>
  </div>
}

export default function AlertsPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('alerts.pageTitle')} | StockLab`
  }, [i18n.language, t])
  const alerts = initialAlerts
  const [query, setQuery] = useState('')
  const [conditionFilter, setConditionFilter] = useState<'all' | AlertCondition>('all')
  const [assetFilter, setAssetFilter] = useState('all')
  const [statusFilter, setStatusFilter] = useState<'all' | AlertStatus>('active')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const counts = useMemo(() => ({
    active: alerts.filter((alert) => alert.status === 'active').length,
    triggered: alerts.filter((alert) => alert.status === 'triggered').length,
    disabled: alerts.filter((alert) => alert.status === 'disabled').length,
  }), [alerts])

  const filteredAlerts = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase()
    return alerts.filter((alert) => {
      const matchesQuery = normalizedQuery.length === 0 || `${alert.symbol} ${alert.name}`.toLowerCase().includes(normalizedQuery)
      const matchesCondition = conditionFilter === 'all' || alert.condition === conditionFilter
      const matchesAsset = assetFilter === 'all' || alert.symbol === assetFilter
      const matchesStatus = statusFilter === 'all' || alert.status === statusFilter
      return matchesQuery && matchesCondition && matchesAsset && matchesStatus
    })
  }, [alerts, assetFilter, conditionFilter, query, statusFilter])

  return <div className="alerts-page stocklab-layout">
    <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
    <main className="alerts-main">
      <TopBar onMenuOpen={() => setSidebarOpen(true)} title={t('alerts.pageTitle')} />
      <div className="alerts-content">
        <section className="alerts-heading">
          <div><h1>{t('alerts.pageTitle')}</h1><p>{t('alerts.pageSubtitle')}</p></div>
          <button aria-describedby="alerts-unavailable-description" className="primary-button" disabled type="button"><Icon name="plus" size={15} /> {t('alerts.createAlert')}</button>
        </section>
        <p className="alerts-availability" id="alerts-unavailable-description" role="status">{t('alerts.unavailablePreview')}</p>
        <div aria-label={t('alerts.alertStatus')} className="alert-tabs" role="tablist">
          <button aria-selected={statusFilter === 'active'} className={statusFilter === 'active' ? 'selected' : ''} onClick={() => setStatusFilter('active')} role="tab" type="button">{t('alerts.statuses.active')} <span>({counts.active})</span></button>
          <button aria-selected={statusFilter === 'triggered'} className={statusFilter === 'triggered' ? 'selected' : ''} onClick={() => setStatusFilter('triggered')} role="tab" type="button">{t('alerts.statuses.triggered')} <span>({counts.triggered})</span></button>
          <button aria-selected={statusFilter === 'disabled'} className={statusFilter === 'disabled' ? 'selected' : ''} onClick={() => setStatusFilter('disabled')} role="tab" type="button">{t('alerts.statuses.disabled')} <span>({counts.disabled})</span></button>
        </div>
        <section aria-labelledby="alerts-list-title" className="panel alerts-panel">
          <div className="panel-heading"><div><h2 id="alerts-list-title">{t('alerts.priceAlerts')}</h2><p>{t('businessData.backendPending')}</p></div><span className="live-indicator">{t('businessData.unavailable')}</span></div>
          <div className="alerts-controls">
            <label className="alerts-search"><Icon name="search" size={15} /><input aria-label={t('alerts.searchAlerts')} onChange={(event) => setQuery(event.target.value)} placeholder={t('alerts.searchAlerts')} value={query} /></label>
            <label className="filter-select"><span>{t('common.condition')}</span><select aria-label={t('common.condition')} onChange={(event) => setConditionFilter(event.target.value as 'all' | AlertCondition)} value={conditionFilter}><option value="all">{t('alerts.allConditions')}</option><option value="above">{t('alerts.conditions.above')}</option><option value="below">{t('alerts.conditions.below')}</option></select><Icon name="chevron-down" size={13} /></label>
            <label className="filter-select"><span>{t('common.asset')}</span><select aria-label={t('common.asset')} onChange={(event) => setAssetFilter(event.target.value)} value={assetFilter}><option value="all">{t('alerts.allAssets')}</option>{assetOptions.map((asset) => <option key={asset.symbol} value={asset.symbol}>{asset.symbol}</option>)}</select><Icon name="chevron-down" size={13} /></label>
            <label className="filter-select"><span>{t('common.status')}</span><select aria-label={t('common.status')} onChange={(event) => setStatusFilter(event.target.value as 'all' | AlertStatus)} value={statusFilter}><option value="all">{t('alerts.allStatuses')}</option><option value="active">{t('alerts.statuses.active')}</option><option value="triggered">{t('alerts.statuses.triggered')}</option><option value="disabled">{t('alerts.statuses.disabled')}</option></select><Icon name="chevron-down" size={13} /></label>
          </div>
          <div className="alert-table-header" aria-hidden="true">{Object.values({ asset: 'asset', condition: 'condition', targetPrice: 'targetPrice', lastPrice: 'lastPrice', status: 'status', created: 'created', actions: 'actions' }).map((key) => <span key={key}>{t(`alerts.columns.${key}`)}</span>)}</div>
          <div className="alert-list">{filteredAlerts.length > 0 ? filteredAlerts.map((alert) => <AlertRow alert={alert} key={alert.id} />) : <div className="empty-state"><span><Icon name="bell" size={18} /></span><strong>{t('businessData.alerts')}</strong><p>{t('businessData.backendPending')}</p></div>}</div>
          <div className="alerts-footer"><span>{t('alerts.showing', { shown: filteredAlerts.length, total: alerts.length })}</span><span className="pagination"><button aria-label={t('common.previousPage')} disabled type="button">‹</button><b>1</b><button aria-label={t('common.nextPage')} disabled type="button">›</button><label>{t('alerts.rowsPerPage')} <select aria-label={t('alerts.rowsPerPage')} disabled defaultValue="25"><option>10</option><option>25</option><option>50</option></select></label></span></div>
        </section>
        <div className="alerts-bottom-grid">
          <section aria-labelledby="create-alert-title" className="panel create-alert-card"><div className="create-alert-card-header"><div className="card-icon blue-icon"><Icon name="bell" size={19} /></div><div><h2 id="create-alert-title">{t('alerts.createAlert')}</h2><p>{t('alerts.createAlertDescription')}</p></div></div><AlertForm /></section>
          <section className="panel how-alerts-card"><div className="card-icon purple-icon"><Icon name="shield" size={19} /></div><div><h2>{t('alerts.howAlertsWork')}</h2><p>{t('alerts.howAlertsWorkDescription')}</p></div><ul><li><span className="how-icon"><Icon name="activity" size={14} /></span><span><strong>{t('alerts.realTimeMonitoring')}</strong><small>{t('alerts.realTimeMonitoringDescription')}</small></span></li><li><span className="how-icon"><Icon name="bell" size={14} /></span><span><strong>{t('alerts.multipleDeliveryChannels')}</strong><small>{t('alerts.multipleDeliveryChannelsDescription')}</small></span></li><li><span className="how-icon"><Icon name="settings" size={14} /></span><span><strong>{t('alerts.manageAnytime')}</strong><small>{t('alerts.manageAnytimeDescription')}</small></span></li></ul></section>
        </div>
        <p className="simulation-note"><Icon name="activity" size={13} /> {t('businessData.backendPending')}</p>
      </div>
    <LogoAttribution /></main>
  </div>
}
