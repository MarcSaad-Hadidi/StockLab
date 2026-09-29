import { useEffect, useState } from 'react'
import { marketDataApi } from '../api/marketDataClient'
import { PortfolioApiError, portfolioApi, type PortfolioApiPosition, type PortfolioApiTransaction } from '../api/portfolioApi'

export type PortfolioPosition = {
  symbol: string
  name: string
  quantity: number
  averagePrice: number
  currentPrice: number | null
  dailyChangePercent: number | null
  marketValue: number | null
  pnl: number | null
  pnlPercent: number | null
  weight: number | null
}

export type PortfolioData = {
  cashBalance: number
  investedValue: number
  totalValue: number
  initialCapital: number
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

const portfolioQuoteBudget = 20
const portfolioQuoteBatchSize = 5

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
        const quotePositions = portfolio.positions.slice(0, portfolioQuoteBudget)
        const quotedPositions: Array<{ position: PortfolioApiPosition; quote: Awaited<ReturnType<typeof marketDataApi.quote>> | null }> = []
        for (let start = 0; start < quotePositions.length; start += portfolioQuoteBatchSize) {
          const batch = quotePositions.slice(start, start + portfolioQuoteBatchSize)
          const results = await Promise.all(batch.map(async (position) => {
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
          quotedPositions.push(...results)
        }
        const quotedBySymbol = new Map(quotedPositions.map(({ position, quote }) => [position.symbol, quote]))
        const enriched = portfolio.positions.map((position) => ({
          position,
          quote: quotedBySymbol.get(position.symbol) ?? null,
        }))

        const hasCompleteMarketData = enriched.every(({ quote }) => quote !== null)
        const marketInvestedValue = enriched.reduce((total, { position, quote }) =>
          total + (quote ? position.quantity * quote.price : 0), 0)
        const investedValue = portfolio.investedValue
        const totalValue = hasCompleteMarketData
          ? portfolio.cashBalance + marketInvestedValue
          : portfolio.totalValue
        // Total return is measured against starting capital so realized gains
        // remain visible after a position has been fully sold. If any quote is
        // unavailable, suppress live valuation details to keep every displayed
        // position consistent with the cost-based API total.
        const pnl = hasCompleteMarketData ? totalValue - portfolio.initialCapital : null
        const returnPercent = pnl === null || portfolio.initialCapital === 0
          ? null
          : (pnl / portfolio.initialCapital) * 100
        const positions = enriched.map(({ position, quote }) => {
          const effectiveQuote = hasCompleteMarketData ? quote : null
          const marketValue = effectiveQuote ? position.quantity * effectiveQuote.price : null
          const pnl = marketValue === null ? null : marketValue - costValue(position)
          return {
            symbol: position.symbol,
            name: effectiveQuote?.name?.trim() || position.symbol,
            quantity: position.quantity,
            averagePrice: position.averageCost,
            currentPrice: effectiveQuote?.price ?? null,
            dailyChangePercent: effectiveQuote?.changePercent ?? null,
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
