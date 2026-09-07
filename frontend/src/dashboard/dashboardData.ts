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
  value: number
  change: number
  detail: string
  icon: IconName
  tone: 'blue' | 'green' | 'purple' | 'orange'
}

export type PerformanceRange = '1D' | '1W' | '1M' | '3M' | '1Y' | 'YTD' | 'ALL'

export type PerformanceSeries = {
  labels: string[]
  values: number[]
  change: number
  changeLabel: string
}

export type Position = {
  symbol: string
  company: string
  shares: number
  value: number
  allocation: number
  price: number
  change: number
  tone: 'positive' | 'negative'
}

export type WatchlistItem = {
  symbol: string
  company: string
  price: number
  change: number
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
    value: 48294.20,
    change: 12.48,
    detail: 'dashboard.metrics.vsLastMonth',
    icon: 'wallet',
    tone: 'blue',
  },
  {
    label: 'dashboard.metrics.cashAvailable',
    value: 8420.75,
    change: 4.20,
    detail: 'dashboard.metrics.vsLastMonth',
    icon: 'briefcase',
    tone: 'green',
  },
  {
    label: 'dashboard.metrics.totalPnl',
    value: 5294.20,
    change: 12.48,
    detail: 'dashboard.metrics.allTime',
    icon: 'trending-up',
    tone: 'purple',
  },
  {
    label: 'dashboard.metrics.return',
    value: 12.48,
    change: 2.14,
    detail: 'dashboard.metrics.vsSp500',
    icon: 'activity',
    tone: 'orange',
  },
]

export const performanceSeries: Record<PerformanceRange, PerformanceSeries> = {
  '1D': {
    labels: ['9:30', '10:30', '11:30', '12:30', '1:30', '2:30', '3:30', '4:00'],
    values: [47.64, 47.72, 47.69, 47.84, 47.91, 47.87, 48.12, 48.29],
    change: 642.18,
    changeLabel: '+1.35% today',
  },
  '1W': {
    labels: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'],
    values: [46.87, 47.18, 46.94, 47.63, 47.41, 48.02, 48.29],
    change: 1424.90,
    changeLabel: '+3.04% this week',
  },
  '1M': {
    labels: ['May 1', 'May 6', 'May 11', 'May 16', 'May 21', 'May 26', 'May 31'],
    values: [43.02, 44.16, 43.88, 45.52, 46.29, 47.48, 48.29],
    change: 3122.64,
    changeLabel: '+6.91% this month',
  },
  '3M': {
    labels: ['Mar 1', 'Mar 15', 'Apr 1', 'Apr 15', 'May 1', 'May 15', 'May 31'],
    values: [39.92, 41.38, 40.84, 43.12, 43.02, 46.86, 48.29],
    change: 6840.42,
    changeLabel: '+16.51% in 3 months',
  },
  '1Y': {
    labels: ['Jun 25', 'Aug 25', 'Oct 25', 'Dec 25', 'Feb 26', 'Apr 26', 'Jun 26'],
    values: [34.48, 36.71, 37.42, 40.22, 41.18, 45.61, 48.29],
    change: 13812.76,
    changeLabel: '+40.04% this year',
  },
  YTD: {
    labels: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul'],
    values: [39.31, 40.58, 39.92, 43.12, 43.02, 46.21, 48.29],
    change: 8980.20,
    changeLabel: '+22.83% YTD',
  },
  ALL: {
    labels: ['2022', '2023', '2024', 'Q1 25', 'Q2 25', 'Q1 26', 'Today'],
    values: [25.42, 30.18, 35.83, 39.31, 43.02, 46.21, 48.29],
    change: 22871.42,
    changeLabel: '+90.00% all time',
  },
}

export const positions: Position[] = [
  {
    symbol: 'AAPL',
    company: 'Apple Inc.',
    shares: 42,
    value: 8812.08,
    allocation: 18.2,
    price: 209.81,
    change: 1.42,
    tone: 'positive',
  },
  {
    symbol: 'MSFT',
    company: 'Microsoft Corp.',
    shares: 18,
    value: 7671.24,
    allocation: 15.9,
    price: 425.07,
    change: 0.84,
    tone: 'positive',
  },
  {
    symbol: 'NVDA',
    company: 'NVIDIA Corporation',
    shares: 36,
    value: 5908.68,
    allocation: 12.2,
    price: 164.13,
    change: 2.61,
    tone: 'positive',
  },
  {
    symbol: 'AMZN',
    company: 'Amazon.com Inc.',
    shares: 24,
    value: 4359.12,
    allocation: 9.0,
    price: 181.63,
    change: -0.38,
    tone: 'negative',
  },
]

export const watchlist: WatchlistItem[] = [
  { symbol: 'TSLA', company: 'Tesla Inc.', price: 338.74, change: 4.82, tone: 'positive', starred: true },
  { symbol: 'GOOGL', company: 'Alphabet Inc.', price: 188.29, change: 1.16, tone: 'positive', starred: true },
  { symbol: 'META', company: 'Meta Platforms', price: 574.62, change: -0.62, tone: 'negative', starred: true },
  { symbol: 'NFLX', company: 'Netflix Inc.', price: 1126.40, change: 2.08, tone: 'positive', starred: true },
  { symbol: 'AMD', company: 'Advanced Micro Devices', price: 121.84, change: -1.24, tone: 'negative', starred: true },
]

export const transactions: Transaction[] = [
  { symbol: 'AAPL', company: 'Apple Inc.', type: 'Buy', shares: 10, amount: 2098.10, time: '10:42', timeKey: 'dashboard.times.todayAt' },
  { symbol: 'TSLA', company: 'Tesla Inc.', type: 'Sell', shares: 5, amount: 1693.70, time: '15:18', timeKey: 'dashboard.times.yesterdayAt' },
  { symbol: 'NVDA', company: 'NVIDIA Corporation', type: 'Buy', shares: 12, amount: 1969.56, time: '', timeKey: 'dashboard.times.may28' },
]

export const aiPerformance = {
  return: 18.72,
  pnl: 2418.62,
  winRate: 76.4,
  trades: 42,
  labels: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'],
  values: [100, 103, 102, 108, 110, 114, 118.72],
}
