import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../i18n/i18n'
import AITraderPage from './AITraderPage'

const root = document.getElementById('ai-trader-root')

if (!root) {
  throw new Error('AI Trader root element was not found')
}

createRoot(root).render(
  <StrictMode>
    <AITraderPage />
  </StrictMode>,
)
