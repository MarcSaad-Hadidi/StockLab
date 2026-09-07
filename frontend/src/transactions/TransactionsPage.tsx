import { Sidebar } from '../components/layout/Sidebar'
import { formatCurrency } from '../i18n/formatters'
import { routeFor } from '../navigation/routes'
import { useTranslation } from 'react-i18next'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  filterTransactions,
  paginateTransactions,
  transactions,
  transactionSummary,
  type Transaction,
  type TransactionAction,
  type TransactionFilters,
  type TransactionTypeFilter,
} from './transactionsData'
import './transactions.css'

type IconName =
  | 'activity'
  | 'arrow-left'
  | 'arrow-right'
  | 'bell'
  | 'briefcase'
  | 'calendar'
  | 'chart'
  | 'chevron-down'
  | 'filter'
  | 'globe'
  | 'grid'
  | 'list'
  | 'mail'
  | 'menu'
  | 'robot'
  | 'search'
  | 'sort'
  | 'star'
  | 'user'
  | 'wallet'

type IconProps = {
  name: IconName
  size?: number
  strokeWidth?: number
  className?: string
}

function Icon({ name, size = 16, strokeWidth = 1.65, className }: IconProps) {
  const common = {
    fill: 'none',
    stroke: 'currentColor',
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    strokeWidth,
  }

  const paths: Record<IconName, ReactNode> = {
    activity: <><path d="M3 12h3l2.2-6 3.6 12 2.2-6H21" {...common} /></>,
    'arrow-left': <path d="m15 6-6 6 6 6" {...common} />,
    'arrow-right': <path d="m9 6 6 6-6 6" {...common} />,
    bell: <><path d="M18 9a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4" {...common} /></>,
    briefcase: <><rect height="13" rx="2" width="18" x="3" y="7" {...common} /><path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 12h18M10 12v2h4v-2" {...common} /></>,
    calendar: <><rect height="17" rx="2" width="18" x="3" y="4" {...common} /><path d="M8 2v4M16 2v4M3 9h18" {...common} /></>,
    chart: <><path d="M4 19V5M4 19h17" {...common} /><path d="m7 15 3-4 3 2 5-7M16 6h2v2" {...common} /></>,
    'chevron-down': <path d="m6 9 6 6 6-6" {...common} />,
    filter: <><path d="M4 5h16M7 12h10M10 19h4" {...common} /><path d="M6 5v2M18 5v2M9 12v2M15 12v2M11 19v-2M13 19v-2" {...common} /></>,
    globe: <><circle cx="12" cy="12" r="8.5" {...common} /><path d="M3.5 12h17M12 3.5c2.1 2.3 3.1 5.1 3.1 8.5s-1 6.2-3.1 8.5c-2.1-2.3-3.1-5.1-3.1-8.5s1-6.2 3.1-8.5Z" {...common} /></>,
    grid: <><rect height="7" rx="1" width="7" x="3" y="3" {...common} /><rect height="7" rx="1" width="7" x="14" y="3" {...common} /><rect height="7" rx="1" width="7" x="3" y="14" {...common} /><rect height="7" rx="1" width="7" x="14" y="14" {...common} /></>,
    list: <><path d="M9 6h11M9 12h11M9 18h11" {...common} /><circle cx="4.5" cy="6" fill="currentColor" r="1" stroke="none" /><circle cx="4.5" cy="12" fill="currentColor" r="1" stroke="none" /><circle cx="4.5" cy="18" fill="currentColor" r="1" stroke="none" /></>,
    mail: <><rect height="14" rx="2" width="18" x="3" y="5" {...common} /><path d="m4 7 8 6 8-6" {...common} /></>,
    menu: <><path d="M4 7h16M4 12h16M4 17h16" {...common} /></>,
    robot: <><rect height="12" rx="3" width="15" x="4.5" y="7" {...common} /><path d="M12 3v4M8 12h.01M16 12h.01M8 16h8" {...common} /></>,
    search: <><circle cx="10.8" cy="10.8" r="6.5" {...common} /><path d="m16 16 4.5 4.5" {...common} /></>,
    sort: <><path d="M8 5v14M5 8l3-3 3 3M16 19V5M13 16l3 3 3-3" {...common} /></>,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z" {...common} />,
    user: <><circle cx="12" cy="8" r="3.2" {...common} /><path d="M5.2 20a6.8 6.8 0 0 1 13.6 0" {...common} /></>,
    wallet: <><path d="M4 6.5A2.5 2.5 0 0 1 6.5 4H19a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5.5A2.5 2.5 0 0 1 3 17.5v-9A2.5 2.5 0 0 1 4 6.5h15" {...common} /><path d="M21 10h-5a2 2 0 0 0 0 4h5M16.5 12h.01" {...common} /></>,
  }

  return <svg aria-hidden="true" className={className} height={size} viewBox="0 0 24 24" width={size}>{paths[name]}</svg>
}

type ToastHandler = (message: string) => void

type TopbarProps = {
  onMenuOpen: () => void
  onQueryChange: (query: string) => void
  onToast: ToastHandler
  query: string
}

function Topbar({ onMenuOpen, onQueryChange, onToast, query }: TopbarProps) {
  const { t } = useTranslation()
  return (
    <header className="transactions-topbar">
      <div className="breadcrumb">
        <button aria-label={t('common.openNavigation')} className="mobile-menu-button icon-button" onClick={onMenuOpen} type="button"><Icon name="menu" size={20} /></button>
        <strong>{t('common.navigation.transactions')}</strong>
      </div>
      <div className="topbar-actions">
        <label className="global-search">
          <Icon name="search" size={16} />
          <input aria-label={t('common.searchStocksEtfsNews')} onChange={(event) => onQueryChange(event.target.value)} placeholder={t('common.searchStocksEtfsNewsPlaceholder')} value={query} />
        </label>
        <button aria-label={t('common.notifications')} className="icon-button notification-button" onClick={() => onToast(t('common.notificationsCaughtUp'))} type="button"><Icon name="bell" size={18} /><i>2</i></button>
        <button aria-label={t('common.openMessages')} className="icon-button mail-button" onClick={() => onToast(t('common.noNewMessages'))} type="button"><Icon name="mail" size={17} /></button>
        <button aria-label={t('common.openAccountMenu')} className="topbar-account" onClick={() => window.location.assign(routeFor('profile'))} type="button"><span className="topbar-avatar">GA</span><Icon name="chevron-down" size={14} /></button>
      </div>
    </header>
  )
}

type SummaryCardProps = {
  detail: string
  icon: IconName
  label: string
  tone: 'blue' | 'green' | 'purple' | 'orange'
  value: string
}

function SummaryCard({ detail, icon, label, tone, value }: SummaryCardProps) {
  const { t } = useTranslation()
  return <article className="summary-card"><div className={`summary-icon summary-icon-${tone}`}><Icon name={icon} size={17} /></div><p>{label}</p><strong>{value}</strong><span className="summary-detail"><b>{detail}</b> <span>{t('transactions.vsLast30Days')}</span></span></article>
}

function formatDateParts(isoDate: string, language: string) {
  const date = new Date(isoDate)
  const locale = language === 'fr' ? 'fr-FR' : 'en-US'
  const dateFormatter = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC', year: 'numeric' })
  const timeFormatter = new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit', timeZone: 'UTC' })
  return { day: dateFormatter.format(date), time: timeFormatter.format(date) }
}

function formatFilterDate(value: string, language: string, emptyLabel: string) {
  if (!value) return emptyLabel
  const locale = language === 'fr' ? 'fr-FR' : 'en-US'
  return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC', year: 'numeric' }).format(new Date(`${value}T00:00:00.000Z`))
}

function TransactionRow({ transaction }: { transaction: Transaction }) {
  const { i18n, t } = useTranslation()
  const dateParts = formatDateParts(transaction.date, i18n.language)
  return <tr><td><time className="date-cell" dateTime={transaction.date}><span>{dateParts.day}</span><small>{dateParts.time}</small></time></td><td><strong className="symbol-cell">{transaction.symbol}</strong></td><td className="company-cell">{transaction.company}</td><td><span className={`action-pill action-${transaction.action.toLowerCase()}`}>{t(`common.${transaction.action === 'BUY' ? 'buy' : 'sell'}`)}</span></td><td className="number-cell">{transaction.quantity}</td><td className="money-cell">{formatCurrency(transaction.executionPrice)}</td><td className="money-cell"><strong>{formatCurrency(transaction.totalAmount)}</strong></td></tr>
}

const defaultFilters: TransactionFilters = {
  action: 'All',
  assetType: 'All',
  from: '2024-05-01',
  query: '',
  to: '2024-05-24',
}

function getPageItems(currentPage: number, totalPages: number): Array<number | 'ellipsis'> {
  if (totalPages <= 5) return Array.from({ length: totalPages }, (_, index) => index + 1)
  if (currentPage <= 3) return [1, 2, 3, 'ellipsis', totalPages]
  if (currentPage >= totalPages - 2) return [1, 'ellipsis', totalPages - 2, totalPages - 1, totalPages]
  return [1, 'ellipsis', currentPage, 'ellipsis', totalPages]
}

type FilterControlsProps = {
  activeFilterCount: number
  filters: TransactionFilters
  onActionChange: (action: TransactionAction) => void
  onFilterMenuToggle: () => void
  onFilterUpdate: (key: keyof TransactionFilters, value: string) => void
  onReset: () => void
  filterMenuOpen: boolean
}

function FilterControls({ activeFilterCount, filterMenuOpen, filters, onActionChange, onFilterMenuToggle, onFilterUpdate, onReset }: FilterControlsProps) {
  const { i18n, t } = useTranslation()
  const typeOptions: Array<{ label: string; value: TransactionTypeFilter }> = [
    { label: t('transactions.filters.allTypes'), value: 'All' },
    { label: t('market.filterNouns.stocks'), value: 'Stock' },
    { label: t('market.filterNouns.etfs'), value: 'ETF' },
    { label: t('market.filterNouns.crypto'), value: 'Crypto' },
  ]

  return (
    <div className="controls-wrap">
      <div className="transactions-controls">
        <label className="table-search">
          <Icon name="search" size={15} />
          <input aria-label={t('transactions.searchLabel')} onChange={(event) => onFilterUpdate('query', event.target.value)} placeholder={t('transactions.searchPlaceholder')} value={filters.query} />
        </label>
        <label className="date-range-control">
          <Icon name="calendar" size={14} />
          <span className="sr-only">{t('transactions.fromDate')}</span>
          <span className="date-field"><span aria-hidden="true">{formatFilterDate(filters.from, i18n.language, t('transactions.anyDate'))}</span><input aria-label={t('transactions.filterFromDate')} onChange={(event) => onFilterUpdate('from', event.target.value)} onInput={(event) => onFilterUpdate('from', event.currentTarget.value)} type="date" value={filters.from} /></span>
          <span aria-hidden="true" className="date-separator">–</span>
          <span className="sr-only">{t('transactions.toDate')}</span>
          <span className="date-field"><span aria-hidden="true">{formatFilterDate(filters.to, i18n.language, t('transactions.anyDate'))}</span><input aria-label={t('transactions.filterToDate')} onChange={(event) => onFilterUpdate('to', event.target.value)} onInput={(event) => onFilterUpdate('to', event.currentTarget.value)} type="date" value={filters.to} /></span>
        </label>
        <label className="type-select">
          <span className="sr-only">{t('common.assetType')}</span>
          <select aria-label={t('transactions.filterAssetType')} onChange={(event) => onFilterUpdate('assetType', event.target.value)} value={filters.assetType}>
            {typeOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
          <Icon name="chevron-down" size={13} />
        </label>
        <div aria-label={t('transactions.filterAction')} className="action-filters" role="group">
          <button aria-pressed={filters.action === 'BUY'} className="action-filter action-filter-buy" onClick={() => onActionChange('BUY')} type="button">{t('common.buy')}</button>
          <button aria-pressed={filters.action === 'SELL'} className="action-filter action-filter-sell" onClick={() => onActionChange('SELL')} type="button">{t('common.sell')}</button>
        </div>
        <div className="filter-popover-wrap">
          <button aria-expanded={filterMenuOpen} className={`filter-button ${activeFilterCount > 0 ? 'has-active-filter' : ''}`} onClick={onFilterMenuToggle} type="button"><span>{t('common.filters')}</span><Icon name="filter" size={14} />{activeFilterCount > 0 && <b>{activeFilterCount}</b>}</button>
          {filterMenuOpen && <div aria-label={t('transactions.filterSummary')} className="filter-menu" role="dialog"><strong>{t('transactions.transactionFilters')}</strong><p>{activeFilterCount > 0 ? t('transactions.customFiltersApplied', { count: activeFilterCount }) : t('transactions.useControls')}</p><button onClick={onReset} type="button">{t('transactions.resetFilters')}</button></div>}
        </div>
      </div>
    </div>
  )
}

function TransactionsTable({ items }: { items: Transaction[] }) {
  const { t } = useTranslation()
  if (items.length === 0) {
    return <div className="empty-state"><span><Icon name="search" size={19} /></span><strong>{t('transactions.noTransactions')}</strong><p>{t('transactions.noTransactionsHint')}</p></div>
  }

  return <div className="table-scroll"><table className="transactions-table"><thead><tr><th scope="col"><span>{t('transactions.columns.date')} <Icon name="sort" size={12} /></span></th><th scope="col">{t('market.columns.symbol')}</th><th scope="col">{t('market.columns.company')}</th><th scope="col">{t('transactions.columns.type')}</th><th scope="col">{t('common.quantity')}</th><th scope="col">{t('transactions.columns.executionPrice')}</th><th scope="col">{t('transactions.columns.totalAmount')}</th></tr></thead><tbody>{items.map((transaction) => <TransactionRow key={transaction.id} transaction={transaction} />)}</tbody></table></div>
}

type PaginationProps = {
  currentPage: number
  onPageChange: (page: number) => void
  totalPages: number
}

function Pagination({ currentPage, onPageChange, totalPages }: PaginationProps) {
  const { t } = useTranslation()
  return <nav aria-label={t('transactions.pagination')} className="pagination"><button aria-label={t('common.previousPage')} className="pagination-arrow" disabled={currentPage === 1} onClick={() => onPageChange(currentPage - 1)} type="button"><Icon name="arrow-left" size={13} /></button>{getPageItems(currentPage, totalPages).map((item, index) => item === 'ellipsis' ? <span aria-hidden="true" className="pagination-ellipsis" key={`ellipsis-${index}`}>…</span> : <button aria-current={item === currentPage ? 'page' : undefined} className={item === currentPage ? 'current' : ''} key={item} onClick={() => onPageChange(item)} type="button">{item}</button>)}<button aria-label={t('common.nextPage')} className="pagination-arrow" disabled={currentPage === totalPages} onClick={() => onPageChange(currentPage + 1)} type="button"><Icon name="arrow-right" size={13} /></button></nav>
}

export function TransactionsPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('common.navigation.transactions')} | StockLab`
  }, [i18n.language, t])
  const [filters, setFilters] = useState<TransactionFilters>(defaultFilters)
  const [filterMenuOpen, setFilterMenuOpen] = useState(false)
  const [page, setPage] = useState(1)
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [toast, setToast] = useState('')

  const filteredTransactions = useMemo(() => filterTransactions(transactions, filters), [filters])
  const pageData = useMemo(() => paginateTransactions(filteredTransactions, page), [filteredTransactions, page])
  const activeFilterCount = [
    filters.query.trim().length > 0,
    filters.assetType !== 'All',
    filters.action !== 'All',
    filters.from !== defaultFilters.from || filters.to !== defaultFilters.to,
  ].filter(Boolean).length

  const showToast = (message: string) => {
    setToast(message)
    window.setTimeout(() => setToast(''), 2200)
  }

  const updateFilter = (key: keyof TransactionFilters, value: string) => {
    setFilters((current) => ({ ...current, [key]: value } as TransactionFilters))
    setPage(1)
  }

  const changeAction = (action: TransactionAction) => {
    updateFilter('action', filters.action === action ? 'All' : action)
  }

  const resetFilters = () => {
    setFilters(defaultFilters)
    setPage(1)
    setFilterMenuOpen(false)
  }

  return (
    <div className="transactions-page stocklab-layout">
      <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
      <main className="transactions-main">
        <Topbar onMenuOpen={() => setSidebarOpen(true)} onQueryChange={(query) => updateFilter('query', query)} onToast={showToast} query={filters.query} />
        <div className="transactions-content">
          <section className="transactions-heading"><div><h1>{t('transactions.title')}</h1><p>{t('transactions.subtitle')}</p></div></section>

          <section aria-label={t('transactions.summaryLabel')} className="summary-grid">
            <SummaryCard detail="+14.3%" icon="activity" label={t('transactions.totalTrades')} tone="blue" value={transactionSummary.totalTrades.toString()} />
            <SummaryCard detail={transactionSummary.investedChange} icon="wallet" label={t('transactions.totalInvested')} tone="purple" value={formatCurrency(transactionSummary.totalInvested)} />
            <SummaryCard detail={transactionSummary.proceedsChange} icon="chart" label={t('transactions.totalProceeds')} tone="orange" value={formatCurrency(transactionSummary.totalProceeds)} />
            <SummaryCard detail={transactionSummary.pnlChange} icon="activity" label={t('transactions.netPnl')} tone="green" value={`+${formatCurrency(transactionSummary.netPnl)}`} />
          </section>

          <section aria-label={t('transactions.filtersLabel')} className="controls-panel panel">
            <FilterControls activeFilterCount={activeFilterCount} filterMenuOpen={filterMenuOpen} filters={filters} onActionChange={changeAction} onFilterMenuToggle={() => setFilterMenuOpen((open) => !open)} onFilterUpdate={updateFilter} onReset={resetFilters} />
          </section>

          <section aria-labelledby="transactions-table-title" className="panel transactions-panel">
            <h2 className="sr-only" id="transactions-table-title">{t('transactions.tableTitle')}</h2>
            <TransactionsTable items={pageData.items} />
            <div className="table-footer"><span>{t('transactions.showing', { start: pageData.startIndex, end: pageData.endIndex, count: filteredTransactions.length })}</span><Pagination currentPage={pageData.currentPage} onPageChange={setPage} totalPages={pageData.totalPages} /></div>
          </section>
          <p className="simulation-note"><Icon name="activity" size={13} /> {t('transactions.simulationNote')}</p>
        </div>
      </main>
      <div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast}</div>
    </div>
  )
}
