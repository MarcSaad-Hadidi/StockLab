export type TraderAction = 'BUY' | 'SELL' | 'HOLD'

export type TraderPosition = {
  symbol: string
  company: string
  size: number
  entryPrice: number
  currentPrice: number
  pnl: number
  pnlPercent: number
}

export type TraderDecision = {
  symbol: string
  company: string
  action: TraderAction
  confidence: number
  targetPrice: number
  stopLoss: number
}

export type RejectedDecision = {
  symbol: string
  action: TraderAction
  reason: string
  confidence: number
  time: string
}

export type TraderTrade = {
  symbol: string
  side: 'BUY' | 'SELL'
  type: 'Entry' | 'Exit'
  price: number
  pnl: number
  pnlPercent: number
  time: string
}

export type ModelVersion = {
  version: string
  accuracy: string
  precision: string
  recall: string
  f1Score: string
  trainedOn: string
  status: 'Current' | 'Archived'
}

export const traderSummary = {
  initialCapital: 100000,
  currentValue: 112846.37,
  profitLoss: 12846.37,
  returnPercent: 12.85,
  winRate: 68.7,
  maxDrawdown: -8.21,
}

export const performanceSeries = [100000, 100820, 100460, 102100, 101760, 103480, 104220, 103890, 106340, 107120, 106780, 109420, 108860, 110540, 112846]
export const performanceLabels = ['May 21', 'May 22', 'May 23', 'May 24', 'May 25', 'May 26', 'May 27']

export const positions: TraderPosition[] = [
  { symbol: 'AAPL', company: 'Apple Inc.', size: 120, entryPrice: 175.22, currentPrice: 191.45, pnl: 1947.6, pnlPercent: 9.26 },
  { symbol: 'NVDA', company: 'NVIDIA Corp.', size: 53, entryPrice: 437.84, currentPrice: 892.72, pnl: 24094.64, pnlPercent: 103.28 },
  { symbol: 'TSLA', company: 'Tesla Inc.', size: 35, entryPrice: 225.1, currentPrice: 154.32, pnl: -2477.3, pnlPercent: -31.43 },
  { symbol: 'MSFT', company: 'Microsoft Corp.', size: 85, entryPrice: 378.05, currentPrice: 415.6, pnl: 3323.75, pnlPercent: 9.06 },
  { symbol: 'GOOGL', company: 'Alphabet Inc.', size: 8, entryPrice: 156.83, currentPrice: 167.34, pnl: 84.08, pnlPercent: 6.7 },
]

export const currentDecisions: TraderDecision[] = [
  { symbol: 'AAPL', company: 'Apple Inc.', action: 'BUY', confidence: 82, targetPrice: 205, stopLoss: 185 },
  { symbol: 'NVDA', company: 'NVIDIA Corp.', action: 'BUY', confidence: 78, targetPrice: 950, stopLoss: 860 },
  { symbol: 'TSLA', company: 'Tesla Inc.', action: 'HOLD', confidence: 61, targetPrice: 190, stopLoss: 160 },
  { symbol: 'AMZN', company: 'Amazon.com Inc.', action: 'SELL', confidence: 73, targetPrice: 175, stopLoss: 190 },
]

export const rejectedDecisions: RejectedDecision[] = [
  { symbol: 'PLTR', action: 'BUY', reason: 'Volatility above threshold', confidence: 47, time: 'May 24, 10:18 AM' },
  { symbol: 'RIVN', action: 'BUY', reason: 'High volatility risk', confidence: 42, time: 'May 24, 09:53 AM' },
  { symbol: 'BBY', action: 'BUY', reason: 'Liquidity below minimum', confidence: 38, time: 'May 23, 03:47 PM' },
  { symbol: 'SOFI', action: 'SELL', reason: 'News sentiment negative', confidence: 46, time: 'May 23, 02:11 PM' },
]

export const recentTrades: TraderTrade[] = [
  { symbol: 'META', side: 'SELL', type: 'Exit', price: 547.21, pnl: 753.92, pnlPercent: 2.68, time: 'May 24, 10:18 AM' },
  { symbol: 'AMD', side: 'SELL', type: 'Exit', price: 243.1, pnl: 474.33, pnlPercent: 2.04, time: 'May 24, 08:09 AM' },
  { symbol: 'NFLX', side: 'BUY', type: 'Entry', price: 625.43, pnl: 168.18, pnlPercent: 1.06, time: 'May 23, 04:11 PM' },
  { symbol: 'AMZN', side: 'BUY', type: 'Entry', price: 176.52, pnl: 79.43, pnlPercent: 0.46, time: 'May 23, 01:16 PM' },
  { symbol: 'MSFT', side: 'SELL', type: 'Exit', price: 423.1, pnl: 63.4, pnlPercent: 0.39, time: 'May 22, 03:41 PM' },
]

export const currentModel: ModelVersion = {
  version: 'v3.2.1',
  accuracy: '72.4%',
  precision: '72.1%',
  recall: '76.8%',
  f1Score: '74.4%',
  trainedOn: 'May 20, 2024',
  status: 'Current',
}

export const modelHistory: ModelVersion[] = [
  currentModel,
  { version: 'v3.1.0', accuracy: '70.8%', precision: '69.9%', recall: '74.2%', f1Score: '72.0%', trainedOn: 'Apr 29, 2024', status: 'Archived' },
  { version: 'v3.0.2', accuracy: '68.5%', precision: '67.2%', recall: '71.6%', f1Score: '69.3%', trainedOn: 'Apr 03, 2024', status: 'Archived' },
]

export const backtestSummary = {
  totalReturn: '+28.12%',
  winRate: '66.3%',
  maxDrawdown: '-8.7%',
  sharpeRatio: '1.48',
  totalTrades: '312',
  profitFactor: '2.12',
}
