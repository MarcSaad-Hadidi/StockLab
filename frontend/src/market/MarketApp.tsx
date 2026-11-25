import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MarketPage } from './MarketPage'
import { marketRoute, stockDetailsRoute } from './marketRoutes'
import { StockDetailsPage } from './StockDetailsPage'

function requestedAssetFromUrl() {
  const params = new URLSearchParams(window.location.search)
  const symbol = params.get('symbol')?.trim()
  return symbol ? { symbol, instrumentType: params.get('instrumentType') } : null
}

export function MarketApp() {
  const { i18n, t } = useTranslation()
  const [requestedAsset, setRequestedAsset] = useState(requestedAssetFromUrl)
  const requestedSymbol = requestedAsset?.symbol

  useEffect(() => {
    const handlePopState = () => setRequestedAsset(requestedAssetFromUrl())
    window.addEventListener('popstate', handlePopState)
    return () => window.removeEventListener('popstate', handlePopState)
  }, [])

  useEffect(() => {
    document.title = requestedSymbol ? `StockLab — ${requestedSymbol}` : `StockLab — ${t('market.title')}`
  }, [i18n.language, requestedSymbol, t])

  const openStock = (symbol: string, instrumentType?: string | null) => {
    window.history.pushState({}, '', stockDetailsRoute(symbol, instrumentType))
    setRequestedAsset({ symbol, instrumentType: instrumentType ?? null })
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  const goBackToMarket = () => {
    window.history.pushState({}, '', marketRoute)
    setRequestedAsset(null)
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  if (requestedAsset) {
    return <StockDetailsPage onBack={goBackToMarket} requestedSymbol={requestedAsset.symbol}
      instrumentType={requestedAsset.instrumentType} key={requestedAsset.symbol} />
  }

  return <MarketPage onOpenStock={openStock} />
}
