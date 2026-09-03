import { useEffect, useState } from 'react'
import { MarketPage } from './MarketPage'
import { getStockBySymbol, marketStocks } from './marketData'
import { marketRoute, stockDetailsRoute } from './marketRoutes'
import { StockDetailsPage } from './StockDetailsPage'

function requestedSymbolFromUrl() {
  return new URLSearchParams(window.location.search).get('symbol')?.trim() || null
}

export function MarketApp() {
  const [requestedSymbol, setRequestedSymbol] = useState<string | null>(requestedSymbolFromUrl)
  const selectedStock = requestedSymbol ? getStockBySymbol(marketStocks, requestedSymbol) : undefined

  useEffect(() => {
    const handlePopState = () => setRequestedSymbol(requestedSymbolFromUrl())
    window.addEventListener('popstate', handlePopState)
    return () => window.removeEventListener('popstate', handlePopState)
  }, [])

  useEffect(() => {
    document.title = selectedStock ? `StockLab — ${selectedStock.symbol}` : 'StockLab — Market'
  }, [selectedStock])

  const openStock = (symbol: string) => {
    window.history.pushState({}, '', stockDetailsRoute(symbol))
    setRequestedSymbol(symbol)
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  const goBackToMarket = () => {
    window.history.pushState({}, '', marketRoute)
    setRequestedSymbol(null)
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  if (requestedSymbol) {
    return <StockDetailsPage onBack={goBackToMarket} requestedSymbol={requestedSymbol} stock={selectedStock} />
  }

  return <MarketPage onOpenStock={openStock} />
}
