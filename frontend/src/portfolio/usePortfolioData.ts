import { useEffect, useState } from 'react'
import { marketDataApi } from '../api/marketDataClient'
import { PortfolioApiError, portfolioApi, type PortfolioApiPosition, type PortfolioApiTransaction } from '../api/portfolioApi'

export type PortfolioPosition = {
  symbol: string
  name: string
  quantity: number
  averagePrice: number
  currentPrice: number | null
  marketValue: number | null
  pnl: number | null
  pnlPercent: number | null
  weight: number | null
}

export type PortfolioData = {
  cashBalance: number
  investedValue: number
  totalValue: number
  initialCapital: number | null
  pnl: number | null
  returnPercent: number | null
  currency: string
  positions: PortfolioPosition[]
  transactions: PortfolioApiTransaction[]
}

export type PortfolioDataState = {
  data: PortfolioData | null
  isLoading: boolean
  error: PortfolioApiError | null
}

function costValue(position: PortfolioApiPosition) {
  return position.quantity * position.averageCost
}

export function usePortfolioData(): PortfolioDataState {
  const [state, setState] = useState<PortfolioDataState>({ data: null, isLoading: true, error: null })

  useEffect(() => {
    const controller = new AbortController()

    void portfolioApi.getPortfolio(controller.signal)
      .then(async (portfolio) => {
        const transactions = await portfolioApi.getRecentTransactions(5, controller.signal).catch((error: unknown) => {
          if (controller.signal.aborted) throw error
          // A portfolio can still be rendered while the optional activity feed
          // is unavailable on an older backend deployment.
          return []
        })
        const enriched = await Promise.all(portfolio.positions.map(async (position) => {
          try {
            const quote = await marketDataApi.quote(position.symbol, controller.signal)
            if (quote.currency.trim().toUpperCase() !== portfolio.currency.trim().toUpperCase())
              return { position, quote: null }
            return { position, quote }
          } catch (error) {
            if (controller.signal.aborted) throw error
            return { position, quote: null }
          }
        }))

        const hasCompleteMarketData = enriched.every(({ quote }) => quote !== null)
        const pricedInvestedValue = enriched.reduce((total, { position, quote }) =>
          total + (quote ? position.quantity * quote.price : costValue(position)), 0)
        const investedValue = hasCompleteMarketData ? pricedInvestedValue : portfolio.investedValue
        const totalValue = hasCompleteMarketData ? portfolio.cashBalance + pricedInvestedValue : portfolio.totalValue
        // The portfolio API reports acquisition cost. Once every current quote is
        // available, compare market value with that cost basis for an honest
        // unrealized P&L; partial quote results remain unavailable.
        const pnl = hasCompleteMarketData ? pricedInvestedValue - portfolio.investedValue : null
        const returnPercent = pnl === null || portfolio.investedValue === 0
          ? null
          : (pnl / portfolio.investedValue) * 100
        const positions = enriched.map(({ position, quote }) => {
          const marketValue = quote ? position.quantity * quote.price : null
          const pnl = marketValue === null ? null : marketValue - costValue(position)
          return {
            symbol: position.symbol,
            name: quote?.name?.trim() || position.symbol,
            quantity: position.quantity,
            averagePrice: position.averageCost,
            currentPrice: quote?.price ?? null,
            marketValue,
            pnl,
            pnlPercent: pnl === null || costValue(position) === 0 ? null : (pnl / costValue(position)) * 100,
            weight: totalValue > 0 && marketValue !== null ? (marketValue / totalValue) * 100 : null,
          } satisfies PortfolioPosition
        })

        if (!controller.signal.aborted) {
          setState({ data: {
            cashBalance: portfolio.cashBalance,
            investedValue,
            totalValue,
            initialCapital: portfolio.initialCapital,
            pnl,
            returnPercent,
            currency: portfolio.currency,
            positions,
            transactions,
          }, isLoading: false, error: null })
        }
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        const portfolioError = error instanceof PortfolioApiError
          ? error
          : new PortfolioApiError(502, 'invalid_response', 'The portfolio service returned an invalid response.')
        setState({ data: null, isLoading: false, error: portfolioError })
      })

    return () => controller.abort()
  }, [])

  return state
}
