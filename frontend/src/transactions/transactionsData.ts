export type TransactionAction = 'BUY' | 'SELL'
export type TransactionAssetType = 'Stock' | 'ETF' | 'Crypto'
export type TransactionTypeFilter = 'All' | TransactionAssetType

export type Transaction = {
  id: string
  symbol: string
  company: string
  assetType: TransactionAssetType
  action: TransactionAction
  quantity: number
  executionPrice: number
  totalAmount: number
  date: string
}

export type TransactionFilters = {
  query: string
  assetType: TransactionTypeFilter
  action: TransactionAction | 'All'
  from: string
  to: string
}

export type PaginatedTransactions = {
  items: Transaction[]
  currentPage: number
  totalPages: number
  startIndex: number
  endIndex: number
}

export const transactionSummary = { totalTrades: 0, totalInvested: null, totalProceeds: null, netPnl: null, investedChange: null, proceedsChange: null, pnlChange: null }

export const transactions: Transaction[] = []

export function filterTransactions(items: Transaction[], filters: TransactionFilters): Transaction[] {
  const normalizedQuery = filters.query.trim().toLowerCase()

  return items.filter((transaction) => {
    const matchesQuery = normalizedQuery.length === 0
      || `${transaction.symbol} ${transaction.company}`.toLowerCase().includes(normalizedQuery)
    const matchesAssetType = filters.assetType === 'All' || transaction.assetType === filters.assetType
    const matchesAction = filters.action === 'All' || transaction.action === filters.action
    const transactionDay = transaction.date.slice(0, 10)
    const matchesFrom = filters.from.length === 0 || transactionDay >= filters.from
    const matchesTo = filters.to.length === 0 || transactionDay <= filters.to

    return matchesQuery && matchesAssetType && matchesAction && matchesFrom && matchesTo
  })
}

export function paginateTransactions(items: Transaction[], page: number, pageSize = 10): PaginatedTransactions {
  const safePageSize = Math.max(1, Math.floor(pageSize))
  const totalPages = Math.max(1, Math.ceil(items.length / safePageSize))
  const currentPage = Math.min(Math.max(1, Math.floor(page) || 1), totalPages)
  const startOffset = (currentPage - 1) * safePageSize
  const endOffset = Math.min(startOffset + safePageSize, items.length)

  return {
    items: items.slice(startOffset, endOffset),
    currentPage,
    totalPages,
    startIndex: items.length === 0 ? 0 : startOffset + 1,
    endIndex: endOffset,
  }
}
