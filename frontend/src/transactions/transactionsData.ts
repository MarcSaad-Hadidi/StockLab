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

export const transactionSummary = {
  totalTrades: 128,
  totalInvested: 48752.19,
  totalProceeds: 41685.32,
  netPnl: 6812.87,
  investedChange: '+8.7%',
  proceedsChange: '+11.2%',
  pnlChange: '+13.6%',
} as const

const firstPageTransactions: Transaction[] = [
  { id: 'tx-aapl-0524', symbol: 'AAPL', company: 'Apple Inc.', assetType: 'Stock', action: 'BUY', quantity: 10, executionPrice: 191.45, totalAmount: 1914.5, date: '2024-05-24T10:32:00.000Z' },
  { id: 'tx-msft-0523', symbol: 'MSFT', company: 'Microsoft Corp.', assetType: 'Stock', action: 'BUY', quantity: 8, executionPrice: 415.6, totalAmount: 3324.8, date: '2024-05-23T14:15:00.000Z' },
  { id: 'tx-nvda-0522', symbol: 'NVDA', company: 'NVIDIA Corp.', assetType: 'Stock', action: 'BUY', quantity: 5, executionPrice: 892.72, totalAmount: 4463.6, date: '2024-05-22T11:03:00.000Z' },
  { id: 'tx-amzn-0521', symbol: 'AMZN', company: 'Amazon.com Inc.', assetType: 'Stock', action: 'SELL', quantity: 3, executionPrice: 186.21, totalAmount: 558.63, date: '2024-05-21T13:00:00.000Z' },
  { id: 'tx-googl-0520', symbol: 'GOOGL', company: 'Alphabet Inc.', assetType: 'Stock', action: 'BUY', quantity: 4, executionPrice: 167.34, totalAmount: 669.36, date: '2024-05-20T09:30:00.000Z' },
  { id: 'tx-tsla-0519', symbol: 'TSLA', company: 'Tesla Inc.', assetType: 'Stock', action: 'SELL', quantity: 2, executionPrice: 154.32, totalAmount: 308.64, date: '2024-05-19T15:20:00.000Z' },
  { id: 'tx-meta-0518', symbol: 'META', company: 'Meta Platforms Inc.', assetType: 'Stock', action: 'BUY', quantity: 3, executionPrice: 485.09, totalAmount: 1455.27, date: '2024-05-18T11:10:00.000Z' },
  { id: 'tx-brkb-0517', symbol: 'BRK.B', company: 'Berkshire Hathaway Inc.', assetType: 'Stock', action: 'BUY', quantity: 1, executionPrice: 404.81, totalAmount: 404.81, date: '2024-05-17T10:25:00.000Z' },
  { id: 'tx-jpm-0516', symbol: 'JPM', company: 'JPMorgan Chase & Co.', assetType: 'Stock', action: 'BUY', quantity: 6, executionPrice: 199.41, totalAmount: 1196.46, date: '2024-05-16T16:45:00.000Z' },
  { id: 'tx-v-0515', symbol: 'V', company: 'Visa Inc.', assetType: 'Stock', action: 'BUY', quantity: 5, executionPrice: 272.31, totalAmount: 1361.55, date: '2024-05-15T09:50:00.000Z' },
]

const additionalTransactionSeeds: Transaction[] = [
  { id: 'tx-spy-seed', symbol: 'SPY', company: 'SPDR S&P 500 ETF', assetType: 'ETF', action: 'BUY', quantity: 4, executionPrice: 529.44, totalAmount: 2117.76, date: '2024-05-14T15:12:00.000Z' },
  { id: 'tx-voo-seed', symbol: 'VOO', company: 'Vanguard S&P 500 ETF', assetType: 'ETF', action: 'BUY', quantity: 2, executionPrice: 534.12, totalAmount: 1068.24, date: '2024-05-13T17:05:00.000Z' },
  { id: 'tx-btc-seed', symbol: 'BTC', company: 'Bitcoin', assetType: 'Crypto', action: 'BUY', quantity: 1, executionPrice: 67421.1, totalAmount: 67421.1, date: '2024-05-12T11:18:00.000Z' },
  { id: 'tx-eth-seed', symbol: 'ETH', company: 'Ethereum', assetType: 'Crypto', action: 'SELL', quantity: 2, executionPrice: 3512.08, totalAmount: 7024.16, date: '2024-05-11T20:44:00.000Z' },
  { id: 'tx-amd-seed', symbol: 'AMD', company: 'Advanced Micro Devices', assetType: 'Stock', action: 'BUY', quantity: 9, executionPrice: 121.84, totalAmount: 1096.56, date: '2024-05-10T14:36:00.000Z' },
  { id: 'tx-nflx-seed', symbol: 'NFLX', company: 'Netflix Inc.', assetType: 'Stock', action: 'SELL', quantity: 2, executionPrice: 1126.4, totalAmount: 2252.8, date: '2024-05-09T18:22:00.000Z' },
]

export const transactions: Transaction[] = [
  ...firstPageTransactions,
  ...Array.from({ length: transactionSummary.totalTrades - firstPageTransactions.length }, (_, index) => {
    const seed = additionalTransactionSeeds[index % additionalTransactionSeeds.length]
    const day = 14 - (index % 14)
    const hour = 9 + (index % 8)
    const minute = (index * 7) % 60
    const date = new Date(Date.UTC(2024, 4, day, hour, minute))
    const quantity = Math.max(1, seed.quantity + ((index % 5) - 2))
    const executionPrice = Number((seed.executionPrice * (1 + ((index % 7) - 3) / 1000)).toFixed(2))

    return {
      ...seed,
      id: `${seed.id}-${index + 1}`,
      quantity,
      executionPrice,
      totalAmount: Number((quantity * executionPrice).toFixed(2)),
      date: date.toISOString(),
    }
  }),
]

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
