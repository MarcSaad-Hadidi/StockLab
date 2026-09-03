export type AssetType = 'Stock' | 'ETF' | 'Index' | 'Crypto'
export type MarketFilter = 'All' | 'Stocks' | 'ETFs' | 'Indices' | 'Crypto' | 'US Market'
export type MarketSection = 'popular' | 'gainers' | 'losers' | 'search'
export type StockTone = 'positive' | 'negative'

export type MarketStock = {
  symbol: string
  company: string
  description: string
  assetType: AssetType
  market: 'US Market' | 'Global Market'
  price: string
  changePercent: string
  marketCap: string
  tone: StockTone
  section: MarketSection
}

export const marketStocks: MarketStock[] = [
  { symbol: 'AAPL', company: 'Apple Inc.', description: 'Apple Inc.', assetType: 'Stock', market: 'US Market', price: '$191.45', changePercent: '1.35%', marketCap: '$2.94T', tone: 'positive', section: 'popular' },
  { symbol: 'MSFT', company: 'Microsoft Corporation', description: 'Microsoft Corp.', assetType: 'Stock', market: 'US Market', price: '$415.60', changePercent: '0.92%', marketCap: '$3.08T', tone: 'positive', section: 'popular' },
  { symbol: 'NVDA', company: 'NVIDIA Corporation', description: 'NVIDIA Corp.', assetType: 'Stock', market: 'US Market', price: '$892.72', changePercent: '2.18%', marketCap: '$2.21T', tone: 'positive', section: 'popular' },
  { symbol: 'AMZN', company: 'Amazon.com, Inc.', description: 'Amazon.com Inc.', assetType: 'Stock', market: 'US Market', price: '$186.21', changePercent: '0.41%', marketCap: '$1.94T', tone: 'negative', section: 'popular' },
  { symbol: 'GOOGL', company: 'Alphabet Inc.', description: 'Alphabet Inc.', assetType: 'Stock', market: 'US Market', price: '$167.34', changePercent: '1.07%', marketCap: '$2.03T', tone: 'positive', section: 'popular' },
  { symbol: 'META', company: 'Meta Platforms, Inc.', description: 'Meta Platforms, Inc.', assetType: 'Stock', market: 'US Market', price: '$485.09', changePercent: '0.66%', marketCap: '$1.23T', tone: 'positive', section: 'search' },
  { symbol: 'TSLA', company: 'Tesla, Inc.', description: 'Tesla, Inc.', assetType: 'Stock', market: 'US Market', price: '$154.32', changePercent: '0.18%', marketCap: '$490.18B', tone: 'negative', section: 'search' },
  { symbol: 'BRK.B', company: 'Berkshire Hathaway Inc.', description: 'Berkshire Hathaway Co.', assetType: 'Stock', market: 'US Market', price: '$404.81', changePercent: '0.21%', marketCap: '$915.28B', tone: 'positive', section: 'search' },
  { symbol: 'JPM', company: 'JPMorgan Chase & Co.', description: 'JPMorgan Chase & Co.', assetType: 'Stock', market: 'US Market', price: '$199.41', changePercent: '0.57%', marketCap: '$576.88B', tone: 'positive', section: 'search' },
  { symbol: 'V', company: 'Visa Inc.', description: 'Visa Inc.', assetType: 'Stock', market: 'US Market', price: '$272.31', changePercent: '0.38%', marketCap: '$571.38B', tone: 'positive', section: 'search' },
  { symbol: 'SMCI', company: 'Super Micro Computer, Inc.', description: 'Super Micro Comp.', assetType: 'Stock', market: 'US Market', price: '$1,024.50', changePercent: '12.34%', marketCap: '$57.61B', tone: 'positive', section: 'gainers' },
  { symbol: 'ARM', company: 'Arm Holdings plc', description: 'ARM Holdings plc', assetType: 'Stock', market: 'US Market', price: '$156.78', changePercent: '8.91%', marketCap: '$163.42B', tone: 'positive', section: 'gainers' },
  { symbol: 'PLTR', company: 'Palantir Technologies Inc.', description: 'Palantir Tech.', assetType: 'Stock', market: 'US Market', price: '$25.42', changePercent: '7.82%', marketCap: '$55.35B', tone: 'positive', section: 'gainers' },
  { symbol: 'MSTR', company: 'MicroStrategy Incorporated', description: 'MicroStrategy Inc.', assetType: 'Stock', market: 'US Market', price: '$1,589.22', changePercent: '6.45%', marketCap: '$28.41B', tone: 'positive', section: 'gainers' },
  { symbol: 'RDDT', company: 'Reddit, Inc.', description: 'Reddit, Inc.', assetType: 'Stock', market: 'US Market', price: '$61.23', changePercent: '6.11%', marketCap: '$10.87B', tone: 'positive', section: 'gainers' },
  { symbol: 'WBD', company: 'Warner Bros. Discovery, Inc.', description: 'Warner Bros. Disc.', assetType: 'Stock', market: 'US Market', price: '$7.23', changePercent: '6.72%', marketCap: '$17.71B', tone: 'negative', section: 'losers' },
  { symbol: 'PDD', company: 'PDD Holdings Inc.', description: 'PDD Holdings Inc.', assetType: 'Stock', market: 'US Market', price: '$110.32', changePercent: '5.48%', marketCap: '$151.86B', tone: 'negative', section: 'losers' },
  { symbol: 'NIO', company: 'NIO Inc.', description: 'NIO Inc.', assetType: 'Stock', market: 'US Market', price: '$4.21', changePercent: '4.53%', marketCap: '$8.24B', tone: 'negative', section: 'losers' },
  { symbol: 'SNAP', company: 'Snap Inc.', description: 'Snap Inc.', assetType: 'Stock', market: 'US Market', price: '$4.85', changePercent: '4.18%', marketCap: '$7.98B', tone: 'negative', section: 'losers' },
  { symbol: 'LCID', company: 'Lucid Group, Inc.', description: 'Lucid Group Inc.', assetType: 'Stock', market: 'US Market', price: '$2.78', changePercent: '3.90%', marketCap: '$6.41B', tone: 'negative', section: 'losers' },
  { symbol: 'VOO', company: 'Vanguard S&P 500 ETF', description: 'Vanguard S&P 500 ETF', assetType: 'ETF', market: 'US Market', price: '$534.12', changePercent: '0.46%', marketCap: '$468.55B', tone: 'positive', section: 'search' },
  { symbol: 'SPY', company: 'SPDR S&P 500 ETF Trust', description: 'SPDR S&P 500 ETF', assetType: 'ETF', market: 'US Market', price: '$529.44', changePercent: '0.32%', marketCap: '$535.21B', tone: 'positive', section: 'search' },
  { symbol: 'QQQ', company: 'Invesco QQQ Trust', description: 'Invesco QQQ Trust', assetType: 'ETF', market: 'US Market', price: '$453.28', changePercent: '0.68%', marketCap: '$277.55B', tone: 'positive', section: 'search' },
  { symbol: 'DIA', company: 'SPDR Dow Jones Industrial ETF', description: 'SPDR Dow Jones ETF', assetType: 'ETF', market: 'US Market', price: '$389.16', changePercent: '0.22%', marketCap: '$31.44B', tone: 'negative', section: 'search' },
  { symbol: 'IWM', company: 'iShares Russell 2000 ETF', description: 'iShares Russell 2000 ETF', assetType: 'ETF', market: 'US Market', price: '$201.84', changePercent: '0.54%', marketCap: '$73.21B', tone: 'negative', section: 'search' },
  { symbol: 'SPX', company: 'S&P 500 Index', description: 'S&P 500 Index', assetType: 'Index', market: 'US Market', price: '$5,208.12', changePercent: '0.41%', marketCap: 'Index', tone: 'positive', section: 'search' },
  { symbol: 'NDX', company: 'Nasdaq 100 Index', description: 'Nasdaq 100 Index', assetType: 'Index', market: 'US Market', price: '$18,245.32', changePercent: '0.72%', marketCap: 'Index', tone: 'positive', section: 'search' },
  { symbol: 'DJI', company: 'Dow Jones Industrial Average', description: 'Dow Jones Average', assetType: 'Index', market: 'US Market', price: '$38,778.10', changePercent: '0.18%', marketCap: 'Index', tone: 'positive', section: 'search' },
  { symbol: 'RUT', company: 'Russell 2000 Index', description: 'Russell 2000 Index', assetType: 'Index', market: 'US Market', price: '$2,024.66', changePercent: '0.37%', marketCap: 'Index', tone: 'negative', section: 'search' },
  { symbol: 'VIX', company: 'CBOE Volatility Index', description: 'CBOE Volatility Index', assetType: 'Index', market: 'US Market', price: '$14.82', changePercent: '1.12%', marketCap: 'Index', tone: 'negative', section: 'search' },
  { symbol: 'BTC', company: 'Bitcoin', description: 'Bitcoin', assetType: 'Crypto', market: 'Global Market', price: '$67,421.10', changePercent: '2.42%', marketCap: '$1.32T', tone: 'positive', section: 'search' },
  { symbol: 'ETH', company: 'Ethereum', description: 'Ethereum', assetType: 'Crypto', market: 'Global Market', price: '$3,512.08', changePercent: '1.18%', marketCap: '$422.68B', tone: 'positive', section: 'search' },
  { symbol: 'SOL', company: 'Solana', description: 'Solana', assetType: 'Crypto', market: 'Global Market', price: '$142.84', changePercent: '0.85%', marketCap: '$66.34B', tone: 'negative', section: 'search' },
  { symbol: 'XRP', company: 'XRP', description: 'XRP', assetType: 'Crypto', market: 'Global Market', price: '$0.52', changePercent: '0.53%', marketCap: '$28.91B', tone: 'positive', section: 'search' },
  { symbol: 'DOGE', company: 'Dogecoin', description: 'Dogecoin', assetType: 'Crypto', market: 'Global Market', price: '$0.14', changePercent: '1.43%', marketCap: '$20.41B', tone: 'negative', section: 'search' },
]

const assetTypeByFilter: Partial<Record<MarketFilter, AssetType>> = {
  Stocks: 'Stock',
  ETFs: 'ETF',
  Indices: 'Index',
  Crypto: 'Crypto',
}

export function filterMarketStocks(
  stocks: MarketStock[],
  query: string,
  filter: MarketFilter = 'All',
): MarketStock[] {
  const normalizedQuery = query.trim().toLowerCase()
  const assetType = assetTypeByFilter[filter]

  return stocks.filter((stock) => {
    const matchesQuery = normalizedQuery.length === 0
      || `${stock.symbol} ${stock.company} ${stock.description}`.toLowerCase().includes(normalizedQuery)
    const matchesAssetType = assetType ? stock.assetType === assetType : true
    const matchesMarket = filter === 'US Market' ? stock.market === 'US Market' : true

    return matchesQuery && matchesAssetType && matchesMarket
  })
}

export function paginateMarketStocks(stocks: MarketStock[], page: number, pageSize = 10): {
  items: MarketStock[]
  currentPage: number
  totalPages: number
  startIndex: number
  endIndex: number
} {
  const totalPages = Math.max(1, Math.ceil(stocks.length / pageSize))
  const currentPage = Math.min(Math.max(1, page), totalPages)
  const startOffset = (currentPage - 1) * pageSize
  const endOffset = Math.min(startOffset + pageSize, stocks.length)

  return {
    items: stocks.slice(startOffset, endOffset),
    currentPage,
    totalPages,
    startIndex: stocks.length === 0 ? 0 : startOffset + 1,
    endIndex: endOffset,
  }
}

function signedChangeValue(stock: MarketStock): number {
  const value = Number.parseFloat(stock.changePercent)
  return stock.tone === 'positive' ? value : -value
}

export function getMarketSections(stocks: MarketStock[], filter: MarketFilter = 'All'): {
  popular: MarketStock[]
  gainers: MarketStock[]
  losers: MarketStock[]
} {
  const eligibleStocks = filterMarketStocks(stocks, '', filter)

  if (filter === 'All' || filter === 'Stocks') {
    return {
      popular: eligibleStocks.filter((stock) => stock.section === 'popular').slice(0, 5),
      gainers: eligibleStocks.filter((stock) => stock.section === 'gainers').slice(0, 5),
      losers: eligibleStocks.filter((stock) => stock.section === 'losers').slice(0, 5),
    }
  }

  const gainers = eligibleStocks
    .filter((stock) => stock.tone === 'positive')
    .sort((first, second) => signedChangeValue(second) - signedChangeValue(first))
  const losers = eligibleStocks
    .filter((stock) => stock.tone === 'negative')
    .sort((first, second) => signedChangeValue(first) - signedChangeValue(second))

  return {
    popular: eligibleStocks.slice(0, 5),
    gainers: gainers.slice(0, 5),
    losers: losers.slice(0, 5),
  }
}

export function getStockBySymbol(stocks: MarketStock[], symbol: string): MarketStock | undefined {
  const normalizedSymbol = symbol.trim().toUpperCase()
  return stocks.find((stock) => stock.symbol.toUpperCase() === normalizedSymbol)
}
