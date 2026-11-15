import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../i18n/i18n'
import ForgotPasswordPage from './ForgotPasswordPage'

createRoot(document.getElementById('recovery-root')!).render(<StrictMode><ForgotPasswordPage /></StrictMode>)
