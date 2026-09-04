export type AlertStatus = 'active' | 'triggered' | 'disabled'
export type AlertCondition = 'above' | 'below'

export type PriceAlert = {
  id: string
  symbol: string
  name: string
  exchange: string
  condition: AlertCondition
  targetPrice: number
  lastPrice: number
  status: AlertStatus
  createdAt: string
}

export const assetOptions = [
  { symbol: 'AAPL', name: 'Apple Inc.', exchange: 'NASDAQ', lastPrice: 191.45 },
  { symbol: 'NVDA', name: 'NVIDIA Corp.', exchange: 'NASDAQ', lastPrice: 892.72 },
  { symbol: 'TSLA', name: 'Tesla Inc.', exchange: 'NASDAQ', lastPrice: 178.22 },
  { symbol: 'MSFT', name: 'Microsoft Corp.', exchange: 'NASDAQ', lastPrice: 415.6 },
  { symbol: 'AMZN', name: 'Amazon.com Inc.', exchange: 'NASDAQ', lastPrice: 186.21 },
  { symbol: 'META', name: 'Meta Platforms, Inc.', exchange: 'NASDAQ', lastPrice: 485.09 },
  { symbol: 'GOOGL', name: 'Alphabet Inc.', exchange: 'NASDAQ', lastPrice: 167.34 },
  { symbol: 'BTC', name: 'Bitcoin', exchange: 'CRYPTO', lastPrice: 64231.5 },
] as const

export const initialAlerts: PriceAlert[] = [
  { id: 'alert-aapl-1', symbol: 'AAPL', name: 'Apple Inc.', exchange: 'NASDAQ', condition: 'above', targetPrice: 200, lastPrice: 191.45, status: 'active', createdAt: 'May 24, 2024' },
  { id: 'alert-nvda-1', symbol: 'NVDA', name: 'NVIDIA Corp.', exchange: 'NASDAQ', condition: 'below', targetPrice: 850, lastPrice: 892.72, status: 'active', createdAt: 'May 24, 2024' },
  { id: 'alert-tsla-1', symbol: 'TSLA', name: 'Tesla Inc.', exchange: 'NASDAQ', condition: 'above', targetPrice: 200, lastPrice: 178.22, status: 'active', createdAt: 'May 23, 2024' },
  { id: 'alert-msft-1', symbol: 'MSFT', name: 'Microsoft Corp.', exchange: 'NASDAQ', condition: 'below', targetPrice: 400, lastPrice: 415.6, status: 'active', createdAt: 'May 22, 2024' },
  { id: 'alert-amzn-1', symbol: 'AMZN', name: 'Amazon.com Inc.', exchange: 'NASDAQ', condition: 'above', targetPrice: 190, lastPrice: 186.21, status: 'active', createdAt: 'May 22, 2024' },
  { id: 'alert-meta-1', symbol: 'META', name: 'Meta Platforms, Inc.', exchange: 'NASDAQ', condition: 'below', targetPrice: 450, lastPrice: 485.09, status: 'active', createdAt: 'May 21, 2024' },
  { id: 'alert-googl-1', symbol: 'GOOGL', name: 'Alphabet Inc.', exchange: 'NASDAQ', condition: 'above', targetPrice: 180, lastPrice: 167.34, status: 'triggered', createdAt: 'May 21, 2024' },
  { id: 'alert-btc-1', symbol: 'BTC', name: 'Bitcoin', exchange: 'CRYPTO', condition: 'above', targetPrice: 70000, lastPrice: 64231.5, status: 'disabled', createdAt: 'May 20, 2024' },
]
