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
  initialCapital: null,
  currentValue: null,
  profitLoss: null,
  returnPercent: null,
  winRate: null,
  maxDrawdown: null,
}

export const performanceSeries: number[] = []
export const performanceLabels: string[] = []

export const positions: TraderPosition[] = []

export const currentDecisions: TraderDecision[] = []

export const rejectedDecisions: RejectedDecision[] = []

export const recentTrades: TraderTrade[] = []

export const currentModel: ModelVersion | null = null

export const modelHistory: ModelVersion[] = []

export const backtestSummary = {
  totalReturn: null,
  winRate: null,
  maxDrawdown: null,
  sharpeRatio: null,
  totalTrades: null,
  profitFactor: null,
}
