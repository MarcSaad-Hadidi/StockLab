import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { MarketShell } from './MarketShell'
import { MarketIcon } from './marketIcons'
import { type MarketStock } from './marketData'
import { StockLogo } from './StockLogo'
import {
  calculateTradeTotal,
  getStockDetails,
  type AiTraderRecommendation,
  type StockDetails,
} from './stockDetailsData'

type StockDetailsPageProps = {
  requestedSymbol: string
  stock?: MarketStock
  onBack: () => void
}

type TradeSide = 'BUY' | 'SELL'
type AlertCondition = 'above' | 'below'

const chartRanges = ['1D', '5D', '1M', '3M', '6M', 'YTD', '1Y', '5Y', 'MAX'] as const
type ChartRange = typeof chartRanges[number]

const detailTabs = ['Overview', 'Chart', 'Financials', 'News', 'Key Metrics', 'Forecast', 'AI Insights'] as const
type DetailTab = typeof detailTabs[number]

function formatCurrency(value: number) {
  return `$${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function changeLabel(details: StockDetails) {
  const sign = details.tone === 'positive' ? '+' : '-'
  return `${sign}${formatCurrency(Math.abs(details.changeAmount))} (${details.changePercent}) today`
}

function getVisibleHistory(details: StockDetails, range: ChartRange) {
  if (range === '1D') return details.history.slice(-2)
  if (range === '5D') return details.history.slice(-5)
  if (range === '1M') return details.history.slice(-6)
  return details.history
}

function PriceChart({ details, range }: { details: StockDetails; range: ChartRange }) {
  const history = getVisibleHistory(details, range)
  const width = 720
  const height = 270
  const plotLeft = 52
  const plotRight = 10
  const plotTop = 18
  const plotBottom = 38
  const plotWidth = width - plotLeft - plotRight
  const plotHeight = height - plotTop - plotBottom
  const values = history.map((point) => point.value)
  const minValue = Math.floor(Math.min(...values) - 3)
  const maxValue = Math.ceil(Math.max(...values) + 3)
  const valueRange = Math.max(1, maxValue - minValue)
  const points = history.map((point, index) => {
    const x = plotLeft + (history.length === 1 ? plotWidth / 2 : (index / (history.length - 1)) * plotWidth)
    const y = plotTop + ((maxValue - point.value) / valueRange) * plotHeight
    return { x, y, ...point }
  })
  const linePath = points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(' ')
  const areaPath = `${linePath} L ${points.at(-1)?.x.toFixed(2)} ${plotTop + plotHeight} L ${points[0]?.x.toFixed(2)} ${plotTop + plotHeight} Z`
  const yTicks = Array.from({ length: 4 }, (_, index) => maxValue - (valueRange / 3) * index)
  const labelIndexes = [...new Set([
    0,
    Math.floor((history.length - 1) * 0.25),
    Math.floor((history.length - 1) * 0.5),
    Math.floor((history.length - 1) * 0.75),
    history.length - 1,
  ])]

  return (
    <div className="stock-price-chart" data-range={range}>
      <svg aria-label={`${details.symbol} historical price chart for ${range}`} role="img" viewBox={`0 0 ${width} ${height}`}>
        {yTicks.map((tick, index) => {
          const y = plotTop + (index / 3) * plotHeight
          return (
            <g key={`tick-${tick}`}>
              <line className="stock-chart-grid-line" x1={plotLeft} x2={width - plotRight} y1={y} y2={y} />
              <text className="stock-chart-y-label" x="6" y={y + 4}>{formatCurrency(tick).replace('.00', '')}</text>
            </g>
          )
        })}
        <path className="stock-chart-area" d={areaPath} />
        <path className="stock-chart-line" d={linePath} />
        {points.at(-1) && (
          <g>
            <line className="stock-chart-guide" x1={points.at(-1)?.x} x2={points.at(-1)?.x} y1={points.at(-1)?.y} y2={plotTop + plotHeight} />
            <circle className="stock-chart-point" cx={points.at(-1)?.x} cy={points.at(-1)?.y} r="4" />
            <rect className="stock-chart-value-pill" height="22" rx="5" width="56" x={Math.min(width - 62, Math.max(plotLeft, (points.at(-1)?.x ?? width) - 26))} y={Math.max(5, (points.at(-1)?.y ?? 20) - 30)} />
            <text className="stock-chart-value-label" x={Math.min(width - 34, Math.max(plotLeft + 28, (points.at(-1)?.x ?? width)))} y={Math.max(20, (points.at(-1)?.y ?? 20) - 15)}>$191.45</text>
          </g>
        )}
        {labelIndexes.map((index) => {
          const point = points[index]
          return point ? <text className="stock-chart-x-label" key={`${point.label}-${index}`} x={point.x} y={height - 8}>{point.label}</text> : null
        })}
      </svg>
    </div>
  )
}

function StatCard({ label, value }: { label: string; value: string }) {
  return (
    <article className="stock-stat-card">
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  )
}

function MetricItem({ label, value, tone }: { label: string; value: string; tone?: 'positive' | 'neutral' }) {
  return (
    <div className="stock-metric-item">
      <span>{label}</span>
      <strong className={tone === 'positive' ? 'stock-positive' : undefined}>{value}</strong>
    </div>
  )
}

function ConfidenceRing({ confidence }: { confidence: number }) {
  const circumference = 2 * Math.PI * 32
  const progress = (confidence / 100) * circumference

  return (
    <div aria-label={`${confidence}% AI confidence`} className="stock-confidence-ring">
      <svg aria-hidden="true" viewBox="0 0 80 80">
        <circle className="stock-confidence-track" cx="40" cy="40" r="32" />
        <circle className="stock-confidence-progress" cx="40" cy="40" r="32" strokeDasharray={`${progress} ${circumference - progress}`} />
      </svg>
      <strong>{confidence}%</strong>
    </div>
  )
}

function recommendationClass(recommendation: AiTraderRecommendation) {
  return recommendation === 'BUY' ? 'stock-ai-buy' : recommendation === 'SELL' ? 'stock-ai-sell' : 'stock-ai-hold'
}

type FocusableRef = { current: HTMLElement | null }

function useDialogAccessibility(onClose: () => void, dialogRef: FocusableRef, initialFocusRef: FocusableRef) {
  const onCloseRef = useRef(onClose)
  const previousActiveElementRef = useRef<HTMLElement | null>(null)

  useEffect(() => {
    onCloseRef.current = onClose
  }, [onClose])

  useEffect(() => {
    previousActiveElementRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const dialog = dialogRef.current
    if (!dialog) return undefined

    const focusableSelector = [
      'a[href]',
      'button:not([disabled])',
      'input:not([disabled])',
      'select:not([disabled])',
      'textarea:not([disabled])',
      '[tabindex]:not([tabindex="-1"])',
    ].join(',')
    const focusInitialElement = () => {
      const focusTarget = initialFocusRef.current ?? dialog.querySelector<HTMLElement>(focusableSelector)
      focusTarget?.focus()
    }
    const frameId = window.requestAnimationFrame(focusInitialElement)
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        onCloseRef.current()
        return
      }
      if (event.key !== 'Tab') return

      const focusableElements = Array.from(dialog.querySelectorAll<HTMLElement>(focusableSelector))
      if (focusableElements.length === 0) {
        event.preventDefault()
        dialog.focus()
        return
      }

      const firstElement = focusableElements[0]
      const lastElement = focusableElements[focusableElements.length - 1]
      if (event.shiftKey && document.activeElement === firstElement) {
        event.preventDefault()
        lastElement.focus()
      } else if (!event.shiftKey && document.activeElement === lastElement) {
        event.preventDefault()
        firstElement.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => {
      window.cancelAnimationFrame(frameId)
      document.removeEventListener('keydown', handleKeyDown)
      const previousActiveElement = previousActiveElementRef.current
      if (previousActiveElement && document.contains(previousActiveElement)) previousActiveElement.focus()
      previousActiveElementRef.current = null
    }
  }, [dialogRef, initialFocusRef])
}

function AiInsightCard({ details }: { details: StockDetails }) {
  const insight = details.aiInsight

  return (
    <article className="stock-ai-card">
      <div className="stock-card-heading">
        <div className="stock-card-title">
          <span className="stock-card-icon stock-card-icon-ai"><MarketIcon name="robot" size={16} /></span>
          <h2>AI Insight <small>by StockLab AI</small></h2>
        </div>
        <span className="stock-updated-pill">{insight.updatedAt}</span>
      </div>
      <div className="stock-ai-summary">
        <div>
          <span className={`stock-ai-recommendation ${recommendationClass(insight.recommendation)}`}>{insight.recommendation}</span>
          <strong>High Confidence</strong>
        </div>
        <ConfidenceRing confidence={insight.confidence} />
      </div>
      <p className="stock-ai-copy">{insight.summary}</p>
      <div className="stock-key-factors">
        <span>Key Factors</span>
        <div>{insight.keyFactors.map((factor) => <span key={factor}>{factor}</span>)}</div>
      </div>
    </article>
  )
}

function AlertModal({ details, onClose, onCreated }: { details: StockDetails; onClose: () => void; onCreated: () => void }) {
  const [condition, setCondition] = useState<AlertCondition>('above')
  const [targetPrice, setTargetPrice] = useState(details.price.toFixed(2))
  const [error, setError] = useState('')
  const dialogRef = useRef<HTMLElement | null>(null)
  const closeButtonRef = useRef<HTMLButtonElement | null>(null)

  useDialogAccessibility(onClose, dialogRef, closeButtonRef)

  const submitAlert = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const parsedPrice = Number(targetPrice)
    if (!Number.isFinite(parsedPrice) || parsedPrice <= 0) {
      setError('Enter a target price greater than zero.')
      return
    }
    onCreated()
  }

  return (
    <div className="stock-modal-backdrop" onClick={onClose} role="presentation">
      <section aria-labelledby="stock-alert-title" aria-modal="true" className="stock-modal" onClick={(event) => event.stopPropagation()} ref={dialogRef} role="dialog" tabIndex={-1}>
        <button aria-label="Close create alert dialog" className="stock-modal-close" onClick={onClose} ref={closeButtonRef} type="button"><MarketIcon name="close" size={17} /></button>
        <span className="stock-modal-icon stock-modal-icon-alert"><MarketIcon name="bell" size={20} /></span>
        <h2 id="stock-alert-title">Create Alert</h2>
        <p>Set a simulated price trigger for {details.symbol}.</p>
        <form onSubmit={submitAlert}>
          <label htmlFor="stock-alert-symbol">Asset</label>
          <input disabled id="stock-alert-symbol" value={`${details.symbol} — ${details.company}`} />
          <fieldset>
            <legend>Condition</legend>
            <div className="stock-choice-row">
              <button aria-pressed={condition === 'above'} className={condition === 'above' ? 'stock-choice-active stock-choice-positive' : ''} onClick={() => setCondition('above')} type="button">Above</button>
              <button aria-pressed={condition === 'below'} className={condition === 'below' ? 'stock-choice-active stock-choice-negative' : ''} onClick={() => setCondition('below')} type="button">Below</button>
            </div>
          </fieldset>
          <label htmlFor="stock-alert-target">Target price</label>
          <div className="stock-input-with-prefix"><span>$</span><input id="stock-alert-target" inputMode="decimal" min="0.01" onChange={(event) => { setTargetPrice(event.target.value); setError('') }} step="0.01" type="number" value={targetPrice} /></div>
          {error && <p className="stock-form-error" role="alert">{error}</p>}
          <div className="stock-modal-actions"><button className="stock-secondary-button" onClick={onClose} type="button">Cancel</button><button className="stock-primary-button" type="submit">Create Alert</button></div>
        </form>
      </section>
    </div>
  )
}

function TradeConfirmationModal({ details, side, quantity, total, onClose, onConfirm }: { details: StockDetails; side: TradeSide; quantity: number; total: number; onClose: () => void; onConfirm: () => void }) {
  const isBuy = side === 'BUY'
  const dialogRef = useRef<HTMLElement | null>(null)
  const closeButtonRef = useRef<HTMLButtonElement | null>(null)

  useDialogAccessibility(onClose, dialogRef, closeButtonRef)

  return (
    <div className="stock-modal-backdrop" onClick={onClose} role="presentation">
      <section aria-labelledby="trade-confirmation-title" aria-modal="true" className="stock-modal stock-trade-confirmation" onClick={(event) => event.stopPropagation()} ref={dialogRef} role="dialog" tabIndex={-1}>
        <button aria-label="Close trade confirmation dialog" className="stock-modal-close" onClick={onClose} ref={closeButtonRef} type="button"><MarketIcon name="close" size={17} /></button>
        <span className={`stock-modal-icon ${isBuy ? 'stock-modal-icon-buy' : 'stock-modal-icon-sell'}`}><MarketIcon name={isBuy ? 'check' : 'chart'} size={20} /></span>
        <h2 id="trade-confirmation-title">Confirm {isBuy ? 'Buy' : 'Sell'} Order</h2>
        <p>Review the simulated order before placing it.</p>
        <div className="stock-confirmation-list">
          <div><span>Asset</span><strong>{details.symbol} · {details.company}</strong></div>
          <div><span>Order type</span><strong>Market Order</strong></div>
          <div><span>Quantity</span><strong>{quantity} shares</strong></div>
          <div><span>Estimated total</span><strong>{formatCurrency(total)}</strong></div>
        </div>
        <div className="stock-modal-actions"><button className="stock-secondary-button" onClick={onClose} type="button">Go Back</button><button className={`stock-primary-button ${isBuy ? '' : 'stock-primary-button-sell'}`} onClick={onConfirm} type="button">Confirm {isBuy ? 'Buy' : 'Sell'}</button></div>
      </section>
    </div>
  )
}

function TradeTicket({ details, side, quantity, quantityError, onSideChange, onQuantityChange, onSubmit }: { details: StockDetails; side: TradeSide; quantity: string; quantityError: string; onSideChange: (side: TradeSide) => void; onQuantityChange: (quantity: string) => void; onSubmit: (event: FormEvent<HTMLFormElement>) => void }) {
  const parsedQuantity = Number(quantity)
  const estimatedTotal = calculateTradeTotal(details.price, parsedQuantity)
  const isBuy = side === 'BUY'

  return (
    <form className="stock-trade-card" onSubmit={onSubmit}>
      <div className="stock-card-heading">
        <div className="stock-card-title"><span className="stock-card-icon stock-card-icon-trade"><MarketIcon name="wallet" size={16} /></span><h2>Paper Trading</h2></div>
        <span className="stock-practice-pill">Practice Mode</span>
      </div>
      <div className="stock-trade-tabs">
        <button aria-pressed={isBuy} className={isBuy ? 'stock-trade-tab-active stock-trade-tab-buy' : ''} onClick={() => onSideChange('BUY')} type="button">BUY</button>
        <button aria-pressed={!isBuy} className={!isBuy ? 'stock-trade-tab-active stock-trade-tab-sell' : ''} onClick={() => onSideChange('SELL')} type="button">SELL</button>
      </div>
      <div className="stock-trade-field"><label htmlFor="stock-order-type">Order Type</label><select id="stock-order-type" defaultValue="market"><option value="market">Market Order</option><option value="limit">Limit Order</option></select></div>
      <div className="stock-trade-field"><label htmlFor="stock-quantity">Quantity</label><div className="stock-trade-input"><input id="stock-quantity" inputMode="numeric" min="1" onChange={(event) => onQuantityChange(event.target.value)} type="number" value={quantity} /><span>Shares</span></div></div>
      {quantityError && <p className="stock-form-error" role="alert">{quantityError}</p>}
      <div className="stock-trade-summary"><div><span>Est. Price</span><strong>{formatCurrency(details.price)}</strong></div><div><span>Est. Total</span><strong>{formatCurrency(estimatedTotal)}</strong></div></div>
      <button className={`stock-trade-submit ${isBuy ? 'stock-trade-submit-buy' : 'stock-trade-submit-sell'}`} type="submit">{isBuy ? 'Place Buy Order' : 'Place Sell Order'}</button>
      <div className="stock-cash-row"><span>Available Cash (Paper)</span><strong>$12,430.18</strong></div>
    </form>
  )
}

export function StockDetailsPage({ requestedSymbol, stock, onBack }: StockDetailsPageProps) {
  const details = useMemo(() => stock ? getStockDetails(stock) : null, [stock])
  const [isWatchlisted, setIsWatchlisted] = useState(false)
  const [isAlertModalOpen, setIsAlertModalOpen] = useState(false)
  const [tradeSide, setTradeSide] = useState<TradeSide>('BUY')
  const [quantity, setQuantity] = useState('10')
  const [quantityError, setQuantityError] = useState('')
  const [tradeConfirmation, setTradeConfirmation] = useState<{ side: TradeSide; quantity: number; total: number } | null>(null)
  const [activeRange, setActiveRange] = useState<ChartRange>('3M')
  const [activeTab, setActiveTab] = useState<DetailTab>('Overview')
  const [toast, setToast] = useState('')

  const showToast = (message: string) => {
    setToast(message)
    window.setTimeout(() => setToast(''), 2500)
  }

  if (!details) {
    return (
      <MarketShell breadcrumb={<><strong>Stock Details</strong><span>—</span><span>/stocks/{requestedSymbol.toUpperCase()}</span></>} topbarSearch>
        <section aria-labelledby="missing-stock-title" className="stock-details-empty">
          <button className="stock-details-back" onClick={onBack} type="button"><MarketIcon name="arrowLeft" size={16} /> Back to Market</button>
          <h1 id="missing-stock-title">Stock not found</h1>
          <p>We could not find a local preview for “{requestedSymbol}”.</p>
        </section>
      </MarketShell>
    )
  }

  const submitTrade = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const parsedQuantity = Number(quantity)
    if (!Number.isInteger(parsedQuantity) || parsedQuantity <= 0) {
      setQuantityError('Enter a whole number of shares greater than zero.')
      return
    }
    setQuantityError('')
    setTradeConfirmation({ side: tradeSide, quantity: parsedQuantity, total: calculateTradeTotal(details.price, parsedQuantity) })
  }

  return (
    <MarketShell breadcrumb={<><strong>Stock Details</strong><span>—</span><span>/stocks/{details.symbol}</span></>} topbarSearch>
      <section aria-labelledby="stock-details-title" className="stock-details-page">
        <header className="stock-details-hero">
          <div className="stock-details-identity">
            <StockLogo size="large" symbol={details.symbol} />
            <div>
              <div className="stock-title-row"><h1 id="stock-details-title">{details.company}</h1><button aria-label={`${isWatchlisted ? 'Remove' : 'Add'} ${details.symbol} ${isWatchlisted ? 'from' : 'to'} watchlist`} aria-pressed={isWatchlisted} className={`stock-title-star ${isWatchlisted ? 'stock-title-star-active' : ''}`} onClick={() => { setIsWatchlisted((current) => !current); showToast(isWatchlisted ? `${details.symbol} removed from watchlist.` : `${details.symbol} added to watchlist.`) }} type="button"><MarketIcon filled={isWatchlisted} name="star" size={17} /></button></div>
              <p className="stock-details-subtitle">{details.symbol} <span>•</span> {details.exchange}</p>
              <div className="stock-price-row"><strong>{formatCurrency(details.price)}</strong><span className={details.tone === 'positive' ? 'stock-positive' : 'stock-negative'}>{changeLabel(details)}</span></div>
              <p className="stock-details-status">{details.status} <span>•</span> {details.updatedAt}</p>
            </div>
          </div>
          <div className="stock-details-actions">
            <button aria-pressed={isWatchlisted} className={`stock-outline-button ${isWatchlisted ? 'stock-outline-button-active' : ''}`} onClick={() => { setIsWatchlisted((current) => !current); showToast(isWatchlisted ? `${details.symbol} removed from watchlist.` : `${details.symbol} added to watchlist.`) }} type="button"><MarketIcon filled={isWatchlisted} name="star" size={15} /> {isWatchlisted ? 'In Watchlist' : 'Add to Watchlist'}</button>
            <button className="stock-outline-button" onClick={() => setIsAlertModalOpen(true)} type="button"><MarketIcon name="bell" size={15} /> Create Alert</button>
          </div>
        </header>

        <nav aria-label="Stock detail sections" className="stock-detail-tabs">
          {detailTabs.map((tab) => <button aria-pressed={activeTab === tab} className={activeTab === tab ? 'stock-detail-tab-active' : ''} key={tab} onClick={() => { setActiveTab(tab); if (tab !== 'Overview') showToast(`${tab} data is represented in this simulated overview.`) }} type="button">{tab}</button>)}
        </nav>

        <div className="stock-details-layout">
          <div className="stock-details-main-column">
            <article className="stock-chart-card">
              <div className="stock-card-heading stock-chart-heading">
                <div className="stock-card-title"><span className="stock-card-icon stock-card-icon-chart"><MarketIcon name="chart" size={16} /></span><h2>Price Chart</h2></div>
                <div className="stock-chart-tools"><div aria-label="Chart time range" className="stock-range-tabs">{chartRanges.map((range) => <button aria-pressed={activeRange === range} className={activeRange === range ? 'stock-range-active' : ''} key={range} onClick={() => setActiveRange(range)} type="button">{range}</button>)}</div><button aria-label="Expand price chart" className="stock-chart-expand" onClick={() => showToast('Expanded chart view is simulated in this preview.')} type="button"><MarketIcon name="expand" size={13} /></button></div>
              </div>
              <PriceChart details={details} range={activeRange} />
            </article>

            <section aria-label="Key statistics" className="stock-stats-grid">
              <StatCard label="Market Cap" value={details.stats.marketCap} />
              <StatCard label="P/E Ratio (TTM)" value={details.stats.peRatio} />
              <StatCard label="EPS (TTM)" value={details.stats.eps} />
              <StatCard label="Dividend Yield" value={details.stats.dividendYield} />
              <StatCard label="52-Week Range" value={details.stats.weekRange} />
              <StatCard label="Volume" value={details.stats.volume} />
              <StatCard label="Avg. Volume (3M)" value={details.stats.averageVolume} />
              <StatCard label="Next Earnings" value={details.stats.nextEarnings} />
            </section>

            <section aria-label="Trading metrics" className="stock-metrics-card">
              <MetricItem label="Open" value={details.stats.open} />
              <MetricItem label="High" value={details.stats.high} />
              <MetricItem label="Low" value={details.stats.low} />
              <MetricItem label="Prev Close" value={details.stats.previousClose} />
              <MetricItem label="Beta" value={details.stats.beta} />
              <MetricItem label="Analyst Rating" value={details.stats.analystRating} tone="positive" />
              <MetricItem label="Analyst Price Target" value={details.stats.analystPriceTarget} tone="positive" />
            </section>
          </div>

          <aside className="stock-details-side-column">
            <AiInsightCard details={details} />
            <TradeTicket details={details} onQuantityChange={(value) => { setQuantity(value); setQuantityError('') }} onSideChange={setTradeSide} onSubmit={submitTrade} quantity={quantity} quantityError={quantityError} side={tradeSide} />
          </aside>
        </div>
      </section>

      {isAlertModalOpen && <AlertModal details={details} onClose={() => setIsAlertModalOpen(false)} onCreated={() => { setIsAlertModalOpen(false); showToast(`${details.symbol} price alert created.`) }} />}
      {tradeConfirmation && <TradeConfirmationModal details={details} onClose={() => setTradeConfirmation(null)} onConfirm={() => { setTradeConfirmation(null); showToast(`${tradeConfirmation.side} order for ${tradeConfirmation.quantity} ${details.symbol} shares placed.`) }} quantity={tradeConfirmation.quantity} side={tradeConfirmation.side} total={tradeConfirmation.total} />}
      {toast && <div aria-live="polite" className="stock-toast">{toast}</div>}
    </MarketShell>
  )
}
