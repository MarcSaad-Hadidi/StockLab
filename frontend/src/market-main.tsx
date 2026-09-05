import { createRoot } from 'react-dom/client'
import { MarketApp } from './market/MarketApp'
import './market/market.css'
import './market/stock-details.css'

createRoot(document.getElementById('market-root')!).render(<MarketApp />)
