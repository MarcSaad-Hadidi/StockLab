import { Sidebar } from '../components/layout/Sidebar'
import { routeFor } from '../navigation/routes'
import { useTranslation } from 'react-i18next'
import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
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

function UserAvatar() {
  return <span aria-hidden="true" className="user-avatar">MS</span>
}

function StockMark({ symbol }: { symbol: string }) {
  const mark = symbol === 'MSFT' ? <><i /><i /><i /><i /></> : symbol === 'META' ? '∞' : symbol === 'BTC' ? '₿' : symbol === 'GOOGL' ? 'G' : symbol.slice(0, 1)
  return <span aria-hidden="true" className={`stock-mark stock-mark-${symbol.toLowerCase()}`}>{mark}</span>
}

function formatCurrency(value: number) {
  return `$${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function ConditionBadge({ condition }: { condition: AlertCondition }) {
  const { t } = useTranslation()
  return <span className={`condition-badge condition-${condition}`}><b>{condition === 'above' ? '↑' : '↓'}</b>{t(`alerts.conditions.${condition}`)}</span>
}

function StatusBadge({ status }: { status: AlertStatus }) {
  const { t } = useTranslation()
  return <span className={`status-badge status-${status}`}><i />{t(`alerts.statuses.${status}`)}</span>
}

function AlertRow({ alert, onEdit, onToggle, onDelete }: { alert: PriceAlert; onEdit: (alert: PriceAlert) => void; onToggle: (alert: PriceAlert) => void; onDelete: (alert: PriceAlert) => void }) {
  const { t } = useTranslation()
  const toggleLabel = alert.status === 'active' ? t('alerts.disableAlert', { symbol: alert.symbol }) : t('alerts.enableAlert', { symbol: alert.symbol })
  return <article className="alert-row">
    <div className="alert-asset"><StockMark symbol={alert.symbol} /><span><strong>{alert.symbol}</strong><small>{alert.name}</small></span></div>
    <div className="alert-cell"><span className="cell-label">{t('common.condition')}</span><ConditionBadge condition={alert.condition} /></div>
    <div className="alert-cell alert-number"><span className="cell-label">{t('common.targetPrice')}</span><strong>{formatCurrency(alert.targetPrice)}</strong></div>
    <div className="alert-cell alert-number"><span className="cell-label">{t('alerts.lastPrice')}</span><strong>{formatCurrency(alert.lastPrice)}</strong></div>
    <div className="alert-cell"><span className="cell-label">{t('common.status')}</span><StatusBadge status={alert.status} /></div>
    <div className="alert-cell alert-date"><span className="cell-label">{t('common.created')}</span><span>{alert.createdAt}</span></div>
    <div className="alert-actions"><button aria-label={t('alerts.editAlert', { symbol: alert.symbol })} className="table-action" onClick={() => onEdit(alert)} title={t('common.edit')} type="button"><Icon name="edit" size={14} /></button><button aria-label={toggleLabel} className={`table-action ${alert.status === 'active' ? 'action-disable' : 'action-enable'}`} onClick={() => onToggle(alert)} title={toggleLabel} type="button"><Icon name={alert.status === 'active' ? 'pause' : 'play'} size={14} /></button><button aria-label={t('alerts.deleteAlert', { symbol: alert.symbol })} className="table-action action-delete" onClick={() => onDelete(alert)} title={t('common.delete')} type="button"><Icon name="trash" size={14} /></button></div>
  </article>
}

type AlertDraft = { symbol: string; condition: AlertCondition; targetPrice: number }

function AlertModal({ alert, onClose, onSave }: { alert: PriceAlert | null; onClose: () => void; onSave: (draft: AlertDraft) => void }) {
  const { t } = useTranslation()
  const [symbol, setSymbol] = useState(alert?.symbol ?? assetOptions[0].symbol)
  const [condition, setCondition] = useState<AlertCondition>(alert?.condition ?? 'above')
  const [targetPrice, setTargetPrice] = useState(String(alert?.targetPrice ?? assetOptions[0].lastPrice.toFixed(2)))
  const [error, setError] = useState('')
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const parsedPrice = Number(targetPrice)
    if (!Number.isFinite(parsedPrice) || parsedPrice <= 0) { setError(t('stockDetails.errors.targetPrice')); return }
    onSave({ symbol, condition, targetPrice: parsedPrice })
  }
  return <div className="modal-backdrop" onClick={onClose} role="presentation"><section aria-labelledby="alert-modal-title" aria-modal="true" className="alert-modal" onClick={(event) => event.stopPropagation()} role="dialog"><button aria-label={t('alerts.closeDialog')} className="modal-close" onClick={onClose} type="button"><Icon name="x" size={17} /></button><div className="alert-modal-icon"><Icon name="bell" size={20} /></div><h2 id="alert-modal-title">{alert ? t('alerts.editAlertTitle') : t('alerts.createAlert')}</h2><p>{t('alerts.modalDescription')}</p><form onSubmit={submit}><label htmlFor="alert-asset">{t('alerts.selectAsset')}</label><div className="select-wrap"><select id="alert-asset" onChange={(event) => setSymbol(event.target.value)} value={symbol}>{assetOptions.map((asset) => <option key={asset.symbol} value={asset.symbol}>{asset.symbol} — {asset.name}</option>)}</select><Icon name="chevron-down" size={14} /></div><fieldset><legend>{t('common.condition')}</legend><div className="condition-options"><button aria-pressed={condition === 'above'} className={`condition-option ${condition === 'above' ? 'selected' : ''}`} onClick={() => setCondition('above')} type="button"><span>↑</span> {t('common.above')}</button><button aria-pressed={condition === 'below'} className={`condition-option ${condition === 'below' ? 'selected' : ''}`} onClick={() => setCondition('below')} type="button"><span>↓</span> {t('common.below')}</button></div></fieldset><label htmlFor="alert-target">{t('common.targetPrice')}</label><div className="price-input"><span>$</span><input id="alert-target" inputMode="decimal" min="0.01" onChange={(event) => { setTargetPrice(event.target.value); setError('') }} required step="0.01" type="number" value={targetPrice} /></div><fieldset className="notify-fieldset"><legend>{t('alerts.notifyVia')}</legend><div className="notify-options"><label><input defaultChecked type="checkbox" /> <span>{t('alerts.inApp')}</span></label><label><input defaultChecked type="checkbox" /> <span>{t('common.email')}</span></label><label><input type="checkbox" /> <span>{t('alerts.pushNotification')}</span></label></div></fieldset>{error && <p className="form-error" role="alert">{error}</p>}<div className="alert-modal-actions"><button className="cancel-button" onClick={onClose} type="button">{t('common.cancel')}</button><button className="modal-primary" type="submit">{alert ? t('common.saveChanges') : t('alerts.createAlert')}</button></div></form></section></div>
}

export default function AlertsPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('alerts.pageTitle')} | StockLab`
  }, [i18n.language, t])
  const [alerts, setAlerts] = useState<PriceAlert[]>(initialAlerts)
  const [query, setQuery] = useState('')
  const [conditionFilter, setConditionFilter] = useState<'all' | AlertCondition>('all')
  const [assetFilter, setAssetFilter] = useState('all')
  const [statusFilter, setStatusFilter] = useState<'all' | AlertStatus>('active')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [toast, setToast] = useState('')
  const [modalAlert, setModalAlert] = useState<PriceAlert | null | undefined>(undefined)

  const showToast = (message: string) => {
    setToast(message)
    window.setTimeout(() => setToast(''), 2300)
  }

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

  const saveAlert = (draft: AlertDraft) => {
    const asset = assetOptions.find((candidate) => candidate.symbol === draft.symbol) ?? assetOptions[0]
    if (modalAlert) {
      setAlerts((current) => current.map((candidate) => candidate.id === modalAlert.id ? { ...candidate, ...asset, condition: draft.condition, targetPrice: draft.targetPrice } : candidate))
      showToast(t('alerts.alertUpdated', { symbol: draft.symbol }))
    } else {
      const createdAlert: PriceAlert = { id: `alert-${Date.now()}`, ...asset, condition: draft.condition, targetPrice: draft.targetPrice, status: 'active', createdAt: 'Jun 4, 2026' }
      setAlerts((current) => [createdAlert, ...current])
      setStatusFilter('active')
      showToast(t('alerts.alertCreatedToast', { symbol: draft.symbol }))
    }
    setModalAlert(undefined)
  }

  const toggleAlert = (alert: PriceAlert) => {
    const nextStatus: AlertStatus = alert.status === 'active' ? 'disabled' : 'active'
    setAlerts((current) => current.map((candidate) => candidate.id === alert.id ? { ...candidate, status: nextStatus } : candidate))
    showToast(t(nextStatus === 'active' ? 'alerts.alertEnabled' : 'alerts.alertDisabled', { symbol: alert.symbol }))
  }

  const deleteAlert = (alert: PriceAlert) => {
    setAlerts((current) => current.filter((candidate) => candidate.id !== alert.id))
    showToast(t('alerts.alertDeleted', { symbol: alert.symbol }))
  }

  return <div className="alerts-page stocklab-layout">
    <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
    <main className="alerts-main">
      <header className="alerts-topbar">
        <button aria-label={t('common.openNavigation')} className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={20} /></button>
        <div className="breadcrumb"><strong>{t('alerts.pageTitle')}</strong></div>
        <div className="topbar-actions">
          <label className="global-search"><Icon name="search" size={16} /><input aria-label={t('common.searchStocks')} onChange={(event) => setQuery(event.target.value)} placeholder={t('common.searchStocksEtfsNewsPlaceholder')} value={query} /></label>
          <button aria-label={t('common.notifications')} className="icon-button notification-button" onClick={() => showToast(t('common.notificationsCaughtUp'))} type="button"><Icon name="bell" size={18} /><i>2</i></button>
          <button aria-label={t('common.openMessages')} className="icon-button mail-button" onClick={() => showToast(t('common.noNewMessages'))} type="button"><Icon name="mail" size={17} /></button>
          <button aria-label={t('common.openAccountMenu')} className="topbar-account" onClick={() => window.location.assign(routeFor('profile'))} type="button"><UserAvatar /><Icon name="chevron-down" size={14} /></button>
        </div>
      </header>
      <div className="alerts-content">
        <section className="alerts-heading">
          <div><h1>{t('alerts.pageTitle')}</h1><p>{t('alerts.pageSubtitle')}</p></div>
          <button className="primary-button" onClick={() => setModalAlert(null)} type="button"><Icon name="plus" size={15} /> {t('alerts.createAlert')}</button>
        </section>
        <div aria-label={t('alerts.alertStatus')} className="alert-tabs" role="tablist">
          <button aria-selected={statusFilter === 'active'} className={statusFilter === 'active' ? 'selected' : ''} onClick={() => setStatusFilter('active')} role="tab" type="button">{t('alerts.statuses.active')} <span>({counts.active})</span></button>
          <button aria-selected={statusFilter === 'triggered'} className={statusFilter === 'triggered' ? 'selected' : ''} onClick={() => setStatusFilter('triggered')} role="tab" type="button">{t('alerts.statuses.triggered')} <span>({counts.triggered})</span></button>
          <button aria-selected={statusFilter === 'disabled'} className={statusFilter === 'disabled' ? 'selected' : ''} onClick={() => setStatusFilter('disabled')} role="tab" type="button">{t('alerts.statuses.disabled')} <span>({counts.disabled})</span></button>
        </div>
        <section aria-labelledby="alerts-list-title" className="panel alerts-panel">
          <div className="panel-heading"><div><h2 id="alerts-list-title">{t('alerts.priceAlerts')}</h2><p>{t('alerts.priceAlertsDescription')}</p></div><span className="live-indicator"><i /> {t('common.simulatedData')}</span></div>
          <div className="alerts-controls">
            <label className="alerts-search"><Icon name="search" size={15} /><input aria-label={t('alerts.searchAlerts')} onChange={(event) => setQuery(event.target.value)} placeholder={t('alerts.searchAlerts')} value={query} /></label>
            <label className="filter-select"><span>{t('common.condition')}</span><select aria-label={t('common.condition')} onChange={(event) => setConditionFilter(event.target.value as 'all' | AlertCondition)} value={conditionFilter}><option value="all">{t('alerts.allConditions')}</option><option value="above">{t('alerts.conditions.above')}</option><option value="below">{t('alerts.conditions.below')}</option></select><Icon name="chevron-down" size={13} /></label>
            <label className="filter-select"><span>{t('common.asset')}</span><select aria-label={t('common.asset')} onChange={(event) => setAssetFilter(event.target.value)} value={assetFilter}><option value="all">{t('alerts.allAssets')}</option>{assetOptions.map((asset) => <option key={asset.symbol} value={asset.symbol}>{asset.symbol}</option>)}</select><Icon name="chevron-down" size={13} /></label>
            <label className="filter-select"><span>{t('common.status')}</span><select aria-label={t('common.status')} onChange={(event) => setStatusFilter(event.target.value as 'all' | AlertStatus)} value={statusFilter}><option value="all">{t('alerts.allStatuses')}</option><option value="active">{t('alerts.statuses.active')}</option><option value="triggered">{t('alerts.statuses.triggered')}</option><option value="disabled">{t('alerts.statuses.disabled')}</option></select><Icon name="chevron-down" size={13} /></label>
            <button aria-label={t('alerts.moreFilters')} className="filter-button" onClick={() => showToast(t('alerts.allFiltersShown'))} type="button"><Icon name="filter" size={15} /></button>
          </div>
          <div className="alert-table-header" aria-hidden="true">{Object.values({ asset: 'asset', condition: 'condition', targetPrice: 'targetPrice', lastPrice: 'lastPrice', status: 'status', created: 'created', actions: 'actions' }).map((key) => <span key={key}>{t(`alerts.columns.${key}`)}</span>)}</div>
          <div className="alert-list">{filteredAlerts.length > 0 ? filteredAlerts.map((alert) => <AlertRow alert={alert} key={alert.id} onDelete={deleteAlert} onEdit={(target) => setModalAlert(target)} onToggle={toggleAlert} />) : <div className="empty-state"><span><Icon name="bell" size={18} /></span><strong>{t('alerts.noAlerts')}</strong><p>{t('alerts.noAlertsHint')}</p></div>}</div>
          <div className="alerts-footer"><span>{t('alerts.showing', { shown: filteredAlerts.length, total: alerts.length })}</span><span className="pagination"><button aria-label={t('common.previousPage')} disabled type="button">‹</button><b>1</b><button aria-label={t('common.nextPage')} disabled type="button">›</button><label>{t('alerts.rowsPerPage')} <select aria-label={t('alerts.rowsPerPage')} defaultValue="25"><option>10</option><option>25</option><option>50</option></select></label></span></div>
        </section>
        <div className="alerts-bottom-grid">
          <section className="panel create-alert-card"><div className="card-icon blue-icon"><Icon name="bell" size={19} /></div><div><h2>{t('alerts.createAlert')}</h2><p>{t('alerts.createAlertDescription')}</p></div><button className="outline-button" onClick={() => setModalAlert(null)} type="button">{t('alerts.createAlert')} <Icon name="chevron-right" size={14} /></button></section>
          <section className="panel how-alerts-card"><div className="card-icon purple-icon"><Icon name="shield" size={19} /></div><div><h2>{t('alerts.howAlertsWork')}</h2><p>{t('alerts.howAlertsWorkDescription')}</p></div><ul><li><span className="how-icon"><Icon name="activity" size={14} /></span><span><strong>{t('alerts.realTimeMonitoring')}</strong><small>{t('alerts.realTimeMonitoringDescription')}</small></span></li><li><span className="how-icon"><Icon name="bell" size={14} /></span><span><strong>{t('alerts.multipleDeliveryChannels')}</strong><small>{t('alerts.multipleDeliveryChannelsDescription')}</small></span></li><li><span className="how-icon"><Icon name="settings" size={14} /></span><span><strong>{t('alerts.manageAnytime')}</strong><small>{t('alerts.manageAnytimeDescription')}</small></span></li></ul></section>
        </div>
        <p className="simulation-note"><Icon name="activity" size={13} /> {t('alerts.simulationNote')}</p>
      </div>
    </main>
    <div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast}</div>
    {modalAlert !== undefined && <AlertModal alert={modalAlert} onClose={() => setModalAlert(undefined)} onSave={saveAlert} />}
  </div>
}
