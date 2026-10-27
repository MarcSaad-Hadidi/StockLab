import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RequireAuth } from '../auth/RequireAuth'
import '../i18n/i18n'
import PortfolioPage from './PortfolioPage'

createRoot(document.getElementById('portfolio-root')!).render(
  <StrictMode>
    <RequireAuth><PortfolioPage /></RequireAuth>
  </StrictMode>,
)
