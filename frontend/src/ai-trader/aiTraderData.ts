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
  reasonKey: string
  confidence: number
  time: string
  timeKey: string
}

export type TraderTrade = {
  symbol: string
  side: 'BUY' | 'SELL'
  type: 'Entry' | 'Exit'
  price: number
  pnl: number
  pnlPercent: number
  time: string
  timeKey: string
}

export type ModelVersion = {
  version: string
  accuracy: number
  precision: number
  recall: number
  f1Score: number
  trainedOn: string
  trainedOnKey?: string
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
export const performanceLabels = [
  'aiTrader.chartLabels.may21',
  'aiTrader.chartLabels.may22',
  'aiTrader.chartLabels.may23',
  'aiTrader.chartLabels.may24',
  'aiTrader.chartLabels.may25',
  'aiTrader.chartLabels.may26',
  'aiTrader.chartLabels.may27',
]

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
  { symbol: 'PLTR', action: 'BUY', reasonKey: 'aiTrader.reasons.volatilityThreshold', confidence: 47, time: '2024-05-24T10:18:00-04:00', timeKey: 'aiTrader.times.may24_1018' },
  { symbol: 'RIVN', action: 'BUY', reasonKey: 'aiTrader.reasons.highVolatilityRisk', confidence: 42, time: '2024-05-24T09:53:00-04:00', timeKey: 'aiTrader.times.may24_0953' },
  { symbol: 'BBY', action: 'BUY', reasonKey: 'aiTrader.reasons.liquidityMinimum', confidence: 38, time: '2024-05-23T15:47:00-04:00', timeKey: 'aiTrader.times.may23_0347' },
  { symbol: 'SOFI', action: 'SELL', reasonKey: 'aiTrader.reasons.newsSentiment', confidence: 46, time: '2024-05-23T14:11:00-04:00', timeKey: 'aiTrader.times.may23_0211' },
]

export const recentTrades: TraderTrade[] = [
  { symbol: 'META', side: 'SELL', type: 'Exit', price: 547.21, pnl: 753.92, pnlPercent: 2.68, time: '2024-05-24T10:18:00-04:00', timeKey: 'aiTrader.times.may24_1018' },
  { symbol: 'AMD', side: 'SELL', type: 'Exit', price: 243.1, pnl: 474.33, pnlPercent: 2.04, time: '2024-05-24T08:09:00-04:00', timeKey: 'aiTrader.times.may24_0809' },
  { symbol: 'NFLX', side: 'BUY', type: 'Entry', price: 625.43, pnl: 168.18, pnlPercent: 1.06, time: '2024-05-23T16:11:00-04:00', timeKey: 'aiTrader.times.may23_0411' },
  { symbol: 'AMZN', side: 'BUY', type: 'Entry', price: 176.52, pnl: 79.43, pnlPercent: 0.46, time: '2024-05-23T13:16:00-04:00', timeKey: 'aiTrader.times.may23_0116' },
  { symbol: 'MSFT', side: 'SELL', type: 'Exit', price: 423.1, pnl: 63.4, pnlPercent: 0.39, time: '2024-05-22T15:41:00-04:00', timeKey: 'aiTrader.times.may22_0341' },
]

export const currentModel: ModelVersion = {
  version: 'v3.2.1',
  accuracy: 72.4,
  precision: 72.1,
  recall: 76.8,
  f1Score: 74.4,
  trainedOn: '2024-05-20',
  trainedOnKey: 'aiTrader.modelHistory.may20',
  status: 'Current',
}

export const modelHistory: ModelVersion[] = [
  currentModel,
  { version: 'v3.1.0', accuracy: 70.8, precision: 69.9, recall: 74.2, f1Score: 72, trainedOn: '2024-04-29', trainedOnKey: 'aiTrader.modelHistory.apr29', status: 'Archived' },
  { version: 'v3.0.2', accuracy: 68.5, precision: 67.2, recall: 71.6, f1Score: 69.3, trainedOn: '2024-04-03', trainedOnKey: 'aiTrader.modelHistory.apr03', status: 'Archived' },
]

export const backtestSummary = {
  totalReturn: 28.12,
  winRate: 66.3,
  maxDrawdown: -8.7,
  sharpeRatio: 1.48,
  totalTrades: 312,
  profitFactor: 2.12,
}
