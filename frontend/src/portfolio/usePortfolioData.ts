import { useEffect, useRef, useState } from 'react'
import { marketDataApi } from '../api/marketDataClient'
import { PortfolioApiError, portfolioApi, type PortfolioApiPosition, type PortfolioApiTransaction } from '../api/portfolioApi'
import { getAuthSession } from '../auth/authStorage'
import { addAmounts, subtractAmounts, tradeProduct, valuationProduct, amountPercent, amountSign } from '../api/decimalMath'
import { useAuthSession } from '../auth/useAuthUser'
import { useRecentTransactions, type RecentTransactionsState } from './useRecentTransactions'

export type PortfolioPosition = {
  symbol: string
  name: string
  quantity: string
  averagePrice: string
  currentPrice: string | null
  dailyChangePercent: number | null
  marketValue: string | null
  pnl: string | null
  pnlPercent: number | null
  weight: number | null
}

export type PortfolioData = {
  cashBalance: string
  investedValue: string
  totalValue: string | null
  initialCapital: string
  pnl: string | null
  returnPercent: number | null
  currency: string
  positions: PortfolioPosition[]
  transactions: PortfolioApiTransaction[]
}

type PortfolioLoadState = {
  data: Omit<PortfolioData, 'transactions'> | null
  error: PortfolioApiError | null
  marketDataIncomplete: boolean
  quoteLimit: number | null
  quoteLoadFailed: boolean
}

export type PortfolioDataState = Omit<PortfolioLoadState, 'data'> & {
  data: PortfolioData | null
  isLoading: boolean
  recentTransactions: RecentTransactionsState
  retry: () => void
}

function costValue(position: PortfolioApiPosition) {
  return tradeProduct(position.averageCost, position.quantity)
}

const portfolioQuoteBudget = 20
const portfolioQuoteBatchSize = 5

export function usePortfolioData(): PortfolioDataState {
  const session = useAuthSession()
  const userId = session?.user.id ?? null
  const authorization = session ? `${session.tokenType} ${session.accessToken}` : null
  const sessionKey = JSON.stringify([userId, authorization])
  const [revision, setRevision] = useState(0)
  const requestKey = JSON.stringify([sessionKey, revision])
  const [result, setResult] = useState<{ sessionKey: string; requestKey: string; state: PortfolioLoadState } | null>(null)
  const activeRequest = useRef<AbortController | null>(null)
  const retryQueued = useRef(false)

  useEffect(() => {
    retryQueued.current = false
    const controller = new AbortController()
    if (!userId || !authorization) return () => controller.abort()
    activeRequest.current = controller
    const active = () => {
      const current = getAuthSession()
      return !controller.signal.aborted && current?.user.id === userId
        && `${current.tokenType} ${current.accessToken}` === authorization
    }

    void portfolioApi.getPortfolio(controller.signal, authorization)
      .then(async (portfolio) => {
        if (!active()) return
        const quotePositions = portfolio.positions.slice(0, portfolioQuoteBudget)
        let quoteLoadFailed = false
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
              quoteLoadFailed = true
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
          addAmounts(total, quote ? valuationProduct(quote.priceDecimal, position.quantity) : '0'), '0')
        const investedValue = portfolio.investedValue
        const totalValue = hasCompleteMarketData
          ? addAmounts(portfolio.cashBalance, marketInvestedValue)
          : null
        // Total return is measured against starting capital so realized gains
        // remain visible after a position has been fully sold. Missing quotes
        // make aggregate valuation unavailable, but must not hide other prices.
        const pnl = totalValue === null ? null : subtractAmounts(totalValue, portfolio.initialCapital)
        const returnPercent = pnl === null
          ? null
          : amountPercent(pnl, portfolio.initialCapital)
        const positions = enriched.map(({ position, quote }) => {
          const marketValue = quote ? valuationProduct(quote.priceDecimal, position.quantity) : null
          const pnl = marketValue === null ? null : subtractAmounts(marketValue, costValue(position))
          return {
            symbol: position.symbol,
            name: quote?.name?.trim() || position.symbol,
            quantity: position.quantity,
            averagePrice: position.averageCost,
            currentPrice: quote?.priceDecimal ?? null,
            dailyChangePercent: quote?.changePercent ?? null,
            marketValue,
            pnl,
            pnlPercent: pnl === null ? null : amountPercent(pnl, costValue(position)),
            weight: totalValue !== null && amountSign(totalValue) > 0 && marketValue !== null ? amountPercent(marketValue, totalValue) : null,
          } satisfies PortfolioPosition
        })

        if (active()) {
          setResult({ sessionKey, requestKey, state: { data: {
            cashBalance: portfolio.cashBalance,
            investedValue,
            totalValue,
            initialCapital: portfolio.initialCapital,
            pnl,
            returnPercent,
            currency: portfolio.currency,
            positions,
          }, error: null, marketDataIncomplete: !hasCompleteMarketData,
          quoteLimit: portfolio.positions.length > portfolioQuoteBudget ? portfolioQuoteBudget : null,
          quoteLoadFailed } })
        }
      })
      .catch((error: unknown) => {
        if (!active()) return
        const portfolioError = error instanceof PortfolioApiError
          ? error
          : new PortfolioApiError(502, 'invalid_response', 'The portfolio service returned an invalid response.')
        // Keep this account's last successful data on a failed refresh. A rejected
        // session must not continue displaying financial data from that session.
        setResult(previous => ({ sessionKey, requestKey, state: {
          data: portfolioError.code !== 'unauthorized' && previous?.sessionKey === sessionKey ? previous.state.data : null,
          error: portfolioError,
          marketDataIncomplete: previous?.sessionKey === sessionKey && previous.state.marketDataIncomplete,
          quoteLimit: previous?.sessionKey === sessionKey ? previous.state.quoteLimit : null,
          quoteLoadFailed: previous?.sessionKey === sessionKey && previous.state.quoteLoadFailed,
        } }))
      })
      .finally(() => { if (activeRequest.current === controller) activeRequest.current = null })

    return () => {
      controller.abort()
      if (activeRequest.current === controller) activeRequest.current = null
    }
  }, [userId, authorization, sessionKey, requestKey])

  function retry() {
    const current = getAuthSession()
    if (!current || current.user.id !== userId || `${current.tokenType} ${current.accessToken}` !== authorization
      || activeRequest.current || retryQueued.current) return
    retryQueued.current = true
    setRevision(value => value + 1)
  }

  const current = result?.sessionKey === sessionKey ? result.state : null
  const recentTransactions = useRecentTransactions(Boolean(session && current?.data))
  // Mask the old owner's data during render, before effect cleanup and loading run.
  if (!session) return { data: null, isLoading: false,
    error: new PortfolioApiError(401, 'unauthorized', 'Your session has expired. Please sign in again.'),
    marketDataIncomplete: false, quoteLimit: null, quoteLoadFailed: false, retry, recentTransactions }
  return { data: current?.data ? { ...current.data, transactions: recentTransactions.data ?? [] } : null,
    isLoading: result?.sessionKey !== sessionKey || result.requestKey !== requestKey,
    error: current?.error ?? null, marketDataIncomplete: current?.marketDataIncomplete ?? false,
    quoteLimit: current?.quoteLimit ?? null, quoteLoadFailed: current?.quoteLoadFailed ?? false, retry, recentTransactions }
}
