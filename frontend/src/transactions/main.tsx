import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { TransactionsPage } from './TransactionsPage'

document.body.classList.add('transactions-body')
document.title = 'Transactions | StockLab'

const transactionsRoot = document.getElementById('transactions-root')

if (!transactionsRoot) {
  throw new Error('The Transactions root element is missing.')
}

createRoot(transactionsRoot).render(
  <StrictMode>
    <TransactionsPage />
  </StrictMode>,
)
