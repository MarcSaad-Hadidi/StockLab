import { MarketShell } from './MarketShell'
import { MarketIcon } from './marketIcons'
import { type MarketStock } from './marketData'
import { StockLogo } from './StockLogo'

type StockDetailsPageProps = {
  requestedSymbol: string
  stock?: MarketStock
  onBack: () => void
}

export function StockDetailsPage({ requestedSymbol, stock, onBack }: StockDetailsPageProps) {
  if (!stock) {
    return (
      <MarketShell>
        <section className="market-details-empty" aria-labelledby="missing-stock-title">
          <button className="market-back-link" type="button" onClick={onBack}>
            <MarketIcon name="arrowLeft" size={16} />
            Back to Market
          </button>
          <h1 id="missing-stock-title">Stock not found</h1>
          <p>We could not find a local preview for “{requestedSymbol}”.</p>
        </section>
      </MarketShell>
    )
  }

  const changeLabel = `${stock.tone === 'positive' ? '↑' : '↓'} ${stock.changePercent}`

  return (
    <MarketShell>
      <section aria-labelledby="stock-details-title" className="market-details-page">
        <button className="market-back-link" type="button" onClick={onBack}>
          <MarketIcon name="arrowLeft" size={16} />
          Back to Market
        </button>

        <div className="market-details-header">
          <div className="market-details-identity">
            <StockLogo size="large" symbol={stock.symbol} />
            <div>
              <p className="market-details-eyebrow">{stock.assetType} · {stock.market}</p>
              <h1 id="stock-details-title">{stock.company}</h1>
              <p>{stock.symbol}</p>
            </div>
          </div>
          <div className="market-details-quote">
            <strong>{stock.price}</strong>
            <span className={stock.tone === 'positive' ? 'market-positive' : 'market-negative'}>{changeLabel} today</span>
          </div>
        </div>

        <div className="market-details-grid">
          <article className="market-detail-card">
            <span>Market Cap</span>
            <strong>{stock.marketCap}</strong>
          </article>
          <article className="market-detail-card">
            <span>Asset type</span>
            <strong>{stock.assetType}</strong>
          </article>
          <article className="market-detail-card">
            <span>Trading market</span>
            <strong>{stock.market}</strong>
          </article>
        </div>

        <div className="market-details-note">
          <MarketIcon name="info" size={16} />
          <p>Stock Details is a frontend-only preview powered by static sample data for this issue.</p>
        </div>
      </section>
    </MarketShell>
  )
}
