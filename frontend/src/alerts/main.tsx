import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../i18n/i18n'
import AlertsPage from './AlertsPage'

createRoot(document.getElementById('alerts-root')!).render(
  <StrictMode>
    <AlertsPage />
  </StrictMode>,
)
