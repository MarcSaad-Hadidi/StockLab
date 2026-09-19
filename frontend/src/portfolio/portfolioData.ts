export type Position = {
  symbol: string
  name: string
  quantity: number | null
  averagePrice: number | null
  currentPrice: number
  marketValue: number
  pnl: number | null
  pnlPercent: number | null
  weight: number
  tone: string
}

export const positions: Position[] = []

export const performanceSeries = Object.fromEntries(['1D', '1W', '1M', '3M', '1Y', 'YTD', 'ALL'].map(range => [range, { labels: [] as string[], values: [] as number[], change: null, changePercent: null }]))
