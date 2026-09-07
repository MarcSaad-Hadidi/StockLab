import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { MarketShell } from './MarketShell'
import { MarketIcon } from './marketIcons'
import { type MarketStock } from './marketData'
import { StockLogo } from './StockLogo'
import {
  calculateTradeTotal,
  chartRanges,
  getStockDetails,
  getTradeExecutionPrice,
  getVisibleHistory,
  type AiTraderRecommendation,
  type ChartRange,
  type StockDetails,
  type TradeOrderType,
} from './stockDetailsData'

type StockDetailsPageProps = {
  requestedSymbol: string
  stock?: MarketStock
  onBack: () => void
}

type TradeSide = 'BUY' | 'SELL'
type AlertCondition = 'above' | 'below'
type TradeConfirmation = {
  side: TradeSide
  quantity: number
  total: number
  orderType: TradeOrderType
  limitPrice?: number
  executionPrice: number
}

const detailTabs = ['Overview', 'Chart', 'Financials', 'News', 'Key Metrics', 'Forecast', 'AI Insights'] as const
type DetailTab = typeof detailTabs[number]
const detailTabKeys: Record<DetailTab, string> = {
  Overview: 'stockDetails.tabs.overview',
  Chart: 'stockDetails.tabs.chart',
  Financials: 'stockDetails.tabs.financials',
  News: 'stockDetails.tabs.news',
  'Key Metrics': 'stockDetails.tabs.keyMetrics',
  Forecast: 'stockDetails.tabs.forecast',
  'AI Insights': 'stockDetails.tabs.aiInsights',
}

function formatCurrency(value: number) {
  return `$${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function changeLabel(details: StockDetails, todayLabel = 'today') {
  const sign = details.tone === 'positive' ? '+' : '-'
  return `${sign}${formatCurrency(Math.abs(details.changeAmount))} (${details.changePercent}) ${todayLabel}`
}

function PriceChart({ details, range }: { details: StockDetails; range: ChartRange }) {
  const { t } = useTranslation()
  const history = getVisibleHistory(details.history, range)
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
  const markerValue = history.at(-1)?.value ?? details.price
  const labelIndexes = [...new Set([
    0,
    Math.floor((history.length - 1) * 0.25),
    Math.floor((history.length - 1) * 0.5),
    Math.floor((history.length - 1) * 0.75),
    history.length - 1,
  ])]

  return (
    <div className="stock-price-chart" data-range={range}>
      <svg aria-label={t('stockDetails.historicalPriceChart', { symbol: details.symbol, range })} role="img" viewBox={`0 0 ${width} ${height}`}>
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
            <rect className="stock-chart-value-pill" height="22" rx="5" width="68" x={Math.min(width - 74, Math.max(plotLeft, (points.at(-1)?.x ?? width) - 34))} y={Math.max(5, (points.at(-1)?.y ?? 20) - 30)} />
            <text className="stock-chart-value-label" x={Math.min(width - 40, Math.max(plotLeft + 34, (points.at(-1)?.x ?? width)))} y={Math.max(20, (points.at(-1)?.y ?? 20) - 15)}>{formatCurrency(markerValue)}</text>
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
  const { t } = useTranslation()
  const circumference = 2 * Math.PI * 32
  const progress = (confidence / 100) * circumference

  return (
    <div aria-label={t('stockDetails.aiConfidence', { confidence })} className="stock-confidence-ring">
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
  const { t } = useTranslation()
  const insight = details.aiInsight
  const recommendationKey = `stockDetails.recommendations.${insight.recommendation.toLowerCase()}`
  const recommendation = t(recommendationKey, { defaultValue: insight.recommendation })
  const summary = insight.summaryKey ? t(insight.summaryKey) : insight.summary
  const keyFactors = insight.keyFactorKeys?.map((key) => t(key)) ?? insight.keyFactors
  const updatedAt = insight.updatedAtKey ? t(insight.updatedAtKey) : insight.updatedAt

  return (
    <article className="stock-ai-card">
      <div className="stock-card-heading">
        <div className="stock-card-title">
          <span className="stock-card-icon stock-card-icon-ai"><MarketIcon name="robot" size={16} /></span>
          <h2>{t('stockDetails.aiInsight')} <small>{t('stockDetails.byStockLabAi')}</small></h2>
        </div>
        <span className="stock-updated-pill">{updatedAt}</span>
      </div>
      <div className="stock-ai-summary">
        <div>
          <span className={`stock-ai-recommendation ${recommendationClass(insight.recommendation)}`}>{recommendation}</span>
          <strong>{t('stockDetails.highConfidence')}</strong>
        </div>
        <ConfidenceRing confidence={insight.confidence} />
      </div>
      <p className="stock-ai-copy">{summary}</p>
      <div className="stock-key-factors">
        <span>{t('stockDetails.keyFactors')}</span>
        <div>{keyFactors.map((factor) => <span key={factor}>{factor}</span>)}</div>
      </div>
    </article>
  )
}

function AlertModal({ details, onClose, onCreated }: { details: StockDetails; onClose: () => void; onCreated: () => void }) {
  const { t } = useTranslation()
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
      setError(t('stockDetails.errors.targetPrice'))
      return
    }
    onCreated()
  }

  return (
    <div className="stock-modal-backdrop" onClick={onClose} role="presentation">
      <section aria-labelledby="stock-alert-title" aria-modal="true" className="stock-modal" onClick={(event) => event.stopPropagation()} ref={dialogRef} role="dialog" tabIndex={-1}>
        <button aria-label={t('stockDetails.closeCreateAlert')} className="stock-modal-close" onClick={onClose} ref={closeButtonRef} type="button"><MarketIcon name="close" size={17} /></button>
        <span className="stock-modal-icon stock-modal-icon-alert"><MarketIcon name="bell" size={20} /></span>
        <h2 id="stock-alert-title">{t('alerts.createAlert')}</h2>
        <p>{t('stockDetails.setPriceTrigger', { symbol: details.symbol })}</p>
        <form onSubmit={submitAlert}>
          <label htmlFor="stock-alert-symbol">{t('common.asset')}</label>
          <input disabled id="stock-alert-symbol" value={`${details.symbol} — ${details.company}`} />
          <fieldset>
            <legend>{t('common.condition')}</legend>
            <div className="stock-choice-row">
              <button aria-pressed={condition === 'above'} className={condition === 'above' ? 'stock-choice-active stock-choice-positive' : ''} onClick={() => setCondition('above')} type="button">{t('common.above')}</button>
              <button aria-pressed={condition === 'below'} className={condition === 'below' ? 'stock-choice-active stock-choice-negative' : ''} onClick={() => setCondition('below')} type="button">{t('common.below')}</button>
            </div>
          </fieldset>
          <label htmlFor="stock-alert-target">{t('common.targetPrice')}</label>
          <div className="stock-input-with-prefix"><span>$</span><input id="stock-alert-target" inputMode="decimal" min="0.01" onChange={(event) => { setTargetPrice(event.target.value); setError('') }} step="0.01" type="number" value={targetPrice} /></div>
          {error && <p className="stock-form-error" role="alert">{error}</p>}
          <div className="stock-modal-actions"><button className="stock-secondary-button" onClick={onClose} type="button">{t('common.cancel')}</button><button className="stock-primary-button" type="submit">{t('alerts.createAlert')}</button></div>
        </form>
      </section>
    </div>
  )
}

function TradeConfirmationModal({ details, side, quantity, total, orderType, limitPrice, executionPrice, onClose, onConfirm }: { details: StockDetails; side: TradeSide; quantity: number; total: number; orderType: TradeOrderType; limitPrice?: number; executionPrice: number; onClose: () => void; onConfirm: () => void }) {
  const { t } = useTranslation()
  const isBuy = side === 'BUY'
  const isLimitOrder = orderType === 'limit'
  const dialogRef = useRef<HTMLElement | null>(null)
  const closeButtonRef = useRef<HTMLButtonElement | null>(null)

  useDialogAccessibility(onClose, dialogRef, closeButtonRef)

  return (
    <div className="stock-modal-backdrop" onClick={onClose} role="presentation">
      <section aria-labelledby="trade-confirmation-title" aria-modal="true" className="stock-modal stock-trade-confirmation" onClick={(event) => event.stopPropagation()} ref={dialogRef} role="dialog" tabIndex={-1}>
        <button aria-label={t('stockDetails.closeTradeConfirmation')} className="stock-modal-close" onClick={onClose} ref={closeButtonRef} type="button"><MarketIcon name="close" size={17} /></button>
        <span className={`stock-modal-icon ${isBuy ? 'stock-modal-icon-buy' : 'stock-modal-icon-sell'}`}><MarketIcon name={isBuy ? 'check' : 'chart'} size={20} /></span>
        <h2 id="trade-confirmation-title">{t('stockDetails.confirmOrder', { side: isBuy ? t('common.buy') : t('common.sell') })}</h2>
        <p>{t('stockDetails.reviewOrder')}</p>
        <div className="stock-confirmation-list">
          <div><span>{t('common.asset')}</span><strong>{details.symbol} · {details.company}</strong></div>
          <div><span>{t('stockDetails.orderType')}</span><strong>{isLimitOrder ? t('stockDetails.limitOrder') : t('stockDetails.marketOrder')}</strong></div>
          {isLimitOrder && <div><span>{t('stockDetails.limitPrice')}</span><strong>{formatCurrency(limitPrice ?? executionPrice)}</strong></div>}
          <div><span>{t('common.quantity')}</span><strong>{t('stockDetails.shares', { count: quantity })}</strong></div>
          <div><span>{t('stockDetails.estimatedPrice')}</span><strong>{formatCurrency(executionPrice)}</strong></div>
          <div><span>{t('stockDetails.estimatedTotal')}</span><strong>{formatCurrency(total)}</strong></div>
        </div>
        <div className="stock-modal-actions"><button className="stock-secondary-button" onClick={onClose} type="button">{t('common.goBack')}</button><button className={`stock-primary-button ${isBuy ? '' : 'stock-primary-button-sell'}`} onClick={onConfirm} type="button">{t('stockDetails.confirmOrder', { side: isBuy ? t('common.buy') : t('common.sell') })}</button></div>
      </section>
    </div>
  )
}

function TradeTicket({ details, side, quantity, quantityError, orderType, limitPrice, limitPriceError, onSideChange, onQuantityChange, onOrderTypeChange, onLimitPriceChange, onSubmit }: { details: StockDetails; side: TradeSide; quantity: string; quantityError: string; orderType: TradeOrderType; limitPrice: string; limitPriceError: string; onSideChange: (side: TradeSide) => void; onQuantityChange: (quantity: string) => void; onOrderTypeChange: (orderType: TradeOrderType) => void; onLimitPriceChange: (limitPrice: string) => void; onSubmit: (event: FormEvent<HTMLFormElement>) => void }) {
  const { t } = useTranslation()
  const parsedQuantity = Number(quantity)
  const estimatedPrice = getTradeExecutionPrice(orderType, details.price, Number(limitPrice))
  const estimatedTotal = calculateTradeTotal(estimatedPrice, parsedQuantity)
  const isBuy = side === 'BUY'

  return (
    <form className="stock-trade-card" onSubmit={onSubmit}>
      <div className="stock-card-heading">
        <div className="stock-card-title"><span className="stock-card-icon stock-card-icon-trade"><MarketIcon name="wallet" size={16} /></span><h2>{t('stockDetails.paperTrading')}</h2></div>
        <span className="stock-practice-pill">{t('stockDetails.practiceMode')}</span>
      </div>
      <div className="stock-trade-tabs">
        <button aria-pressed={isBuy} className={isBuy ? 'stock-trade-tab-active stock-trade-tab-buy' : ''} onClick={() => onSideChange('BUY')} type="button">{t('common.buy')}</button>
        <button aria-pressed={!isBuy} className={!isBuy ? 'stock-trade-tab-active stock-trade-tab-sell' : ''} onClick={() => onSideChange('SELL')} type="button">{t('common.sell')}</button>
      </div>
      <div className="stock-trade-field"><label htmlFor="stock-order-type">{t('stockDetails.orderType')}</label><select id="stock-order-type" onChange={(event) => onOrderTypeChange(event.target.value as TradeOrderType)} value={orderType}><option value="market">{t('stockDetails.marketOrder')}</option><option value="limit">{t('stockDetails.limitOrder')}</option></select></div>
      {orderType === 'limit' && <div className="stock-trade-field"><label htmlFor="stock-limit-price">{t('stockDetails.limitPrice')}</label><div className="stock-trade-input"><input aria-describedby={limitPriceError ? 'stock-limit-price-error' : undefined} id="stock-limit-price" inputMode="decimal" min="0.01" onChange={(event) => onLimitPriceChange(event.target.value)} step="0.01" type="number" value={limitPrice} /><span>USD</span></div></div>}
      <div className="stock-trade-field"><label htmlFor="stock-quantity">{t('common.quantity')}</label><div className="stock-trade-input"><input id="stock-quantity" inputMode="numeric" min="1" onChange={(event) => onQuantityChange(event.target.value)} type="number" value={quantity} /><span>{t('stockDetails.sharesLabel')}</span></div></div>
      {quantityError && <p className="stock-form-error" role="alert">{quantityError}</p>}
      {limitPriceError && <p className="stock-form-error" id="stock-limit-price-error" role="alert">{limitPriceError}</p>}
      <div className="stock-trade-summary"><div><span>{t('stockDetails.estimatedPriceShort')}</span><strong>{estimatedPrice > 0 ? formatCurrency(estimatedPrice) : '—'}</strong></div><div><span>{t('stockDetails.estimatedTotalShort')}</span><strong>{estimatedPrice > 0 ? formatCurrency(estimatedTotal) : '—'}</strong></div></div>
      <button className={`stock-trade-submit ${isBuy ? 'stock-trade-submit-buy' : 'stock-trade-submit-sell'}`} type="submit">{t(isBuy ? 'stockDetails.placeBuyOrder' : 'stockDetails.placeSellOrder')}</button>
      <div className="stock-cash-row"><span>{t('stockDetails.availableCashPaper')}</span><strong>$12,430.18</strong></div>
    </form>
  )
}

export function StockDetailsPage({ requestedSymbol, stock, onBack }: StockDetailsPageProps) {
  const { t } = useTranslation()
  const details = useMemo(() => stock ? getStockDetails(stock) : null, [stock])
  const analystRatingKeys: Record<string, string> = {
    'Strong Buy': 'stockDetails.ratings.strongBuy',
    Buy: 'stockDetails.ratings.buy',
    Hold: 'stockDetails.ratings.hold',
  }
  const [isWatchlisted, setIsWatchlisted] = useState(false)
  const [isAlertModalOpen, setIsAlertModalOpen] = useState(false)
  const [tradeSide, setTradeSide] = useState<TradeSide>('BUY')
  const [orderType, setOrderType] = useState<TradeOrderType>('market')
  const [limitPrice, setLimitPrice] = useState('')
  const [quantity, setQuantity] = useState('10')
  const [quantityError, setQuantityError] = useState('')
  const [limitPriceError, setLimitPriceError] = useState('')
  const [tradeConfirmation, setTradeConfirmation] = useState<TradeConfirmation | null>(null)
  const [activeRange, setActiveRange] = useState<ChartRange>('3M')
  const [activeTab, setActiveTab] = useState<DetailTab>('Overview')
  const [toast, setToast] = useState('')

  const showToast = (message: string) => {
    setToast(message)
    window.setTimeout(() => setToast(''), 2500)
  }

  if (!details) {
    return (
      <MarketShell breadcrumb={<strong>{t('stockDetails.title')}</strong>} topbarSearch>
        <section aria-labelledby="missing-stock-title" className="stock-details-empty">
          <button className="stock-details-back" onClick={onBack} type="button"><MarketIcon name="arrowLeft" size={16} /> {t('stockDetails.backToMarket')}</button>
          <h1 id="missing-stock-title">{t('stockDetails.notFound')}</h1>
          <p>{t('stockDetails.notFoundHint', { symbol: requestedSymbol })}</p>
        </section>
      </MarketShell>
    )
  }

  const submitTrade = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const parsedQuantity = Number(quantity)
    if (!Number.isInteger(parsedQuantity) || parsedQuantity <= 0) {
      setQuantityError(t('stockDetails.errors.quantity'))
      return
    }
    setQuantityError('')
    const parsedLimitPrice = Number(limitPrice)
    if (orderType === 'limit' && (!Number.isFinite(parsedLimitPrice) || parsedLimitPrice <= 0)) {
      setLimitPriceError(t('stockDetails.errors.limitPrice'))
      return
    }
    setLimitPriceError('')
    const executionPrice = getTradeExecutionPrice(orderType, details.price, parsedLimitPrice)
    setTradeConfirmation({
      side: tradeSide,
      quantity: parsedQuantity,
      total: calculateTradeTotal(executionPrice, parsedQuantity),
      orderType,
      limitPrice: orderType === 'limit' ? parsedLimitPrice : undefined,
      executionPrice,
    })
  }

  return (
    <MarketShell breadcrumb={<strong>{t('stockDetails.title')}</strong>} topbarSearch>
      <section aria-labelledby="stock-details-title" className="stock-details-page">
        <header className="stock-details-hero">
          <div className="stock-details-identity">
            <StockLogo size="large" symbol={details.symbol} />
            <div>
              <div className="stock-title-row"><h1 id="stock-details-title">{details.company}</h1><button aria-label={isWatchlisted ? t('stockDetails.removeFromWatchlist', { symbol: details.symbol }) : t('stockDetails.addToWatchlist', { symbol: details.symbol })} aria-pressed={isWatchlisted} className={`stock-title-star ${isWatchlisted ? 'stock-title-star-active' : ''}`} onClick={() => { setIsWatchlisted((current) => !current); showToast(isWatchlisted ? t('stockDetails.removedFromWatchlist', { symbol: details.symbol }) : t('stockDetails.addedToWatchlist', { symbol: details.symbol })) }} type="button"><MarketIcon filled={isWatchlisted} name="star" size={17} /></button></div>
              <p className="stock-details-subtitle">{details.symbol} <span>•</span> {details.exchange}</p>
              <div className="stock-price-row"><strong>{formatCurrency(details.price)}</strong><span className={details.tone === 'positive' ? 'stock-positive' : 'stock-negative'}>{changeLabel(details, t('stockDetails.today'))}</span></div>
              <p className="stock-details-status">{details.statusKey ? t(details.statusKey) : details.status} <span>•</span> {details.updatedAtKey ? t(details.updatedAtKey) : details.updatedAt}</p>
            </div>
          </div>
          <div className="stock-details-actions">
            <button aria-pressed={isWatchlisted} className={`stock-outline-button ${isWatchlisted ? 'stock-outline-button-active' : ''}`} onClick={() => { setIsWatchlisted((current) => !current); showToast(isWatchlisted ? t('stockDetails.removedFromWatchlist', { symbol: details.symbol }) : t('stockDetails.addedToWatchlist', { symbol: details.symbol })) }} type="button"><MarketIcon filled={isWatchlisted} name="star" size={15} /> {isWatchlisted ? t('stockDetails.inWatchlist') : t('stockDetails.addToWatchlist', { symbol: details.symbol })}</button>
            <button className="stock-outline-button" onClick={() => setIsAlertModalOpen(true)} type="button"><MarketIcon name="bell" size={15} /> {t('alerts.createAlert')}</button>
          </div>
        </header>

        <nav aria-label={t('stockDetails.sectionsLabel')} className="stock-detail-tabs">
          {detailTabs.map((tab) => <button aria-pressed={activeTab === tab} className={activeTab === tab ? 'stock-detail-tab-active' : ''} key={tab} onClick={() => { setActiveTab(tab); if (tab !== 'Overview') showToast(t('stockDetails.tabSimulated', { tab: t(detailTabKeys[tab]) })) }} type="button">{t(detailTabKeys[tab])}</button>)}
        </nav>

        <div className="stock-details-layout">
          <div className="stock-details-main-column">
            <article className="stock-chart-card">
              <div className="stock-card-heading stock-chart-heading">
                <div className="stock-card-title"><span className="stock-card-icon stock-card-icon-chart"><MarketIcon name="chart" size={16} /></span><h2>{t('stockDetails.priceChart')}</h2></div>
                <div className="stock-chart-tools"><div aria-label={t('stockDetails.chartTimeRange')} className="stock-range-tabs">{chartRanges.map((range) => <button aria-pressed={activeRange === range} className={activeRange === range ? 'stock-range-active' : ''} key={range} onClick={() => setActiveRange(range)} type="button">{range}</button>)}</div><button aria-label={t('stockDetails.expandChart')} className="stock-chart-expand" onClick={() => showToast(t('stockDetails.expandedChart'))} type="button"><MarketIcon name="expand" size={13} /></button></div>
              </div>
              <PriceChart details={details} range={activeRange} />
            </article>

            <section aria-label={t('stockDetails.keyStatistics')} className="stock-stats-grid">
              <StatCard label={t('stockDetails.stats.marketCap')} value={details.stats.marketCap} />
              <StatCard label={t('stockDetails.stats.peRatio')} value={details.stats.peRatio} />
              <StatCard label={t('stockDetails.stats.eps')} value={details.stats.eps} />
              <StatCard label={t('stockDetails.stats.dividendYield')} value={details.stats.dividendYield} />
              <StatCard label={t('stockDetails.stats.weekRange')} value={details.stats.weekRange} />
              <StatCard label={t('stockDetails.stats.volume')} value={details.stats.volume} />
              <StatCard label={t('stockDetails.stats.averageVolume')} value={details.stats.averageVolume} />
              <StatCard label={t('stockDetails.stats.nextEarnings')} value={details.stats.nextEarnings} />
            </section>

            <section aria-label={t('stockDetails.tradingMetrics')} className="stock-metrics-card">
              <MetricItem label={t('stockDetails.metrics.open')} value={details.stats.open} />
              <MetricItem label={t('stockDetails.metrics.high')} value={details.stats.high} />
              <MetricItem label={t('stockDetails.metrics.low')} value={details.stats.low} />
              <MetricItem label={t('stockDetails.metrics.previousClose')} value={details.stats.previousClose} />
              <MetricItem label={t('stockDetails.metrics.beta')} value={details.stats.beta} />
              <MetricItem label={t('stockDetails.metrics.analystRating')} value={analystRatingKeys[details.stats.analystRating] ? t(analystRatingKeys[details.stats.analystRating]) : details.stats.analystRating} tone="positive" />
              <MetricItem label={t('stockDetails.metrics.analystTarget')} value={details.stats.analystPriceTarget} tone="positive" />
            </section>
          </div>

          <aside className="stock-details-side-column">
            <AiInsightCard details={details} />
            <TradeTicket
              details={details}
              limitPrice={limitPrice}
              limitPriceError={limitPriceError}
              onLimitPriceChange={(value) => { setLimitPrice(value); setLimitPriceError('') }}
              onOrderTypeChange={(value) => { setOrderType(value); setLimitPriceError('') }}
              onQuantityChange={(value) => { setQuantity(value); setQuantityError('') }}
              onSideChange={setTradeSide}
              onSubmit={submitTrade}
              orderType={orderType}
              quantity={quantity}
              quantityError={quantityError}
              side={tradeSide}
            />
          </aside>
        </div>
      </section>

      {isAlertModalOpen && <AlertModal details={details} onClose={() => setIsAlertModalOpen(false)} onCreated={() => { setIsAlertModalOpen(false); showToast(t('stockDetails.alertCreated', { symbol: details.symbol })) }} />}
      {tradeConfirmation && <TradeConfirmationModal details={details} executionPrice={tradeConfirmation.executionPrice} limitPrice={tradeConfirmation.limitPrice} onClose={() => setTradeConfirmation(null)} onConfirm={() => { setTradeConfirmation(null); showToast(t('stockDetails.orderPlaced', { side: tradeConfirmation.side === 'BUY' ? t('common.buy') : t('common.sell'), orderType: tradeConfirmation.orderType === 'limit' ? t('stockDetails.limitOrder') : t('stockDetails.marketOrder'), quantity: tradeConfirmation.quantity, symbol: details.symbol })) }} orderType={tradeConfirmation.orderType} quantity={tradeConfirmation.quantity} side={tradeConfirmation.side} total={tradeConfirmation.total} />}
      {toast && <div aria-live="polite" className="stock-toast">{toast}</div>}
    </MarketShell>
  )
}
