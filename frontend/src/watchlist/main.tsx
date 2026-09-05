import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import WatchlistPage from './WatchlistPage'

createRoot(document.getElementById('watchlist-root')!).render(
  <StrictMode>
    <WatchlistPage />
  </StrictMode>,
)
