import type { MarketStock, StockTone } from './marketData'

export type StockDetailHistoryPoint = {
  label: string
  value: number
}

export type StockDetailStats = {
  marketCap: string
  peRatio: string
  eps: string
  dividendYield: string
  weekRange: string
  volume: string
  averageVolume: string
  nextEarnings: string
  open: string
  high: string
  low: string
  previousClose: string
  beta: string
  analystRating: string
  analystPriceTarget: string
}

export type AiTraderRecommendation = 'BUY' | 'SELL' | 'HOLD'

export type AiTraderInsight = {
  recommendation: AiTraderRecommendation
  confidence: number
  summary: string
  keyFactors: string[]
  updatedAt: string
}

export type StockDetails = {
  symbol: string
  company: string
  description: string
  market: MarketStock['market']
  exchange: string
  status: string
  updatedAt: string
  price: number
  changeAmount: number
  changePercent: string
  tone: StockTone
  history: StockDetailHistoryPoint[]
  stats: StockDetailStats
  aiInsight: AiTraderInsight
}

type StockDetailsOverrides = Partial<Omit<StockDetails, 'stats' | 'aiInsight' | 'history'>> & {
  history?: StockDetailHistoryPoint[]
  stats?: Partial<StockDetailStats>
  aiInsight?: Partial<AiTraderInsight>
}

const aaplHistory: StockDetailHistoryPoint[] = [
  { label: 'Feb 24', value: 173.2 },
  { label: 'Feb 29', value: 171.8 },
  { label: 'Mar 10', value: 175.3 },
  { label: 'Mar 17', value: 178.6 },
  { label: 'Mar 24', value: 181.4 },
  { label: 'Mar 31', value: 179.9 },
  { label: 'Apr 7', value: 188.7 },
  { label: 'Apr 14', value: 186.9 },
  { label: 'Apr 21', value: 184.8 },
  { label: 'Apr 28', value: 191.4 },
  { label: 'May 5', value: 194.3 },
  { label: 'May 12', value: 198.2 },
  { label: 'May 19', value: 191.2 },
  { label: 'May 24', value: 191.45 },
]

const detailOverrides: Record<string, StockDetailsOverrides> = {
  AAPL: {
    exchange: 'NASDAQ',
    status: 'Market Closed',
    updatedAt: 'May 24, 2024 4:00 PM ET',
    history: aaplHistory,
    stats: {
      peRatio: '28.75',
      eps: '$6.66',
      dividendYield: '0.52%',
      weekRange: '$164.08 – $199.62',
      volume: '68.7M',
      averageVolume: '72.4M',
      nextEarnings: 'Jul 31, 2024',
      open: '$189.32',
      high: '$192.18',
      low: '$188.73',
      previousClose: '$188.90',
      beta: '1.26',
      analystRating: 'Strong Buy',
      analystPriceTarget: '$208.45',
    },
    aiInsight: {
      recommendation: 'BUY',
      confidence: 82,
      summary: 'Strong momentum, positive earnings outlook, and favorable technical indicators suggest potential upside in the near term.',
      keyFactors: ['Earnings Momentum', 'Technical Strength', 'Analyst Upgrades'],
      updatedAt: 'Updated 2m ago',
    },
  },
}

function parsePrice(price: string) {
  const value = Number.parseFloat(price.replace(/[$,]/g, ''))
  return Number.isFinite(value) ? value : 0
}

function buildFallbackHistory(price: number, tone: StockTone): StockDetailHistoryPoint[] {
  const trend = tone === 'positive' ? 1 : -1
  const factors = [0.94, 0.97, 0.96, 1.01, 0.99, 1.03, 1.01, 1.05, 1.02, 1]
  return factors.map((factor, index) => ({
    label: `Week ${index + 1}`,
    value: Number((price * (factor + trend * index * 0.002)).toFixed(2)),
  }))
}

function defaultStats(stock: MarketStock): StockDetailStats {
  const price = parsePrice(stock.price)
  const low = (price * 0.86).toFixed(2)
  const high = (price * 1.08).toFixed(2)

  return {
    marketCap: stock.marketCap,
    peRatio: '24.18',
    eps: `$${(price / 30).toFixed(2)}`,
    dividendYield: '0.68%',
    weekRange: `$${low} – $${high}`,
    volume: '42.8M',
    averageVolume: '48.2M',
    nextEarnings: 'Aug 1, 2024',
    open: `$${(price * 0.995).toFixed(2)}`,
    high: `$${(price * 1.012).toFixed(2)}`,
    low: `$${(price * 0.984).toFixed(2)}`,
    previousClose: `$${(price * 0.991).toFixed(2)}`,
    beta: '1.14',
    analystRating: stock.tone === 'positive' ? 'Buy' : 'Hold',
    analystPriceTarget: `$${(price * (stock.tone === 'positive' ? 1.12 : 1.04)).toFixed(2)}`,
  }
}

function defaultInsight(stock: MarketStock): AiTraderInsight {
  const isPositive = stock.tone === 'positive'
  return {
    recommendation: isPositive ? 'BUY' : 'HOLD',
    confidence: isPositive ? 78 : 61,
    summary: isPositive
      ? 'Positive price action and supportive market signals point to a constructive near-term setup.'
      : 'Mixed momentum suggests waiting for a clearer signal before increasing exposure.',
    keyFactors: ['Price Momentum', 'Market Sentiment', 'Technical Signals'],
    updatedAt: 'Updated 5m ago',
  }
}

export function getStockDetails(stock: MarketStock): StockDetails {
  const overrides = detailOverrides[stock.symbol]
  const price = parsePrice(stock.price)
  const changeAmount = Number((price * (Number.parseFloat(stock.changePercent) / 100) * (stock.tone === 'positive' ? 1 : -1)).toFixed(2))

  return {
    symbol: stock.symbol,
    company: stock.company,
    description: stock.description,
    market: stock.market,
    exchange: overrides?.exchange ?? (stock.market === 'US Market' ? 'NASDAQ / NYSE' : 'Global Exchange'),
    status: overrides?.status ?? 'Market Open',
    updatedAt: overrides?.updatedAt ?? 'May 24, 2024 4:00 PM ET',
    price,
    changeAmount,
    changePercent: stock.changePercent,
    tone: stock.tone,
    history: overrides?.history ?? buildFallbackHistory(price, stock.tone),
    stats: { ...defaultStats(stock), ...overrides?.stats },
    aiInsight: { ...defaultInsight(stock), ...overrides?.aiInsight },
  }
}

export function calculateTradeTotal(price: number, quantity: number) {
  if (!Number.isFinite(price) || !Number.isFinite(quantity) || price <= 0 || quantity <= 0) return 0
  return Math.round(price * quantity * 100) / 100
}
