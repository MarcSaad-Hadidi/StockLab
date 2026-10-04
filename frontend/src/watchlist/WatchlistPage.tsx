import { StockLogo, LogoAttribution } from '../market/StockLogo'
import { routeFor } from '../navigation/routes'
import { Sidebar } from '../components/layout/Sidebar'
import { TopBar } from '../components/layout/TopBar'
import { formatCurrency, formatNumber, formatSignedCurrency, formatSignedPercent } from '../i18n/formatters'
import { useTranslation } from 'react-i18next'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type { WatchlistItem } from './watchlistData'
import { useWatchlistData } from './useWatchlistData'
import { WatchlistFeedback } from './WatchlistFeedback'
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

function StockMark({ item }: { item: WatchlistItem }) { return <StockLogo symbol={item.symbol} /> }

function WatchlistRow({ item, removing, onDetails, onRemove }: { item: WatchlistItem; removing: boolean; onDetails: (item: WatchlistItem) => void; onRemove: (item: WatchlistItem) => void }) {
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
        <strong>{item.currency ? formatCurrency(item.price, undefined, 2, item.currency) : '—'}</strong>
      </div>
      <div className={`watchlist-cell watchlist-change ${item.tone}`}>
        <span className="cell-label">{t('common.today')}</span>
        <strong>{item.currency ? formatSignedCurrency(item.change, undefined, 2, item.currency) : '—'}</strong>
        <small>{formatSignedPercent(item.changePercent)}</small>
      </div>
      <div className="watchlist-trend"><span className="cell-label">{t('watchlist.chart7d')}</span><span title={t('businessData.unavailable')}>—</span></div>
      <div className="watchlist-actions">
        <button aria-label={`${t('watchlist.stockDetails')} — ${item.symbol}`} className="details-button" onClick={() => onDetails(item)} type="button"><Icon name="external-link" size={14} /></button>
        <button aria-label={`${t('alerts.createAlert')} — ${item.symbol}`} className="alert-button" disabled title={t('businessData.unavailable')} type="button"><Icon name="bell" size={14} /></button>
        <button aria-label={t('watchlist.removeFromWatchlist', { symbol: item.symbol })} className="remove-button" disabled={removing} onClick={() => onRemove(item)} type="button"><Icon name="x" size={15} /></button>
      </div>
    </article>
  )
}

export default function WatchlistPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('common.navigation.watchlist')} | StockLab`
  }, [i18n.language, t])
  const watchlist = useWatchlistData()
  const { items, loading, error, mutationError, pendingSymbols } = watchlist
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<'default' | 'price' | 'change'>('default')
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [toast, setToast] = useState<{ key: string; values?: Record<string, string | number> } | null>(null)
  const toastTimer = useRef<ReturnType<typeof window.setTimeout> | undefined>(undefined)
  useEffect(() => () => window.clearTimeout(toastTimer.current), [])

  const showToast = (key: string, values?: Record<string, string | number>) => {
    setToast({ key, values })
    window.clearTimeout(toastTimer.current)
    toastTimer.current = window.setTimeout(() => setToast(null), 2300)
  }

  const filteredItems = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase()
    const matchingItems = normalizedQuery.length === 0 ? [...items] : items.filter((item) => `${item.symbol} ${item.name} ${item.exchange}`.toLowerCase().includes(normalizedQuery))
    if (sort === 'price') return matchingItems.sort((a, b) => (b.price ?? -Infinity) - (a.price ?? -Infinity))
    if (sort === 'change') return matchingItems.sort((a, b) => (b.changePercent ?? -Infinity) - (a.changePercent ?? -Infinity))
    return matchingItems.sort((a, b) => Date.parse(b.createdAtUtc) - Date.parse(a.createdAtUtc) || a.symbol.localeCompare(b.symbol))
  }, [items, query, sort])

  const removeItem = async (item: WatchlistItem) => {
    if (await watchlist.remove(item.symbol)) showToast('watchlist.removedToast', { symbol: item.symbol })
  }

  const pricedItems = items.filter((item): item is WatchlistItem & { changePercent: number } => item.changePercent !== null)
  const topGainer = pricedItems.length > 0 ? pricedItems.reduce((top, item) => item.changePercent > top.changePercent ? item : top) : undefined
  const topLoser = pricedItems.length > 0 ? pricedItems.reduce((bottom, item) => item.changePercent < bottom.changePercent ? item : bottom) : undefined
  const averageChange = pricedItems.length > 0 ? pricedItems.reduce((total, item) => total + item.changePercent, 0) / pricedItems.length : null
  const ready = !loading && !error

  return (
    <div className="watchlist-page stocklab-layout">
      <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      <main className="watchlist-main">
        <TopBar onMenuOpen={() => setSidebarOpen(true)} title={t('common.navigation.watchlist')} />

        <div className="watchlist-content">
          <section className="watchlist-welcome"><div><p className="eyebrow">{t('watchlist.marketOverview')}</p><h1>{t('watchlist.myWatchlist')} <span>✦</span></h1><p className="welcome-copy">{t('watchlist.subtitle')}</p></div><a className="primary-button" href={routeFor('market')}><span>+</span> {t('watchlist.addStock')}</a></section>

          <section aria-label={t('watchlist.summaryLabel')} className="watchlist-summary">
            <div className="summary-card"><span className="summary-card-label">{t('watchlist.savedAssets')}</span><strong>{formatNumber(ready ? items.length : null, undefined, 0)}</strong><small>{ready ? t('watchlist.assetsShown', { shown: items.length, total: items.length }) : '—'}</small></div>
            <div className="summary-card"><span className="summary-card-label">{t('watchlist.topGainer')}</span><strong>{topGainer?.symbol ?? '—'}</strong><small className="positive">{topGainer ? formatSignedPercent(topGainer.changePercent) : '—'}</small></div>
            <div className="summary-card"><span className="summary-card-label">{t('watchlist.topLoser')}</span><strong>{topLoser?.symbol ?? '—'}</strong><small className="negative">{topLoser ? formatSignedPercent(topLoser.changePercent) : '—'}</small></div>
            <div className="summary-card"><span className="summary-card-label">{t('watchlist.averageMove')}</span><strong className={averageChange === null ? '' : averageChange >= 0 ? 'positive' : 'negative'}>{formatSignedPercent(averageChange)}</strong><small>{t('watchlist.acrossWatched')}</small></div>
          </section>

          <section aria-labelledby="watchlist-title" aria-busy={loading} className="panel watchlist-panel">
            <div className="panel-heading">
              <div><h2 id="watchlist-title">{t('watchlist.stocksWatching')}</h2><p>{t('watchlist.dataNote')}</p></div>
              <div className="watchlist-panel-actions">
                <button className="panel-action" disabled={loading || pendingSymbols.size > 0} onClick={watchlist.reload} type="button"><Icon name="activity" size={14} /> {t('watchlist.refreshPrices')}</button>
                <label className="sort-control"><span>{t('watchlist.sortBy')}</span><select aria-label={t('watchlist.sortLabel')} onChange={(event) => setSort(event.target.value as 'default' | 'price' | 'change')} value={sort}><option value="default">{t('watchlist.addedRecently')}</option><option value="price">{t('watchlist.priceHighLow')}</option><option value="change">{t('watchlist.dailyChange')}</option></select><Icon name="chevron-down" size={14} /></label>
              </div>
            </div>
            <div className="watchlist-controls"><label className="watchlist-filter"><Icon name="search" size={16} /><input aria-label={t('watchlist.filterStocks')} onChange={(event) => setQuery(event.target.value)} placeholder={t('watchlist.filterPlaceholder')} value={query} /></label></div>
            <WatchlistFeedback loading={loading} error={error} retry={watchlist.reload} />
            <WatchlistFeedback error={mutationError} />
            <div className="watchlist-columns" aria-hidden="true"><span>{t('watchlist.symbol')}</span><span>{t('watchlist.company')}</span><span>{t('watchlist.currentPrice')}</span><span>{t('common.today')}</span><span>{t('watchlist.chart7d')}</span><span>{t('common.actions')}</span></div>
            <div className="watchlist-list">
              {filteredItems.map(item => <WatchlistRow item={item} key={item.symbol} removing={loading || pendingSymbols.has(item.symbol)} onDetails={item => window.location.assign(`${routeFor('market')}?symbol=${encodeURIComponent(item.symbol)}`)} onRemove={item => { void removeItem(item) }} />)}
              {ready && filteredItems.length === 0 && <div className="empty-state"><span><Icon name="search" size={19} /></span><strong>{t(items.length === 0 ? 'businessData.watchlist' : 'market.noStocksFound')}</strong><p>{t(items.length === 0 ? 'watchlist.emptyHint' : 'watchlist.noStocksHint')}</p>{items.length === 0 && <a href={routeFor('market')}>{t('watchlist.addStock')}</a>}</div>}
            </div>
            <div className="watchlist-footer"><span>{t('watchlist.dataNote')}</span><strong>{ready ? t('watchlist.assetsShown', { shown: filteredItems.length, total: items.length }) : '—'}</strong></div>
          </section>
        </div>
      <LogoAttribution /></main>
      <div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast ? t(toast.key, toast.values) : ''}</div>
    </div>
  )
}
