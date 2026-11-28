import { AccountPreferences } from './preferences/AccountPreferences'
import './preferences/theme.css'
import { createRoot } from 'react-dom/client'
import './i18n/i18n'
import { MarketApp } from './market/MarketApp'
import './market/market.css'
import './market/stock-details.css'

createRoot(document.getElementById('market-root')!).render(<AccountPreferences><MarketApp /></AccountPreferences>)
