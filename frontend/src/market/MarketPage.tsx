import { useTranslation } from 'react-i18next'
import { useMemo, useState } from 'react'
import { MarketShell } from './MarketShell'
import { MarketIcon } from './marketIcons'
import {
  filterMarketStocks,
  getMarketSections,
  marketStocks,
  paginateMarketStocks,
  type MarketFilter,
  type MarketStock,
} from './marketData'
import { StockLogo } from './StockLogo'

type MarketPageProps = {
  onOpenStock: (symbol: string) => void
}

const filterOptions: MarketFilter[] = ['All', 'Stocks', 'ETFs', 'Indices', 'Crypto']
const resultsPerPage = 10
const filterLabelKeys: Record<MarketFilter, string> = {
  All: 'market.filters.all',
  Stocks: 'market.filters.stocks',
  ETFs: 'market.filters.etfs',
  Indices: 'market.filters.indices',
  Crypto: 'market.filters.crypto',
  'US Market': 'market.filters.usMarket',
}

const filterNounKeys: Record<MarketFilter, string> = {
  All: 'market.filterNouns.stocks',
  Stocks: 'market.filterNouns.stocks',
  ETFs: 'market.filterNouns.etfs',
  Indices: 'market.filterNouns.indices',
  Crypto: 'market.filterNouns.crypto',
  'US Market': 'market.filterNouns.usMarket',
}

function changeLabel(stock: MarketStock) {
  return `${stock.tone === 'positive' ? '↑' : '↓'} ${stock.changePercent}`
}

function MarketOverviewCard({
  title,
  stocks,
  onOpenStock,
  onViewAll,
  tone,
}: {
  title: string
  stocks: MarketStock[]
  onOpenStock: (symbol: string) => void
  onViewAll: () => void
  tone: 'popular' | 'positive' | 'negative'
}) {
  const { t } = useTranslation()
  return (
    <section aria-labelledby={`${title.toLowerCase().replaceAll(' ', '-')}-title`} className={`market-overview-card market-overview-card-${tone}`}>
      <div className="market-overview-heading">
        <h2 id={`${title.toLowerCase().replaceAll(' ', '-')}-title`}>{title}</h2>
        <button className="market-view-all" type="button" onClick={onViewAll}>{t('common.viewAll')}</button>
      </div>
      <div className="market-overview-list">
        {stocks.map((stock) => (
          <button className="market-overview-row" key={stock.symbol} type="button" onClick={() => onOpenStock(stock.symbol)}>
            <StockLogo symbol={stock.symbol} />
            <span className="market-stock-copy">
              <strong>{stock.symbol}</strong>
              <small>{stock.description}</small>
            </span>
            <span className="market-stock-quote">
              <strong>{stock.price}</strong>
              <span className={`market-stock-change ${stock.tone === 'positive' ? 'market-positive' : 'market-negative'}`}>{changeLabel(stock)}</span>
            </span>
          </button>
        ))}
      </div>
    </section>
  )
}

function SearchResultRow({
  stock,
  favorite,
  onOpenStock,
  onToggleFavorite,
}: {
  stock: MarketStock
  favorite: boolean
  onOpenStock: (symbol: string) => void
  onToggleFavorite: (symbol: string) => void
}) {
  const { t } = useTranslation()
  return (
    <tr>
      <td>
        <button aria-label={t('market.openStockDetails', { symbol: stock.symbol })} className="market-symbol-cell" type="button" onClick={() => onOpenStock(stock.symbol)}>
          <StockLogo symbol={stock.symbol} />
          <strong>{stock.symbol}</strong>
        </button>
      </td>
      <td>
        <button className="market-company-cell" type="button" onClick={() => onOpenStock(stock.symbol)}>{stock.company}</button>
      </td>
      <td className="market-number-cell">{stock.price}</td>
      <td className="market-number-cell">
        <span className={`market-table-change ${stock.tone === 'positive' ? 'market-positive' : 'market-negative'}`}>{changeLabel(stock)}</span>
      </td>
      <td className="market-number-cell">{stock.marketCap}</td>
      <td className="market-favorite-cell">
        <button
          aria-label={favorite ? t('market.removeFavorite', { symbol: stock.symbol }) : t('market.addFavorite', { symbol: stock.symbol })}
          aria-pressed={favorite}
          className={`market-favorite-button ${favorite ? 'market-favorite-button-active' : ''}`}
          type="button"
          onClick={() => onToggleFavorite(stock.symbol)}
        >
          <MarketIcon name="star" size={14} filled={favorite} />
        </button>
      </td>
    </tr>
  )
}

export function MarketPage({ onOpenStock }: MarketPageProps) {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [activeFilter, setActiveFilter] = useState<MarketFilter>('All')
  const [currentPage, setCurrentPage] = useState(1)
  const [favoriteSymbols, setFavoriteSymbols] = useState<Set<string>>(() => new Set(['AAPL', 'MSFT']))
  const [moreFiltersOpen, setMoreFiltersOpen] = useState(false)
  const [favoriteOnly, setFavoriteOnly] = useState(false)
  const sections = useMemo(() => getMarketSections(marketStocks, activeFilter), [activeFilter])
  const filterNoun = t(filterNounKeys[activeFilter])
  const overviewTitles = {
    popular: t('market.popular', { asset: filterNoun }),
    gainers: activeFilter === 'All' || activeFilter === 'Stocks' || activeFilter === 'US Market' ? t('market.topGainers') : t('market.topAssetGainers', { asset: filterNoun }),
    losers: activeFilter === 'All' || activeFilter === 'Stocks' || activeFilter === 'US Market' ? t('market.topLosers') : t('market.topAssetLosers', { asset: filterNoun }),
  }

  const filteredResults = useMemo(() => {
    const results = filterMarketStocks(marketStocks, query, activeFilter)
    return favoriteOnly ? results.filter((stock) => favoriteSymbols.has(stock.symbol)) : results
  }, [activeFilter, favoriteOnly, favoriteSymbols, query])

  const pagination = useMemo(
    () => paginateMarketStocks(filteredResults, currentPage, resultsPerPage),
    [currentPage, filteredResults],
  )
  const visibleResults = pagination.items

  const toggleFavorite = (symbol: string) => {
    setFavoriteSymbols((current) => {
      const next = new Set(current)
      if (next.has(symbol)) {
        next.delete(symbol)
      } else {
        next.add(symbol)
      }
      return next
    })
  }

  const clearFilters = () => {
    setQuery('')
    setActiveFilter('All')
    setCurrentPage(1)
    setFavoriteOnly(false)
    setMoreFiltersOpen(false)
  }

  const showAllResults = () => {
    setQuery('')
    setCurrentPage(1)
    setFavoriteOnly(false)
  }

  return (
    <MarketShell>
      <section aria-labelledby="market-title" className="market-intro">
        <h1 id="market-title">{t('market.title')}</h1>
        <p>{t('market.subtitle')}</p>
      </section>

      <label className="market-search-bar">
        <MarketIcon name="search" size={17} />
        <span className="market-sr-only">{t('market.searchLabel')}</span>
        <input
          aria-label={t('market.searchLabel')}
          placeholder={t('market.searchPlaceholder')}
          type="search"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value)
            setCurrentPage(1)
          }}
        />
        <MarketIcon name="search" size={16} />
      </label>

      <div className="market-filter-row" aria-label={t('market.filtersLabel')}>
        <div className="market-filter-tabs">
          {filterOptions.map((filter) => (
            <button
              aria-pressed={activeFilter === filter}
              className={`market-filter-tab ${activeFilter === filter ? 'market-filter-tab-active' : ''}`}
              key={filter}
              type="button"
              onClick={() => {
                setActiveFilter(filter)
                setCurrentPage(1)
              }}
            >
              {t(filterLabelKeys[filter])}
            </button>
          ))}
        </div>
        <div className="market-filter-actions">
          <button
            aria-pressed={activeFilter === 'US Market'}
            className={`market-market-select ${activeFilter === 'US Market' ? 'market-market-select-active' : ''}`}
            type="button"
            onClick={() => {
              setActiveFilter(activeFilter === 'US Market' ? 'All' : 'US Market')
              setCurrentPage(1)
            }}
          >
            <span>{t('market.filters.usMarket')}</span>
            <MarketIcon name="chevronDown" size={13} />
          </button>
          <div className="market-more-filter-wrap">
            <button
              aria-expanded={moreFiltersOpen}
              className={`market-more-filter ${moreFiltersOpen ? 'market-more-filter-active' : ''}`}
              type="button"
              onClick={() => setMoreFiltersOpen((open) => !open)}
            >
              <span>{t('market.moreFilters')}</span>
              <MarketIcon name="filter" size={14} />
            </button>
            {moreFiltersOpen && (
              <div aria-label={t('market.moreFilters')} className="market-more-filter-menu" role="dialog">
                <label className="market-checkbox-row">
                  <input
                    checked={favoriteOnly}
                    type="checkbox"
                    onChange={(event) => {
                      setFavoriteOnly(event.target.checked)
                      setCurrentPage(1)
                    }}
                  />
                  <span>{t('market.onlyFavorites')}</span>
                </label>
                <button className="market-clear-filters" type="button" onClick={clearFilters}>{t('common.clearFilters')}</button>
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="market-overview-grid">
        <MarketOverviewCard title={overviewTitles.popular} tone="popular" stocks={sections.popular} onOpenStock={onOpenStock} onViewAll={showAllResults} />
        <MarketOverviewCard title={overviewTitles.gainers} tone="positive" stocks={sections.gainers} onOpenStock={onOpenStock} onViewAll={showAllResults} />
        <MarketOverviewCard title={overviewTitles.losers} tone="negative" stocks={sections.losers} onOpenStock={onOpenStock} onViewAll={showAllResults} />
      </div>

      <section aria-labelledby="search-results-title" className="market-results-panel">
        <div className="market-results-heading">
          <h2 id="search-results-title">{t('market.searchResults')}</h2>
          <div className="market-results-meta">
            <span>{t('market.showingResults', { start: pagination.startIndex, end: pagination.endIndex, count: filteredResults.length })}</span>
            {pagination.totalPages > 1 && (
              <div aria-label={t('market.resultsPages')} className="market-pagination">
                <button
                  aria-label={t('common.previousPage')}
                  className="market-page-arrow"
                  disabled={pagination.currentPage === 1}
                  type="button"
                  onClick={() => setCurrentPage((page) => Math.max(1, page - 1))}
                >
                  <MarketIcon name="arrowLeft" size={13} />
                </button>
                {Array.from({ length: pagination.totalPages }, (_, index) => index + 1).map((page) => (
                  <button
                    aria-current={page === pagination.currentPage ? 'page' : undefined}
                    aria-label={t('market.goToResultsPage', { page })}
                    className={`market-page-button ${page === pagination.currentPage ? 'market-page-button-active' : ''}`}
                    key={page}
                    type="button"
                    onClick={() => setCurrentPage(page)}
                  >
                    {page}
                  </button>
                ))}
                <button
                  aria-label={t('common.nextPage')}
                  className="market-page-arrow"
                  disabled={pagination.currentPage === pagination.totalPages}
                  type="button"
                  onClick={() => setCurrentPage((page) => Math.min(pagination.totalPages, page + 1))}
                >
                  <MarketIcon name="chevronRight" size={13} />
                </button>
              </div>
            )}
          </div>
        </div>

        {visibleResults.length > 0 ? (
          <div className="market-table-scroll">
            <table className="market-results-table">
              <thead>
                <tr>
                  <th scope="col">{t('market.columns.symbol')}</th>
                  <th scope="col">{t('market.columns.company')}</th>
                  <th scope="col">{t('market.columns.price')}</th>
                  <th scope="col">{t('market.columns.dailyChange')}</th>
                  <th scope="col">{t('market.columns.marketCap')}</th>
                  <th scope="col"><span className="market-sr-only">{t('market.columns.favorite')}</span></th>
                </tr>
              </thead>
              <tbody>
                {visibleResults.map((stock) => (
                  <SearchResultRow
                    favorite={favoriteSymbols.has(stock.symbol)}
                    key={stock.symbol}
                    stock={stock}
                    onOpenStock={onOpenStock}
                    onToggleFavorite={toggleFavorite}
                  />
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="market-empty-state">
            <MarketIcon name="search" size={22} />
            <strong>{t('market.noStocksFound')}</strong>
            <p>{t('market.noStocksHint')}</p>
          </div>
        )}
      </section>
    </MarketShell>
  )
}
