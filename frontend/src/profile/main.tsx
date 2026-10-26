import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RequireAuth } from '../auth/RequireAuth'
import '../i18n/i18n'
import ProfilePage from './ProfilePage'

createRoot(document.getElementById('profile-root')!).render(
  <StrictMode>
    <RequireAuth><ProfilePage /></RequireAuth>
  </StrictMode>,
)
