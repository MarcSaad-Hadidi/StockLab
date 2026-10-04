export type WatchlistItem = {
  symbol: string
  createdAtUtc: string
  name: string
  exchange: string
  currency: string | null
  price: number | null
  change: number | null
  changePercent: number | null
  tone: 'positive' | 'negative' | ''
}
