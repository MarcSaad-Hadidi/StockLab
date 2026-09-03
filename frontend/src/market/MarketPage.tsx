import { useMemo, useState } from 'react'
import { MarketShell } from './MarketShell'
import { MarketIcon } from './marketIcons'
import {
  filterMarketStocks,
  getMarketSections,
  marketStocks,
  type MarketFilter,
  type MarketStock,
} from './marketData'
import { StockLogo } from './StockLogo'

type MarketPageProps = {
  onOpenStock: (symbol: string) => void
}

const filterOptions: MarketFilter[] = ['All', 'Stocks', 'ETFs', 'Indices', 'Crypto']
const filterNounByFilter: Record<MarketFilter, string> = {
  All: 'Stocks',
  Stocks: 'Stocks',
  ETFs: 'ETFs',
  Indices: 'Indices',
  Crypto: 'Crypto',
  'US Market': 'US Market',
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
  return (
    <section aria-labelledby={`${title.toLowerCase().replaceAll(' ', '-')}-title`} className={`market-overview-card market-overview-card-${tone}`}>
      <div className="market-overview-heading">
        <h2 id={`${title.toLowerCase().replaceAll(' ', '-')}-title`}>{title}</h2>
        <button className="market-view-all" type="button" onClick={onViewAll}>View all</button>
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
  return (
    <tr>
      <td>
        <button aria-label={`Open ${stock.symbol} stock details`} className="market-symbol-cell" type="button" onClick={() => onOpenStock(stock.symbol)}>
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
          aria-label={`${favorite ? 'Remove' : 'Add'} ${stock.symbol} ${favorite ? 'from' : 'to'} favorites`}
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
  const [query, setQuery] = useState('')
  const [activeFilter, setActiveFilter] = useState<MarketFilter>('All')
  const [favoriteSymbols, setFavoriteSymbols] = useState<Set<string>>(() => new Set(['AAPL', 'MSFT']))
  const [moreFiltersOpen, setMoreFiltersOpen] = useState(false)
  const [favoriteOnly, setFavoriteOnly] = useState(false)
  const sections = useMemo(() => getMarketSections(marketStocks, activeFilter), [activeFilter])
  const filterNoun = filterNounByFilter[activeFilter]
  const overviewTitles = {
    popular: `Popular ${filterNoun}`,
    gainers: activeFilter === 'All' || activeFilter === 'Stocks' || activeFilter === 'US Market' ? 'Top Gainers' : `Top ${filterNoun} Gainers`,
    losers: activeFilter === 'All' || activeFilter === 'Stocks' || activeFilter === 'US Market' ? 'Top Losers' : `Top ${filterNoun} Losers`,
  }

  const filteredResults = useMemo(() => {
    const results = filterMarketStocks(marketStocks, query, activeFilter)
    return favoriteOnly ? results.filter((stock) => favoriteSymbols.has(stock.symbol)) : results
  }, [activeFilter, favoriteOnly, favoriteSymbols, query])

  const visibleResults = filteredResults.slice(0, 10)

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
    setFavoriteOnly(false)
    setMoreFiltersOpen(false)
  }

  const showAllStocks = () => {
    setQuery('')
    setActiveFilter('All')
    setFavoriteOnly(false)
  }

  return (
    <MarketShell>
      <section aria-labelledby="market-title" className="market-intro">
        <h1 id="market-title">Market</h1>
        <p>Explore stocks, ETFs and discover investment opportunities.</p>
      </section>

      <label className="market-search-bar">
        <MarketIcon name="search" size={17} />
        <span className="market-sr-only">Search for stocks, companies or keywords</span>
        <input
          aria-label="Search for stocks, companies or keywords"
          placeholder="Search for stocks, companies or keywords..."
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
        />
        <MarketIcon name="search" size={16} />
      </label>

      <div className="market-filter-row" aria-label="Market filters">
        <div className="market-filter-tabs">
          {filterOptions.map((filter) => (
            <button
              aria-pressed={activeFilter === filter}
              className={`market-filter-tab ${activeFilter === filter ? 'market-filter-tab-active' : ''}`}
              key={filter}
              type="button"
              onClick={() => setActiveFilter(filter)}
            >
              {filter}
            </button>
          ))}
        </div>
        <div className="market-filter-actions">
          <button
            aria-pressed={activeFilter === 'US Market'}
            className={`market-market-select ${activeFilter === 'US Market' ? 'market-market-select-active' : ''}`}
            type="button"
            onClick={() => setActiveFilter(activeFilter === 'US Market' ? 'All' : 'US Market')}
          >
            <span>US Market</span>
            <MarketIcon name="chevronDown" size={13} />
          </button>
          <div className="market-more-filter-wrap">
            <button
              aria-expanded={moreFiltersOpen}
              className={`market-more-filter ${moreFiltersOpen ? 'market-more-filter-active' : ''}`}
              type="button"
              onClick={() => setMoreFiltersOpen((open) => !open)}
            >
              <span>More Filters</span>
              <MarketIcon name="filter" size={14} />
            </button>
            {moreFiltersOpen && (
              <div aria-label="More filters" className="market-more-filter-menu" role="dialog">
                <label className="market-checkbox-row">
                  <input checked={favoriteOnly} type="checkbox" onChange={(event) => setFavoriteOnly(event.target.checked)} />
                  <span>Only show favorites</span>
                </label>
                <button className="market-clear-filters" type="button" onClick={clearFilters}>Clear filters</button>
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="market-overview-grid">
        <MarketOverviewCard title={overviewTitles.popular} tone="popular" stocks={sections.popular} onOpenStock={onOpenStock} onViewAll={showAllStocks} />
        <MarketOverviewCard title={overviewTitles.gainers} tone="positive" stocks={sections.gainers} onOpenStock={onOpenStock} onViewAll={showAllStocks} />
        <MarketOverviewCard title={overviewTitles.losers} tone="negative" stocks={sections.losers} onOpenStock={onOpenStock} onViewAll={showAllStocks} />
      </div>

      <section aria-labelledby="search-results-title" className="market-results-panel">
        <div className="market-results-heading">
          <h2 id="search-results-title">Search Results</h2>
          <div className="market-results-meta">
            <span>Showing {visibleResults.length > 0 ? 1 : 0}-{visibleResults.length} of {filteredResults.length} results</span>
            <div aria-label="Search results pages" className="market-pagination">
              <button aria-current="page" className="market-page-button market-page-button-active" type="button">1</button>
              <button className="market-page-button" type="button">2</button>
              <button className="market-page-button" type="button">3</button>
              <span>...</span>
              <button className="market-page-button" type="button">25</button>
              <button aria-label="Next results page" className="market-page-arrow" type="button"><MarketIcon name="chevronRight" size={13} /></button>
            </div>
          </div>
        </div>

        {visibleResults.length > 0 ? (
          <div className="market-table-scroll">
            <table className="market-results-table">
              <thead>
                <tr>
                  <th scope="col">Symbol</th>
                  <th scope="col">Company</th>
                  <th scope="col">Price</th>
                  <th scope="col">Daily Change</th>
                  <th scope="col">Market Cap</th>
                  <th scope="col"><span className="market-sr-only">Favorite</span></th>
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
            <strong>No stocks found</strong>
            <p>Try another symbol, company name or filter.</p>
          </div>
        )}
      </section>
    </MarketShell>
  )
}
