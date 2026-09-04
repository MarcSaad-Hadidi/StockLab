import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import RegisterPage from './RegisterPage'

createRoot(document.getElementById('register-root')!).render(
  <StrictMode>
    <RegisterPage />
  </StrictMode>,
)
