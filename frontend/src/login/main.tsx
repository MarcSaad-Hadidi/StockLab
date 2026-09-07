import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../i18n/i18n'
import LoginPage from './LoginPage'

createRoot(document.getElementById('login-root')!).render(
  <StrictMode>
    <LoginPage />
  </StrictMode>,
)
