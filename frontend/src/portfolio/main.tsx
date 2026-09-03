import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import PortfolioPage from './PortfolioPage'

createRoot(document.getElementById('portfolio-root')!).render(
  <StrictMode>
    <PortfolioPage />
  </StrictMode>,
)
