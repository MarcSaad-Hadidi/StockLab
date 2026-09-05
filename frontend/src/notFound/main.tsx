import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import NotFoundPage from './NotFoundPage'

createRoot(document.getElementById('not-found-root')!).render(
  <StrictMode>
    <NotFoundPage />
  </StrictMode>,
)
