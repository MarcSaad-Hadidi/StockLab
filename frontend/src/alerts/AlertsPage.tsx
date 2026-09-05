import { routeFor } from '../navigation/routes'
import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
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

function Brand() {
  return <div aria-label="StockLab" className="brand"><span aria-hidden="true" className="brand-mark"><i /><i /><i /></span><span>Stock<span>Lab</span></span></div>
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

function formatStatus(status: AlertStatus | AlertCondition) {
  return status.charAt(0).toUpperCase() + status.slice(1)
}

function ConditionBadge({ condition }: { condition: AlertCondition }) {
  return <span className={`condition-badge condition-${condition}`}><b>{condition === 'above' ? '↑' : '↓'}</b>{formatStatus(condition)}</span>
}

function StatusBadge({ status }: { status: AlertStatus }) {
  return <span className={`status-badge status-${status}`}><i />{formatStatus(status)}</span>
}

function AlertRow({ alert, onEdit, onToggle, onDelete }: { alert: PriceAlert; onEdit: (alert: PriceAlert) => void; onToggle: (alert: PriceAlert) => void; onDelete: (alert: PriceAlert) => void }) {
  const toggleLabel = alert.status === 'active' ? `Disable ${alert.symbol} alert` : `Enable ${alert.symbol} alert`
  return <article className="alert-row">
    <div className="alert-asset"><StockMark symbol={alert.symbol} /><span><strong>{alert.symbol}</strong><small>{alert.name}</small></span></div>
    <div className="alert-cell"><span className="cell-label">Condition</span><ConditionBadge condition={alert.condition} /></div>
    <div className="alert-cell alert-number"><span className="cell-label">Target price</span><strong>{formatCurrency(alert.targetPrice)}</strong></div>
    <div className="alert-cell alert-number"><span className="cell-label">Last price</span><strong>{formatCurrency(alert.lastPrice)}</strong></div>
    <div className="alert-cell"><span className="cell-label">Status</span><StatusBadge status={alert.status} /></div>
    <div className="alert-cell alert-date"><span className="cell-label">Created</span><span>{alert.createdAt}</span></div>
    <div className="alert-actions"><button aria-label={`Edit ${alert.symbol} alert`} className="table-action" onClick={() => onEdit(alert)} title="Edit alert" type="button"><Icon name="edit" size={14} /></button><button aria-label={toggleLabel} className={`table-action ${alert.status === 'active' ? 'action-disable' : 'action-enable'}`} onClick={() => onToggle(alert)} title={toggleLabel} type="button"><Icon name={alert.status === 'active' ? 'pause' : 'play'} size={14} /></button><button aria-label={`Delete ${alert.symbol} alert`} className="table-action action-delete" onClick={() => onDelete(alert)} title="Delete alert" type="button"><Icon name="trash" size={14} /></button></div>
  </article>
}

type AlertDraft = { symbol: string; condition: AlertCondition; targetPrice: number }

function AlertModal({ alert, onClose, onSave }: { alert: PriceAlert | null; onClose: () => void; onSave: (draft: AlertDraft) => void }) {
  const [symbol, setSymbol] = useState(alert?.symbol ?? assetOptions[0].symbol)
  const [condition, setCondition] = useState<AlertCondition>(alert?.condition ?? 'above')
  const [targetPrice, setTargetPrice] = useState(String(alert?.targetPrice ?? assetOptions[0].lastPrice.toFixed(2)))
  const [error, setError] = useState('')
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const parsedPrice = Number(targetPrice)
    if (!Number.isFinite(parsedPrice) || parsedPrice <= 0) { setError('Enter a target price greater than zero.'); return }
    onSave({ symbol, condition, targetPrice: parsedPrice })
  }
  return <div className="modal-backdrop" onClick={onClose} role="presentation"><section aria-labelledby="alert-modal-title" aria-modal="true" className="alert-modal" onClick={(event) => event.stopPropagation()} role="dialog"><button aria-label="Close alert dialog" className="modal-close" onClick={onClose} type="button"><Icon name="x" size={17} /></button><div className="alert-modal-icon"><Icon name="bell" size={20} /></div><h2 id="alert-modal-title">{alert ? 'Edit Alert' : 'Create Alert'}</h2><p>Set a simulated price trigger and choose when you want to be notified.</p><form onSubmit={submit}><label htmlFor="alert-asset">Select Asset</label><div className="select-wrap"><select id="alert-asset" onChange={(event) => setSymbol(event.target.value)} value={symbol}>{assetOptions.map((asset) => <option key={asset.symbol} value={asset.symbol}>{asset.symbol} — {asset.name}</option>)}</select><Icon name="chevron-down" size={14} /></div><fieldset><legend>Condition</legend><div className="condition-options"><button aria-pressed={condition === 'above'} className={`condition-option ${condition === 'above' ? 'selected' : ''}`} onClick={() => setCondition('above')} type="button"><span>↑</span> Above</button><button aria-pressed={condition === 'below'} className={`condition-option ${condition === 'below' ? 'selected' : ''}`} onClick={() => setCondition('below')} type="button"><span>↓</span> Below</button></div></fieldset><label htmlFor="alert-target">Target Price</label><div className="price-input"><span>$</span><input id="alert-target" inputMode="decimal" min="0.01" onChange={(event) => { setTargetPrice(event.target.value); setError('') }} required step="0.01" type="number" value={targetPrice} /></div><fieldset className="notify-fieldset"><legend>Notify me via</legend><div className="notify-options"><label><input defaultChecked type="checkbox" /> <span>In-app</span></label><label><input defaultChecked type="checkbox" /> <span>Email</span></label><label><input type="checkbox" /> <span>Push notification</span></label></div></fieldset>{error && <p className="form-error" role="alert">{error}</p>}<div className="alert-modal-actions"><button className="cancel-button" onClick={onClose} type="button">Cancel</button><button className="modal-primary" type="submit">{alert ? 'Save Changes' : 'Create Alert'}</button></div></form></section></div>
}

const navigation = [
  { label: 'Dashboard', icon: 'grid' as IconName, href: routeFor('dashboard') },
  { label: 'Market', icon: 'chart' as IconName, href: routeFor('market') },
  { label: 'Portfolio', icon: 'briefcase' as IconName, href: routeFor('portfolio') },
  { label: 'Transactions', icon: 'activity' as IconName, href: routeFor('transactions') },
  { label: 'Watchlist', icon: 'star' as IconName, href: routeFor('watchlist') },
  { label: 'Alerts', icon: 'bell' as IconName, href: routeFor('alerts') },
]

export default function AlertsPage() {
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
      showToast(`${draft.symbol} alert updated.`)
    } else {
      const createdAlert: PriceAlert = { id: `alert-${Date.now()}`, ...asset, condition: draft.condition, targetPrice: draft.targetPrice, status: 'active', createdAt: 'Jun 4, 2026' }
      setAlerts((current) => [createdAlert, ...current])
      setStatusFilter('active')
      showToast(`${draft.symbol} alert created.`)
    }
    setModalAlert(undefined)
  }

  const toggleAlert = (alert: PriceAlert) => {
    const nextStatus: AlertStatus = alert.status === 'active' ? 'disabled' : 'active'
    setAlerts((current) => current.map((candidate) => candidate.id === alert.id ? { ...candidate, status: nextStatus } : candidate))
    showToast(`${alert.symbol} alert ${nextStatus === 'active' ? 'enabled' : 'disabled'}.`)
  }

  const deleteAlert = (alert: PriceAlert) => {
    setAlerts((current) => current.filter((candidate) => candidate.id !== alert.id))
    showToast(`${alert.symbol} alert deleted.`)
  }

  return <div className={`alerts-page ${sidebarOpen ? 'sidebar-open' : ''}`}><button aria-label="Close navigation" className="sidebar-backdrop" onClick={() => setSidebarOpen(false)} type="button" /><aside className="alerts-sidebar"><Brand /><nav aria-label="Primary navigation" className="sidebar-nav"><span className="nav-label">Overview</span>{navigation.slice(0, 2).map((item) => <a className="nav-item" href={item.href} key={item.label} onClick={() => setSidebarOpen(false)}><Icon name={item.icon} size={16} /><span>{item.label}</span></a>)}<span className="nav-label nav-label-spaced">Your trading</span>{navigation.slice(2).map((item) => <a aria-current={item.label === 'Alerts' ? 'page' : undefined} className={`nav-item ${item.label === 'Alerts' ? 'active' : ''}`} href={item.href} key={item.label} onClick={() => setSidebarOpen(false)}><Icon name={item.icon} size={16} /><span>{item.label}</span></a>)}<span className="nav-label nav-label-spaced">AI</span><a className="nav-item" href={routeFor('ai-trader')} onClick={() => setSidebarOpen(false)}><Icon name="activity" size={16} /><span>AI Trader</span></a><span className="nav-label nav-label-spaced">Account</span><a className="nav-item" href={routeFor('profile')} onClick={() => setSidebarOpen(false)}><Icon name="user" size={16} /><span>Profile</span></a><button className="nav-item nav-button" onClick={() => window.location.assign(routeFor('logout'))} type="button"><Icon name="logout" size={16} /><span>Logout</span></button></nav><div className="sidebar-footer"><p>StockLab preview</p></div></aside><main className="alerts-main"><header className="alerts-topbar"><button aria-label="Open navigation" className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={20} /></button><div className="breadcrumb"><strong>Alerts</strong><span>—</span><a href={routeFor('alerts')}>/alerts</a></div><div className="topbar-actions"><label className="global-search"><Icon name="search" size={16} /><input aria-label="Search stocks" onChange={(event) => setQuery(event.target.value)} placeholder="Search stocks, ETFs, news..." value={query} /></label><button aria-label="Notifications" className="icon-button notification-button" onClick={() => showToast('You are all caught up.')} type="button"><Icon name="bell" size={18} /><i>2</i></button><button aria-label="Open messages" className="icon-button mail-button" onClick={() => showToast('No new messages.')} type="button"><Icon name="mail" size={17} /></button><button aria-label="Open account menu" className="topbar-account" onClick={() => window.location.assign(routeFor('profile'))} type="button"><UserAvatar /><Icon name="chevron-down" size={14} /></button></div></header><div className="alerts-content"><section className="alerts-heading"><div><h1>Alerts</h1><p>Create and manage price alerts to stay ahead of the market.</p></div><button className="primary-button" onClick={() => setModalAlert(null)} type="button"><Icon name="plus" size={15} /> Create Alert</button></section><div aria-label="Alert status" className="alert-tabs" role="tablist"><button aria-selected={statusFilter === 'active'} className={statusFilter === 'active' ? 'selected' : ''} onClick={() => setStatusFilter('active')} role="tab" type="button">Active <span>({counts.active})</span></button><button aria-selected={statusFilter === 'triggered'} className={statusFilter === 'triggered' ? 'selected' : ''} onClick={() => setStatusFilter('triggered')} role="tab" type="button">Triggered <span>({counts.triggered})</span></button><button aria-selected={statusFilter === 'disabled'} className={statusFilter === 'disabled' ? 'selected' : ''} onClick={() => setStatusFilter('disabled')} role="tab" type="button">Disabled <span>({counts.disabled})</span></button></div><section aria-labelledby="alerts-list-title" className="panel alerts-panel"><div className="panel-heading"><div><h2 id="alerts-list-title">Price alerts</h2><p>Monitor simulated price conditions across your favourite assets.</p></div><span className="live-indicator"><i /> Simulated data</span></div><div className="alerts-controls"><label className="alerts-search"><Icon name="search" size={15} /><input aria-label="Search alerts" onChange={(event) => setQuery(event.target.value)} placeholder="Search alerts..." value={query} /></label><label className="filter-select"><span>Condition</span><select aria-label="Filter by condition" onChange={(event) => setConditionFilter(event.target.value as 'all' | AlertCondition)} value={conditionFilter}><option value="all">All Conditions</option><option value="above">Above</option><option value="below">Below</option></select><Icon name="chevron-down" size={13} /></label><label className="filter-select"><span>Asset</span><select aria-label="Filter by asset" onChange={(event) => setAssetFilter(event.target.value)} value={assetFilter}><option value="all">All Assets</option>{assetOptions.map((asset) => <option key={asset.symbol} value={asset.symbol}>{asset.symbol}</option>)}</select><Icon name="chevron-down" size={13} /></label><label className="filter-select"><span>Status</span><select aria-label="Filter by status" onChange={(event) => setStatusFilter(event.target.value as 'all' | AlertStatus)} value={statusFilter}><option value="all">All Statuses</option><option value="active">Active</option><option value="triggered">Triggered</option><option value="disabled">Disabled</option></select><Icon name="chevron-down" size={13} /></label><button aria-label="More filters" className="filter-button" onClick={() => showToast('All available filters are shown.')} type="button"><Icon name="filter" size={15} /></button></div><div className="alert-table-header" aria-hidden="true"><span>Asset</span><span>Condition</span><span>Target Price</span><span>Last Price</span><span>Status</span><span>Created</span><span>Actions</span></div><div className="alert-list">{filteredAlerts.length > 0 ? filteredAlerts.map((alert) => <AlertRow alert={alert} key={alert.id} onDelete={deleteAlert} onEdit={(target) => setModalAlert(target)} onToggle={toggleAlert} />) : <div className="empty-state"><span><Icon name="bell" size={18} /></span><strong>No alerts found</strong><p>Try another filter or create a new price alert.</p></div>}</div><div className="alerts-footer"><span>Showing <strong>{filteredAlerts.length}</strong> of <strong>{alerts.length}</strong> alerts</span><span className="pagination"><button aria-label="Previous page" disabled type="button">‹</button><b>1</b><button aria-label="Next page" disabled type="button">›</button><label>Show <select aria-label="Rows per page" defaultValue="25"><option>10</option><option>25</option><option>50</option></select></label></span></div></section><div className="alerts-bottom-grid"><section className="panel create-alert-card"><div className="card-icon blue-icon"><Icon name="bell" size={19} /></div><div><h2>Create an alert</h2><p>Set a price target for any simulated asset and get notified when the condition is met.</p></div><button className="outline-button" onClick={() => setModalAlert(null)} type="button">Create Alert <Icon name="chevron-right" size={14} /></button></section><section className="panel how-alerts-card"><div className="card-icon purple-icon"><Icon name="shield" size={19} /></div><div><h2>How Alerts Work</h2><p>Price alerts are simulated for this frontend preview.</p></div><ul><li><span className="how-icon"><Icon name="activity" size={14} /></span><span><strong>Real-time monitoring</strong><small>We monitor prices and notify you instantly.</small></span></li><li><span className="how-icon"><Icon name="bell" size={14} /></span><span><strong>Multiple delivery channels</strong><small>Choose in-app, email, or push notifications.</small></span></li><li><span className="how-icon"><Icon name="settings" size={14} /></span><span><strong>Manage anytime</strong><small>Pause, edit, or delete alerts at any time.</small></span></li></ul></section></div><p className="simulation-note"><Icon name="activity" size={13} /> All alert prices and triggers are simulated. No live monitoring is connected.</p></div></main><div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast}</div>{modalAlert !== undefined && <AlertModal alert={modalAlert} onClose={() => setModalAlert(undefined)} onSave={saveAlert} />}</div>
}
