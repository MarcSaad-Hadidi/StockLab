import { StockLogo, LogoAttribution } from '../market/StockLogo'
import { Sidebar } from '../components/layout/Sidebar'
import { TopBar } from '../components/layout/TopBar'
import { formatCurrency, localeForLanguage } from '../i18n/formatters'
import { useTranslation } from 'react-i18next'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useAuthSession } from '../auth/useAuthUser'
import type { PriceAlertErrorCode } from '../api/priceAlertsApi'
import type { AlertCondition, AlertStatus, AlertTerms, PriceAlert } from './alertsData'
import { AlertForm } from './AlertForm'
import { usePriceAlerts } from './usePriceAlerts'
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


function AlertRow({ alert, locked, onEdit, onToggle, onDelete }: {
  alert: PriceAlert
  locked: boolean
  onEdit: () => void
  onToggle: () => void
  onDelete: () => void
}) {
  const { i18n, t } = useTranslation()
  const locale = localeForLanguage(i18n.language)
  const active = alert.status === 'active'
  const canChange = alert.status !== 'triggered'
  const toggleLabel = t(active ? 'alerts.disableAlert' : 'alerts.enableAlert', { symbol: alert.symbol })
  const editLabel = t('alerts.editAlert', { symbol: alert.symbol })
  const deleteLabel = t('alerts.deleteAlert', { symbol: alert.symbol })
  return <article className="alert-row">
    <div className="alert-asset"><StockMark symbol={alert.symbol} /><span><strong>{alert.symbol}</strong><small>{alert.currency}</small></span></div>
    <div className="alert-cell"><span className="cell-label">{t('common.condition')}</span><ConditionBadge condition={alert.condition} /></div>
    <div className="alert-cell alert-number"><span className="cell-label">{t('common.targetPrice')}</span><strong>{formatCurrency(alert.targetPrice, i18n.language, 2, alert.currency)}</strong></div>
    <div className="alert-cell alert-number">
      <span className="cell-label">{t('alerts.columns.triggeredPrice')}</span>
      <strong>{formatCurrency(alert.triggeredPrice, i18n.language, 2, alert.currency)}</strong>
      {alert.triggeredAtUtc && <time className="alert-triggered-at" dateTime={alert.triggeredAtUtc}>
        {new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(alert.triggeredAtUtc))}
      </time>}
    </div>
    <div className="alert-cell"><span className="cell-label">{t('common.status')}</span><StatusBadge status={alert.status} /></div>
    <div className="alert-cell alert-date">
      <span className="cell-label">{t('common.created')}</span>
      <time dateTime={alert.createdAtUtc}>{new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(new Date(alert.createdAtUtc))}</time>
    </div>
    <div className="alert-actions">
      {canChange && <>
        <button aria-label={editLabel} className="table-action" disabled={locked} title={editLabel} type="button" onClick={onEdit}><Icon name="edit" size={14} /></button>
        <button aria-label={toggleLabel} className={active ? 'table-action action-disable' : 'table-action action-enable'} disabled={locked} title={toggleLabel} type="button" onClick={onToggle}><Icon name={active ? 'pause' : 'play'} size={14} /></button>
      </>}
      <button aria-label={deleteLabel} className="table-action action-delete" disabled={locked} title={deleteLabel} type="button" onClick={onDelete}><Icon name="trash" size={14} /></button>
    </div>
  </article>
}

type Notice = { area: 'list' | 'form' } & (
  { error: PriceAlertErrorCode } | { message: string; symbol: string }
)

const statusFilters = ['all', 'active', 'triggered', 'disabled'] as const
const columns = ['asset', 'condition', 'targetPrice', 'triggeredPrice', 'status', 'created', 'actions'] as const

export default function AlertsPage() {
  const session = useAuthSession()
  return <AlertsScreen key={session ? JSON.stringify([session.user.id, session.accessToken]) : 'guest'}
    authorization={session ? 'Bearer ' + session.accessToken : null} />
}

function AlertsScreen({ authorization }: { authorization: string | null }) {
  const { i18n, t } = useTranslation()
  const { alerts, loading, loadError, saving, change, refresh } = usePriceAlerts(authorization)
  const [query, setQuery] = useState('')
  const [conditionFilter, setConditionFilter] = useState<'all' | AlertCondition>('all')
  const [assetFilter, setAssetFilter] = useState('all')
  const [statusFilter, setStatusFilter] = useState<'all' | AlertStatus>('all')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [editing, setEditing] = useState<PriceAlert | null>(null)
  const [deleting, setDeleting] = useState<PriceAlert | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)
  const titleRef = useRef<HTMLHeadingElement>(null)
  const deletePromptRef = useRef<HTMLDivElement>(null)
  const locked = loading || saving || !!loadError

  useEffect(() => {
    document.title = t('alerts.pageTitle') + ' | StockLab'
  }, [i18n.language, t])

  useEffect(() => {
    if (deleting) deletePromptRef.current?.focus()
  }, [deleting])

  const symbols = [...new Set(alerts.map(alert => alert.symbol))].sort()
  const selectedAsset = symbols.includes(assetFilter) ? assetFilter : 'all'
  const counts = {
    all: alerts.length,
    active: alerts.filter(alert => alert.status === 'active').length,
    triggered: alerts.filter(alert => alert.status === 'triggered').length,
    disabled: alerts.filter(alert => alert.status === 'disabled').length,
  }
  const normalizedQuery = query.trim().toLowerCase()
  const filteredAlerts = alerts.filter(alert =>
    alert.symbol.toLowerCase().includes(normalizedQuery)
    && (conditionFilter === 'all' || conditionFilter === alert.condition)
    && (selectedAsset === 'all' || alert.symbol === selectedAsset)
    && (statusFilter === 'all' || statusFilter === alert.status))

  const openForm = (alert: PriceAlert | null) => {
    setEditing(alert)
    setNotice(null)
    titleRef.current?.focus()
    titleRef.current?.scrollIntoView?.({ block: 'nearest' })
  }

  async function save(terms: AlertTerms & { symbol: string }) {
    setNotice(null)
    const result = await change(editing ? {
      kind: 'update', id: editing.id, terms: { condition: terms.condition, targetPrice: terms.targetPrice },
    } : { kind: 'create', terms })
    if (!result.ok) {
      if (result.error) {
        const invalidated = editing && ['not_found', 'triggered', 'conflict'].includes(result.error)
        if (invalidated) setEditing(null)
        setNotice({ area: invalidated ? 'list' : 'form', error: result.error })
      }
      return false
    }
    setNotice({ area: 'form', message: editing ? 'alerts.alertUpdated' : 'alerts.alertCreatedToast', symbol: terms.symbol })
    setEditing(null)
    return true
  }

  async function updateStatus(alert: PriceAlert) {
    setNotice(null)
    const enabled = alert.status === 'disabled'
    const result = await change({ kind: 'toggle', id: alert.id, enabled })
    if (result.ok) {
      if (editing?.id === alert.id) setEditing(null)
      setNotice({ area: 'list', message: enabled ? 'alerts.alertEnabled' : 'alerts.alertDisabled', symbol: alert.symbol })
    } else if (result.error) setNotice({ area: 'list', error: result.error })
  }

  async function deleteAlert(alert: PriceAlert) {
    setNotice(null)
    const result = await change({ kind: 'remove', id: alert.id })
    if (result.ok) {
      setDeleting(null)
      if (editing?.id === alert.id) setEditing(null)
      setNotice({ area: 'list', message: 'alerts.alertDeleted', symbol: alert.symbol })
    } else if (result.error) {
      if (result.error === 'not_found') setDeleting(null)
      setNotice({ area: 'list', error: result.error })
    }
  }

  function feedback(area: Notice['area']) {
    if (notice?.area !== area) return null
    return 'error' in notice
      ? <p className="form-error" role="alert">{t(`alerts.errors.${notice.error}`)}</p>
      : <p className="alerts-notice" role="status">{t(notice.message, { symbol: notice.symbol })}</p>
  }

  return <div className="alerts-page stocklab-layout">
    <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
    <main className="alerts-main">
      <TopBar onMenuOpen={() => setSidebarOpen(true)} title={t('alerts.pageTitle')} />
      <div className="alerts-content">
        <section className="alerts-heading">
          <div><h1>{t('alerts.pageTitle')}</h1><p>{t('alerts.pageSubtitle')}</p></div>
          <button className="primary-button" disabled={locked} type="button" onClick={() => openForm(null)}><Icon name="plus" size={15} /> {t('alerts.createAlert')}</button>
        </section>
        <div aria-label={t('alerts.alertStatus')} className="alert-tabs" role="group">
          {statusFilters.map(status => <button aria-pressed={statusFilter === status}
            className={statusFilter === status ? 'selected' : ''} key={status} onClick={() => setStatusFilter(status)} type="button">
            {t(status === 'all' ? 'alerts.allStatuses' : `alerts.statuses.${status}`)}
            <span>({(loading || loadError) && !alerts.length ? '—' : counts[status]})</span>
          </button>)}
        </div>
        <section aria-labelledby="alerts-list-title" aria-busy={loading} className="panel alerts-panel">
          <div className="panel-heading">
            <div><h2 id="alerts-list-title">{t('alerts.priceAlerts')}</h2><p>{t('alerts.priceAlertsDescription')}</p></div>
            <button className="outline-button" disabled={!authorization || loading || saving} type="button" onClick={refresh}>{t('alerts.refresh')}</button>
          </div>
          <div className="alerts-controls">
            <label className="alerts-search"><Icon name="search" size={15} /><input aria-label={t('alerts.searchAlerts')} onChange={event => setQuery(event.target.value)} placeholder={t('alerts.searchAlerts')} value={query} /></label>
            <label className="filter-select"><span>{t('common.condition')}</span><select aria-label={t('common.condition')} onChange={event => setConditionFilter(event.target.value as 'all' | AlertCondition)} value={conditionFilter}><option value="all">{t('alerts.allConditions')}</option><option value="above">{t('alerts.conditions.above')}</option><option value="below">{t('alerts.conditions.below')}</option></select><Icon name="chevron-down" size={13} /></label>
            <label className="filter-select"><span>{t('common.asset')}</span><select aria-label={t('common.asset')} onChange={event => setAssetFilter(event.target.value)} value={selectedAsset}><option value="all">{t('alerts.allAssets')}</option>{symbols.map(symbol => <option key={symbol} value={symbol}>{symbol}</option>)}</select><Icon name="chevron-down" size={13} /></label>
            <label className="filter-select"><span>{t('common.status')}</span><select aria-label={t('common.status')} onChange={event => setStatusFilter(event.target.value as 'all' | AlertStatus)} value={statusFilter}><option value="all">{t('alerts.allStatuses')}</option><option value="active">{t('alerts.statuses.active')}</option><option value="triggered">{t('alerts.statuses.triggered')}</option><option value="disabled">{t('alerts.statuses.disabled')}</option></select><Icon name="chevron-down" size={13} /></label>
          </div>
          {loading && <p className="alerts-notice" role="status">{t('alerts.loading')}</p>}
          {loadError && <p className="form-error" role="alert">{t(loadError === 'unauthorized' ? 'alerts.errors.unauthorized' : 'alerts.loadError')}</p>}
          {feedback('list')}
          {deleting && <div className="alerts-delete" ref={deletePromptRef} role="group"
            aria-labelledby="alert-delete-question" tabIndex={-1}>
            <p id="alert-delete-question">{t('alerts.confirmDelete', { symbol: deleting.symbol })}</p>
            <button className="cancel-button" disabled={saving} type="button" onClick={() => setDeleting(null)}>{t('common.cancel')}</button>
            <button className="modal-primary" disabled={locked} type="button" onClick={() => { void deleteAlert(deleting) }}>{t('common.delete')}</button>
          </div>}
          <div className="alert-table-header" aria-hidden="true">{columns.map(key => <span key={key}>{t(`alerts.columns.${key}`)}</span>)}</div>
          <div className="alert-list">
            {filteredAlerts.map(alert => <AlertRow alert={alert} key={alert.id} locked={locked || deleting !== null}
              onEdit={() => openForm(alert)} onToggle={() => { void updateStatus(alert) }}
              onDelete={() => { setNotice(null); setDeleting(alert) }} />)}
            {!loading && !loadError && filteredAlerts.length === 0 && <div className="empty-state">
              <span><Icon name="bell" size={18} /></span><strong>{t(alerts.length ? 'alerts.noAlerts' : 'alerts.empty')}</strong>
              <p>{t(alerts.length ? 'alerts.noAlertsHint' : 'alerts.emptyHint')}</p>
            </div>}
          </div>
          <div className="alerts-footer"><span>{loading || loadError ? '—' : t('alerts.showing', { shown: filteredAlerts.length, total: alerts.length })}</span></div>
        </section>
        <div className="alerts-bottom-grid">
          <section aria-labelledby="create-alert-title" className="panel create-alert-card">
            <div className="create-alert-card-header"><div className="card-icon blue-icon"><Icon name="bell" size={19} /></div>
              <div><h2 id="create-alert-title" tabIndex={-1} ref={titleRef}>{t(editing ? 'alerts.editAlertTitle' : 'alerts.createAlert')}</h2>
                <p>{t(editing ? 'alerts.editDescription' : 'alerts.createAlertDescription')}</p></div>
            </div>
            {feedback('form')}
            <AlertForm key={editing?.id ?? 'create'} editing={editing} disabled={locked || deleting !== null} saving={saving}
              onCancel={() => openForm(null)} onSave={save} />
          </section>
          <section className="panel how-alerts-card">
            <div className="card-icon purple-icon"><Icon name="shield" size={19} /></div>
            <div><h2>{t('alerts.howAlertsWork')}</h2><p>{t('alerts.howAlertsWorkDescription')}</p></div>
            <ul>
              <li><span className="how-icon"><Icon name="activity" size={14} /></span><span><strong>{t('alerts.alertConditions')}</strong><small>{t('alerts.alertConditionsDescription')}</small></span></li>
              <li><span className="how-icon"><Icon name="bell" size={14} /></span><span><strong>{t('alerts.recordedTriggers')}</strong><small>{t('alerts.recordedTriggersDescription')}</small></span></li>
              <li><span className="how-icon"><Icon name="settings" size={14} /></span><span><strong>{t('alerts.manageAnytime')}</strong><small>{t('alerts.manageAnytimeDescription')}</small></span></li>
            </ul>
          </section>
        </div>
      </div>
      <LogoAttribution />
    </main>
  </div>
}
