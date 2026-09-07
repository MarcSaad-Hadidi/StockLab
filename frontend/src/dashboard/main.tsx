import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../i18n/i18n'
import { DashboardPage } from './DashboardPage'
import './DashboardPage.css'

document.body.classList.add('dashboard-body')

const dashboardRoot = document.getElementById('dashboard-root')

if (!dashboardRoot) {
  throw new Error('The Dashboard root element is missing.')
}

createRoot(dashboardRoot).render(
  <StrictMode>
    <DashboardPage />
  </StrictMode>,
)
