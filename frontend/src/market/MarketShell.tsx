import { Sidebar } from '../components/layout/Sidebar'
import { TopBar } from '../components/layout/TopBar'
import { useTranslation } from 'react-i18next'
import { useState, type ReactNode } from 'react'
import { LogoAttribution } from './StockLogo'

type MarketShellProps = {
  children: ReactNode
  breadcrumb?: ReactNode
}

export function MarketShell({
  children,
  breadcrumb,
}: MarketShellProps) {
  const { t } = useTranslation()
  const [sidebarOpen, setSidebarOpen] = useState(false)

  return (
    <div className="market-shell-frame stocklab-layout">
      <div className="market-app-shell">
      <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      <div className="market-main">
        <TopBar onMenuOpen={() => setSidebarOpen(true)} title={breadcrumb ?? t('market.title')} />

        <main className="market-content">{children}<LogoAttribution /></main>
      </div>
    </div>
    </div>
  )
}
