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

export const watchlistItems: WatchlistItem[] = []
