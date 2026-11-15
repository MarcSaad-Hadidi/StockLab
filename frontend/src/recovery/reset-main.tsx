import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../i18n/i18n'
import { ResetPasswordEntry } from './ResetPasswordPage'
import { consumeResetLink } from './resetLink'

const initialLink = consumeResetLink()
createRoot(document.getElementById('recovery-root')!).render(<StrictMode><ResetPasswordEntry initialLink={initialLink} /></StrictMode>)
