import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../i18n/i18n'
import NotFoundPage from './NotFoundPage'

createRoot(document.getElementById('not-found-root')!).render(
  <StrictMode>
    <NotFoundPage />
  </StrictMode>,
)
