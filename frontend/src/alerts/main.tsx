import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import AlertsPage from './AlertsPage'

createRoot(document.getElementById('alerts-root')!).render(
  <StrictMode>
    <AlertsPage />
  </StrictMode>,
)
