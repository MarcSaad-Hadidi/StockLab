import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../i18n/i18n'
import RegisterPage from './RegisterPage'

createRoot(document.getElementById('register-root')!).render(
  <StrictMode>
    <RegisterPage />
  </StrictMode>,
)
