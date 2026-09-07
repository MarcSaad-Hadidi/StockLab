import { Sidebar } from '../components/layout/Sidebar'
import { routeFor } from '../navigation/routes'
import { useTranslation } from 'react-i18next'
import { useState, type ReactNode } from 'react'
import { MarketIcon } from './marketIcons'

type MarketShellProps = {
  children: ReactNode
  breadcrumb?: ReactNode
  topbarSearch?: boolean
}

export function MarketShell({
  children,
  breadcrumb,
  topbarSearch = false,
}: MarketShellProps) {
  const { t } = useTranslation()
  const [sidebarOpen, setSidebarOpen] = useState(false)

  return (
    <div className={`market-shell-frame stocklab-layout ${breadcrumb ? 'market-shell-frame-context' : ''}`}>
      {breadcrumb && <div className="market-context-bar"><div className="market-breadcrumb-copy">{breadcrumb}</div></div>}
      <div className="market-app-shell">
      <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      <div className="market-main">
        <header className="market-topbar">
          <div className="market-breadcrumb">
            <button aria-label={t('common.openNavigation')} className="market-mobile-menu" type="button" onClick={() => setSidebarOpen(true)}>
              <MarketIcon name="menu" size={19} />
            </button>
          </div>

          {topbarSearch && (
            <label className="market-shell-search">
              <MarketIcon name="search" size={15} />
              <span className="market-sr-only">{t('common.searchStocksEtfsNews')}</span>
              <input aria-label={t('common.searchStocksEtfsNews')} placeholder={t('common.searchStocksEtfsNewsPlaceholder')} type="search" />
            </label>
          )}

          <div className="market-topbar-actions">
            <button aria-label={t('common.notifications')} className="market-topbar-icon has-notification" type="button">
              <MarketIcon name="bell" size={17} />
              <span aria-hidden="true" className="market-notification-dot">2</span>
            </button>
            <button aria-label={t('common.openMessages')} className="market-topbar-icon" type="button">
              <MarketIcon name="mail" size={17} />
            </button>
            <button aria-label={t('common.openProfile')} className="market-avatar" onClick={() => window.location.assign(routeFor('profile'))} type="button">AJ</button>
            <button aria-label={t('common.openAccountMenu')} className="market-account-chevron" onClick={() => window.location.assign(routeFor('profile'))} type="button">
              <MarketIcon name="chevronDown" size={14} />
            </button>
          </div>
        </header>

        <main className="market-content">{children}</main>
      </div>
    </div>
    </div>
  )
}
