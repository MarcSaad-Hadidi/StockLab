export type WatchlistItem = {
  symbol: string
  name: string
  exchange: string
  price: number
  change: number
  changePercent: number
  tone: 'positive' | 'negative'
  markTone: string
}

export const watchlistItems: WatchlistItem[] = [
  { symbol: 'NVDA', name: 'NVIDIA Corporation', exchange: 'NASDAQ', price: 892.72, change: 21.46, changePercent: 2.46, tone: 'positive', markTone: 'nvidia' },
  { symbol: 'AAPL', name: 'Apple Inc.', exchange: 'NASDAQ', price: 191.45, change: 1.82, changePercent: 0.96, tone: 'positive', markTone: 'apple' },
  { symbol: 'TSLA', name: 'Tesla, Inc.', exchange: 'NASDAQ', price: 154.32, change: -4.92, changePercent: -3.09, tone: 'negative', markTone: 'tesla' },
  { symbol: 'MSFT', name: 'Microsoft Corporation', exchange: 'NASDAQ', price: 415.6, change: 3.24, changePercent: 0.79, tone: 'positive', markTone: 'microsoft' },
  { symbol: 'AMZN', name: 'Amazon.com, Inc.', exchange: 'NASDAQ', price: 186.21, change: 2.14, changePercent: 1.16, tone: 'positive', markTone: 'amazon' },
  { symbol: 'GOOGL', name: 'Alphabet Inc.', exchange: 'NASDAQ', price: 167.34, change: -0.88, changePercent: -0.52, tone: 'negative', markTone: 'google' },
  { symbol: 'META', name: 'Meta Platforms, Inc.', exchange: 'NASDAQ', price: 574.62, change: 6.28, changePercent: 1.11, tone: 'positive', markTone: 'meta' },
  { symbol: 'AMD', name: 'Advanced Micro Devices', exchange: 'NASDAQ', price: 121.84, change: -1.53, changePercent: -1.24, tone: 'negative', markTone: 'amd' },
]
