import { Sidebar } from '../components/layout/Sidebar'
import { formatCurrency, formatNumber, formatSignedCurrency, formatSignedPercent } from '../i18n/formatters'
import { useTranslation } from 'react-i18next'
import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { watchlistItems, type WatchlistItem } from './watchlistData'
import './watchlist.css'

type IconName =
  | 'activity'
  | 'bell'
  | 'briefcase'
  | 'chart'
  | 'chevron-down'
  | 'chevron-right'
  | 'close'
  | 'external-link'
  | 'grid'
  | 'menu'
  | 'more'
  | 'pie-chart'
  | 'search'
  | 'settings'
  | 'star'
  | 'trending-up'
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
    close: <path d="m6 6 12 12M18 6 6 18" {...common} />,
    'external-link': <><path d="M14 5h5v5" {...common} /><path d="m19 5-8 8" {...common} /><path d="M18 13v5a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5" {...common} /></>,
    grid: <><rect x="3" y="3" width="7" height="7" rx="1" {...common} /><rect x="14" y="3" width="7" height="7" rx="1" {...common} /><rect x="3" y="14" width="7" height="7" rx="1" {...common} /><rect x="14" y="14" width="7" height="7" rx="1" {...common} /></>,
    menu: <path d="M4 7h16M4 12h16M4 17h16" {...common} />,
    more: <><circle cx="5" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="12" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="19" cy="12" r="1" fill="currentColor" stroke="none" /></>,
    'pie-chart': <><path d="M12 3v9h9" {...common} /><path d="M20.5 15A9 9 0 1 1 9 3.5" {...common} /></>,
    search: <><circle cx="10.8" cy="10.8" r="6.8" {...common} /><path d="m16 16 4.5 4.5" {...common} /></>,
    settings: <><circle cx="12" cy="12" r="3" {...common} /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-1.7 1.7-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5v.2h-2.4v-.2a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1L8 17l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H6.7v-2.4h.2a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9L8 8.6l1.7-1.7.1.1a1.7 1.7 0 0 0 1.9.3 1.7 1.7 0 0 0 1-1.5v-.2h2.4v.2a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1 1.7 1.7-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.2V14h-.2a1.7 1.7 0 0 0-1.5 1Z" {...common} /></>,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z" {...common} />,
    'trending-up': <><path d="M3 17 9 11l4 4 8-9" {...common} /><path d="M15 6h6v6" {...common} /></>,
    x: <path d="m6 6 12 12M18 6 6 18" {...common} />,
  }

  return <svg aria-hidden="true" className="icon" height={size} viewBox="0 0 24 24" width={size}>{paths[name]}</svg>
}

function StockMark({ item }: { item: WatchlistItem }) {
  return <span aria-hidden="true" className={`stock-mark stock-mark-${item.markTone}`}>{item.symbol === 'MSFT' ? <><i /><i /><i /><i /></> : item.symbol.slice(0, 1)}</span>
}

function TrendSparkline({ item }: { item: WatchlistItem }) {
  const seed = item.symbol.split('').reduce((total, character) => total + character.charCodeAt(0), 0)
  const points = Array.from({ length: 7 }, (_, index) => {
    const drift = item.tone === 'positive' ? index * 2.2 : -index * 1.8
    const variation = ((seed + index * 17) % 9) - 4
    return `${index * 16},${26 - drift - variation}`
  }).join(' ')

  return <svg aria-label={`${item.symbol} seven day trend`} className={`trend-sparkline ${item.tone}`} role="img" viewBox="0 0 96 32"><polyline fill="none" points={points} stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" /></svg>
}

function WatchlistRow({ item, onAlert, onDetails, onRemove }: { item: WatchlistItem; onAlert: (item: WatchlistItem) => void; onDetails: (item: WatchlistItem) => void; onRemove: (item: WatchlistItem) => void }) {
  const { t } = useTranslation()
  return (
    <article className="watchlist-row">
      <div className="watchlist-symbol">
        <StockMark item={item} />
        <strong>{item.symbol}</strong>
      </div>
      <div className="watchlist-company"><span>{item.name}</span><small>{item.exchange}</small></div>
      <div className="watchlist-cell watchlist-price">
        <span className="cell-label">{t('watchlist.currentPrice')}</span>
        <strong>{formatCurrency(item.price)}</strong>
      </div>
      <div className={`watchlist-cell watchlist-change ${item.tone}`}>
        <span className="cell-label">{t('common.today')}</span>
        <strong>{formatSignedCurrency(item.change)}</strong>
        <small>{formatSignedPercent(item.changePercent)}</small>
      </div>
      <div className="watchlist-trend"><span className="cell-label">{t('watchlist.chart7d')}</span><TrendSparkline item={item} /></div>
      <div className="watchlist-actions">
        <button aria-label={`${t('watchlist.stockDetails')} — ${item.symbol}`} className="details-button" onClick={() => onDetails(item)} type="button"><Icon name="external-link" size={14} /></button>
        <button aria-label={`${t('alerts.createAlert')} — ${item.symbol}`} className="alert-button" onClick={() => onAlert(item)} type="button"><Icon name="bell" size={14} /></button>
        <button aria-label={t('watchlist.removeFromWatchlist', { symbol: item.symbol })} className="remove-button" onClick={() => onRemove(item)} type="button"><Icon name="x" size={15} /></button>
      </div>
    </article>
  )
}

function StockDetails({ item, onClose }: { item: WatchlistItem; onClose: () => void }) {
  const { t } = useTranslation()
  return <div className="modal-backdrop" onClick={onClose} role="presentation"><section aria-labelledby="stock-details-title" aria-modal="true" className="stock-details-modal" onClick={(event) => event.stopPropagation()} role="dialog"><button aria-label={t('watchlist.closeStockDetails')} className="modal-close" onClick={onClose} type="button"><Icon name="x" size={17} /></button><div className="modal-stock-heading"><StockMark item={item} /><div><span>{item.exchange}</span><h2 id="stock-details-title">{item.symbol}</h2><p>{item.name}</p></div></div><div className="modal-price"><span>{t('watchlist.currentPrice')}</span><strong>{formatCurrency(item.price)}</strong><b className={item.tone}>{formatSignedCurrency(item.change)} ({formatSignedPercent(item.changePercent)})</b></div><div className="modal-detail-grid"><div><span>{t('watchlist.marketStatus')}</span><strong><i className="market-dot" /> {t('watchlist.open')}</strong></div><div><span>{t('watchlist.dayRange')}</span><strong>{formatCurrency(item.price * 0.97)} – {formatCurrency(item.price * 1.02)}</strong></div><div><span>{t('watchlist.weekRange')}</span><strong>{formatCurrency(item.price * 0.65)} – {formatCurrency(item.price * 1.28)}</strong></div><div><span>{t('watchlist.dataSource')}</span><strong>{t('common.simulated')}</strong></div></div><button className="modal-primary" onClick={onClose} type="button">{t('common.done')}</button></section></div>
}

function AlertModal({ item, onClose, onSave }: { item: WatchlistItem; onClose: () => void; onSave: (threshold: string) => void }) {
  const { t } = useTranslation()
  const [threshold, setThreshold] = useState(item.price.toFixed(2))
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    onSave(threshold)
  }

  return <div className="modal-backdrop" onClick={onClose} role="presentation"><section aria-labelledby="alert-title" aria-modal="true" className="alert-modal" onClick={(event) => event.stopPropagation()} role="dialog"><button aria-label={t('stockDetails.closeCreateAlert')} className="modal-close" onClick={onClose} type="button"><Icon name="x" size={17} /></button><div className="alert-icon"><Icon name="bell" size={21} /></div><h2 id="alert-title">{t('alerts.createAlert')}</h2><p>{t('watchlist.alertPrompt')} <strong>{item.symbol}</strong>.</p><form onSubmit={submit}><label htmlFor="alert-threshold">{t('common.targetPrice')}</label><div className="alert-input"><span>$</span><input id="alert-threshold" inputMode="decimal" min="0" onChange={(event) => setThreshold(event.target.value)} required step="0.01" type="number" value={threshold} /></div><div className="alert-form-actions"><button className="cancel-button" onClick={onClose} type="button">{t('common.cancel')}</button><button className="modal-primary" type="submit">{t('watchlist.saveAlert')}</button></div></form></section></div>
}

export default function WatchlistPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('common.navigation.watchlist')} | StockLab`
  }, [i18n.language, t])
  const [items, setItems] = useState(watchlistItems)
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<'default' | 'price' | 'change'>('default')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [toast, setToast] = useState<{ key: string; values?: Record<string, string | number> } | null>(null)
  const [detailTarget, setDetailTarget] = useState<WatchlistItem | null>(null)
  const [alertTarget, setAlertTarget] = useState<WatchlistItem | null>(null)

  const showToast = (key: string, values?: Record<string, string | number>) => {
    setToast({ key, values })
    window.setTimeout(() => setToast(null), 2300)
  }

  const filteredItems = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase()
    const matchingItems = normalizedQuery.length === 0 ? [...items] : items.filter((item) => `${item.symbol} ${item.name} ${item.exchange}`.toLowerCase().includes(normalizedQuery))
    if (sort === 'price') return matchingItems.sort((a, b) => b.price - a.price)
    if (sort === 'change') return matchingItems.sort((a, b) => b.changePercent - a.changePercent)
    return matchingItems
  }, [items, query, sort])

  const removeItem = (item: WatchlistItem) => {
    setItems((current) => current.filter((candidate) => candidate.symbol !== item.symbol))
    showToast('watchlist.removedToast', { symbol: item.symbol })
  }

  const topGainer = items.length > 0 ? items.reduce((top, item) => item.changePercent > top.changePercent ? item : top, items[0]) : undefined
  const topLoser = items.length > 0 ? items.reduce((bottom, item) => item.changePercent < bottom.changePercent ? item : bottom, items[0]) : undefined
  const averageChange = items.length > 0 ? items.reduce((total, item) => total + item.changePercent, 0) / items.length : 0

  return (
    <div className="watchlist-page stocklab-layout">
      <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      <main className="watchlist-main">
        <header className="watchlist-topbar"><button aria-label={t('common.openNavigation')} className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={22} /></button><div className="breadcrumb"><span>{t('common.workspace')}</span><Icon name="chevron-right" size={14} /><strong>{t('common.navigation.watchlist')}</strong></div><div className="topbar-actions"><label className="global-search"><Icon name="search" size={17} /><input aria-label={t('watchlist.searchLabel')} onChange={(event) => setQuery(event.target.value)} placeholder={t('dashboard.searchPlaceholder')} value={query} /></label><button aria-label={t('common.notifications')} className="icon-button notification-button" onClick={() => showToast('common.notificationsCaughtUp')} type="button"><Icon name="bell" size={19} /><i /></button><span className="topbar-avatar">MS</span></div></header>

        <div className="watchlist-content">
          <section className="watchlist-welcome"><div><p className="eyebrow">{t('watchlist.marketOverview')}</p><h1>{t('watchlist.myWatchlist')} <span>✦</span></h1><p className="welcome-copy">{t('watchlist.subtitle')}</p></div><button className="primary-button" onClick={() => showToast('watchlist.addStockHint')} type="button"><span>+</span> {t('watchlist.addStock')}</button></section>

          <section aria-label={t('watchlist.summaryLabel')} className="watchlist-summary">
            <div className="summary-card"><span className="summary-card-label">{t('watchlist.savedAssets')}</span><strong>{formatNumber(items.length, undefined, 0)}</strong><small>{t('watchlist.assetsShown', { shown: items.length, total: items.length })}</small></div>
            <div className="summary-card"><span className="summary-card-label">{t('watchlist.topGainer')}</span><strong>{topGainer?.symbol ?? '—'}</strong><small className="positive">{topGainer ? formatSignedPercent(topGainer.changePercent) : '—'}</small></div>
            <div className="summary-card"><span className="summary-card-label">{t('watchlist.topLoser')}</span><strong>{topLoser?.symbol ?? '—'}</strong><small className="negative">{topLoser ? formatSignedPercent(topLoser.changePercent) : '—'}</small></div>
            <div className="summary-card"><span className="summary-card-label">{t('watchlist.averageMove')}</span><strong className={averageChange >= 0 ? 'positive' : 'negative'}>{formatSignedPercent(averageChange)}</strong><small>{t('watchlist.acrossWatched')}</small></div>
          </section>

          <section aria-labelledby="watchlist-title" className="panel watchlist-panel"><div className="panel-heading"><div><h2 id="watchlist-title">{t('watchlist.stocksWatching')}</h2><p>{t('watchlist.previewPrices')}</p></div><div className="watchlist-panel-actions"><button className="panel-action" onClick={() => showToast('watchlist.pricesUpdated')} type="button"><Icon name="activity" size={14} /> {t('watchlist.refreshPrices')}</button><label className="sort-control"><span>{t('watchlist.sortBy')}</span><select aria-label={t('watchlist.sortLabel')} onChange={(event) => setSort(event.target.value as 'default' | 'price' | 'change')} value={sort}><option value="default">{t('watchlist.addedRecently')}</option><option value="price">{t('watchlist.priceHighLow')}</option><option value="change">{t('watchlist.dailyChange')}</option></select><Icon name="chevron-down" size={14} /></label></div></div><div className="watchlist-controls"><label className="watchlist-filter"><Icon name="search" size={16} /><input aria-label={t('watchlist.filterStocks')} onChange={(event) => setQuery(event.target.value)} placeholder={t('watchlist.filterPlaceholder')} value={query} /></label></div><div className="watchlist-columns" aria-hidden="true"><span>{t('watchlist.symbol')}</span><span>{t('watchlist.company')}</span><span>{t('watchlist.currentPrice')}</span><span>{t('common.today')}</span><span>{t('watchlist.chart7d')}</span><span>{t('common.actions')}</span></div><div className="watchlist-list">{filteredItems.length > 0 ? filteredItems.map((item) => <WatchlistRow item={item} key={item.symbol} onAlert={setAlertTarget} onDetails={setDetailTarget} onRemove={removeItem} />) : <div className="empty-state"><span><Icon name="search" size={19} /></span><strong>{t('market.noStocksFound')}</strong><p>{t('watchlist.noStocksHint')}</p></div>}</div><div className="watchlist-footer"><span><i className="live-dot" /> {t('watchlist.liveUpdateNote')}</span><strong>{t('watchlist.assetsShown', { shown: filteredItems.length, total: items.length })}</strong></div></section>
        </div>
      </main>
      <div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast ? t(toast.key, toast.key === 'watchlist.alertSetFormatted' ? { ...toast.values, price: formatCurrency(Number(toast.values?.price)) } : toast.values) : ''}</div>
      {detailTarget && <StockDetails item={detailTarget} onClose={() => setDetailTarget(null)} />}
      {alertTarget && <AlertModal item={alertTarget} onClose={() => setAlertTarget(null)} onSave={(threshold) => { setAlertTarget(null); showToast('watchlist.alertSetFormatted', { symbol: alertTarget.symbol, price: Number(threshold) }) }} />}
    </div>
  )
}
