import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../i18n/i18n'
import WatchlistPage from './WatchlistPage'

createRoot(document.getElementById('watchlist-root')!).render(
  <StrictMode>
    <WatchlistPage />
  </StrictMode>,
)
