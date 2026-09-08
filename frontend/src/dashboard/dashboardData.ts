export type IconName =
  | 'activity'
  | 'bell'
  | 'briefcase'
  | 'chart'
  | 'chevron-down'
  | 'chevron-right'
  | 'clock'
  | 'close'
  | 'grid'
  | 'menu'
  | 'more'
  | 'pie-chart'
  | 'search'
  | 'settings'
  | 'sparkles'
  | 'star'
  | 'trending-up'
  | 'wallet'
  | 'x'

export type Metric = {
  label: string
  value: number | null
  change: number | null
  detail: string
  icon: IconName
  tone: 'blue' | 'green' | 'purple' | 'orange'
}

export type PerformanceRange = '1D' | '1W' | '1M' | '3M' | '1Y' | 'YTD' | 'ALL'

export type PerformanceSeries = {
  labels: string[]
  values: number[]
  change: number | null
  changeLabel: string
}

export type Position = {
  symbol: string
  company: string
  shares: number
  value: number | null
  allocation: number
  price: number
  change: number | null
  tone: 'positive' | 'negative'
}

export type WatchlistItem = {
  symbol: string
  company: string
  price: number
  change: number | null
  tone: 'positive' | 'negative'
  starred?: boolean
}

export type Transaction = {
  symbol: string
  company: string
  type: 'Buy' | 'Sell'
  shares: number
  amount: number
  time: string
  timeKey: string
}

export const metrics: Metric[] = [
  {
    label: 'dashboard.metrics.totalPortfolioValue',
    value: null,
    change: null,
    detail: 'dashboard.metrics.vsLastMonth',
    icon: 'wallet',
    tone: 'blue',
  },
  {
    label: 'dashboard.metrics.cashAvailable',
    value: null,
    change: null,
    detail: 'dashboard.metrics.vsLastMonth',
    icon: 'briefcase',
    tone: 'green',
  },
  {
    label: 'dashboard.metrics.totalPnl',
    value: null,
    change: null,
    detail: 'dashboard.metrics.allTime',
    icon: 'trending-up',
    tone: 'purple',
  },
  {
    label: 'dashboard.metrics.return',
    value: null,
    change: null,
    detail: 'dashboard.metrics.vsSp500',
    icon: 'activity',
    tone: 'orange',
  },
]

export const performanceSeries: Record<PerformanceRange, PerformanceSeries> = Object.fromEntries(['1D', '1W', '1M', '3M', '1Y', 'YTD', 'ALL'].map(range => [range, { labels: [], values: [], change: null, changeLabel: '' }])) as unknown as Record<PerformanceRange, PerformanceSeries>

export const positions: Position[] = []

export const watchlist: WatchlistItem[] = []

export const transactions: Transaction[] = []

export const aiPerformance = { return: null, pnl: null, winRate: null, trades: 0 }
