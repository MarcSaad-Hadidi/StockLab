import { useEffect, useState } from 'react'
import { marketDataApi } from '../api/marketDataClient'
import { PortfolioApiError, portfolioApi, type PortfolioApiPosition, type PortfolioApiTransaction } from '../api/portfolioApi'
import { getAuthSession } from '../auth/authStorage'
import { useAuthSession } from '../auth/useAuthUser'
import { useRecentTransactions, type RecentTransactionsState } from './useRecentTransactions'

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
  totalValue: number | null
  initialCapital: number
  pnl: number | null
  returnPercent: number | null
  currency: string
  positions: PortfolioPosition[]
  transactions: PortfolioApiTransaction[]
}

type PortfolioLoadState = {
  data: Omit<PortfolioData, 'transactions'> | null
  isLoading: boolean
  error: PortfolioApiError | null
}

export type PortfolioDataState = Omit<PortfolioLoadState, 'data'> & {
  data: PortfolioData | null
  recentTransactions: RecentTransactionsState
}

function costValue(position: PortfolioApiPosition) {
  return position.quantity * position.averageCost
}

const portfolioQuoteBudget = 20
const portfolioQuoteBatchSize = 5

export function usePortfolioData(): PortfolioDataState {
  const session = useAuthSession()
  const userId = session?.user.id ?? null
  const authorization = session ? `${session.tokenType} ${session.accessToken}` : null
  const sessionKey = JSON.stringify([userId, authorization])
  const [result, setResult] = useState<{ sessionKey: string; state: PortfolioLoadState } | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    if (!userId || !authorization) return () => controller.abort()
    const active = () => {
      const current = getAuthSession()
      return !controller.signal.aborted && current?.user.id === userId
        && `${current.tokenType} ${current.accessToken}` === authorization
    }

    void portfolioApi.getPortfolio(controller.signal, authorization)
      .then(async (portfolio) => {
        if (!active()) return
        const quotePositions = portfolio.positions.slice(0, portfolioQuoteBudget)
        const quotedPositions: Array<{ position: PortfolioApiPosition; quote: Awaited<ReturnType<typeof marketDataApi.quote>> | null }> = []
        for (let start = 0; start < quotePositions.length; start += portfolioQuoteBatchSize) {
          if (!active()) return
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
          : null
        // Total return is measured against starting capital so realized gains
        // remain visible after a position has been fully sold. Missing quotes
        // make aggregate valuation unavailable, but must not hide other prices.
        const pnl = totalValue === null ? null : totalValue - portfolio.initialCapital
        const returnPercent = pnl === null || portfolio.initialCapital === 0
          ? null
          : (pnl / portfolio.initialCapital) * 100
        const positions = enriched.map(({ position, quote }) => {
          const marketValue = quote ? position.quantity * quote.price : null
          const pnl = marketValue === null ? null : marketValue - costValue(position)
          return {
            symbol: position.symbol,
            name: quote?.name?.trim() || position.symbol,
            quantity: position.quantity,
            averagePrice: position.averageCost,
            currentPrice: quote?.price ?? null,
            dailyChangePercent: quote?.changePercent ?? null,
            marketValue,
            pnl,
            pnlPercent: pnl === null || costValue(position) === 0 ? null : (pnl / costValue(position)) * 100,
            weight: totalValue !== null && totalValue > 0 && marketValue !== null ? (marketValue / totalValue) * 100 : null,
          } satisfies PortfolioPosition
        })

        if (active()) {
          setResult({ sessionKey, state: { data: {
            cashBalance: portfolio.cashBalance,
            investedValue,
            totalValue,
            initialCapital: portfolio.initialCapital,
            pnl,
            returnPercent,
            currency: portfolio.currency,
            positions,
          }, isLoading: false, error: null } })
        }
      })
      .catch((error: unknown) => {
        if (!active()) return
        const portfolioError = error instanceof PortfolioApiError
          ? error
          : new PortfolioApiError(502, 'invalid_response', 'The portfolio service returned an invalid response.')
        setResult({ sessionKey, state: { data: null, isLoading: false, error: portfolioError } })
      })

    return () => controller.abort()
  }, [userId, authorization, sessionKey])

  const current = result?.sessionKey === sessionKey ? result.state : null
  const recentTransactions = useRecentTransactions(Boolean(session && current?.data))
  // Mask the old owner's data during render, before effect cleanup and loading run.
  if (!session) return { data: null, isLoading: false,
    error: new PortfolioApiError(401, 'unauthorized', 'Your session has expired. Please sign in again.'), recentTransactions }
  return { data: current?.data ? { ...current.data, transactions: recentTransactions.data ?? [] } : null,
    isLoading: current?.isLoading ?? true, error: current?.error ?? null, recentTransactions }
}
