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

export const assetOptions: { symbol: string; name: string; exchange: string; lastPrice: number | null }[] = []

export const initialAlerts: PriceAlert[] = []
