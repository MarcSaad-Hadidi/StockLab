import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import ProfilePage from './ProfilePage'

createRoot(document.getElementById('profile-root')!).render(
  <StrictMode>
    <ProfilePage />
  </StrictMode>,
)
