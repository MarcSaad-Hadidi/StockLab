import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RequireAuth } from '../auth/RequireAuth'
import '../i18n/i18n'
import WatchlistPage from './WatchlistPage'

createRoot(document.getElementById('watchlist-root')!).render(
  <StrictMode>
    <RequireAuth><WatchlistPage /></RequireAuth>
  </StrictMode>,
)
