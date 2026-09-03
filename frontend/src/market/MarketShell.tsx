import { useState, type ReactNode } from 'react'
import { MarketIcon, type MarketIconName } from './marketIcons'

type MarketShellProps = {
  children: ReactNode
}

const navigationSections: Array<{
  label?: string
  items: Array<{ id: string; label: string; icon: MarketIconName }>
}> = [
  { items: [{ id: 'dashboard', label: 'Dashboard', icon: 'grid' }] },
  {
    label: 'Your trading',
    items: [
      { id: 'market', label: 'Market', icon: 'globe' },
      { id: 'portfolio', label: 'Portfolio', icon: 'briefcase' },
      { id: 'transactions', label: 'Transactions', icon: 'list' },
      { id: 'watchlist', label: 'Watchlist', icon: 'star' },
      { id: 'alerts', label: 'Alerts', icon: 'bell' },
    ],
  },
  { label: 'AI', items: [{ id: 'ai-trader', label: 'AI Trader', icon: 'robot' }] },
  {
    label: 'Account',
    items: [
      { id: 'profile', label: 'Profile', icon: 'user' },
      { id: 'logout', label: 'Logout', icon: 'logout' },
    ],
  },
]

function BrandIcon() {
  return (
    <span aria-hidden="true" className="market-brand-mark">
      <i />
      <i />
      <i />
    </span>
  )
}

export function MarketShell({
  children,
}: MarketShellProps) {
  const [sidebarOpen, setSidebarOpen] = useState(false)

  return (
    <div className="market-app-shell">
      <aside className={`market-sidebar ${sidebarOpen ? 'market-sidebar-open' : ''}`}>
        <a className="market-brand" href="market.html" onClick={() => setSidebarOpen(false)}>
          <BrandIcon />
          <span>
            Stock<span>Lab</span>
          </span>
        </a>

        <nav aria-label="Main navigation" className="market-sidebar-navigation">
          {navigationSections.map((section) => (
            <div className="market-navigation-section" key={section.label ?? 'primary'}>
              {section.label && <p className="market-navigation-label">{section.label}</p>}
              <div className="market-navigation-items">
                {section.items.map((item) => {
                  const isActive = item.id === 'market'
                  const href = isActive ? 'market.html' : '#'

                  return (
                    <a
                      aria-current={isActive ? 'page' : undefined}
                      className={`market-navigation-item ${isActive ? 'market-navigation-item-active' : ''}`}
                      href={href}
                      key={item.id}
                      onClick={(event) => {
                        setSidebarOpen(false)
                        if (!isActive) {
                          event.preventDefault()
                        }
                      }}
                    >
                      <MarketIcon name={item.icon} size={15} />
                      <span>{item.label}</span>
                    </a>
                  )
                })}
              </div>
            </div>
          ))}
        </nav>

        <button aria-label="Collapse navigation" className="market-sidebar-collapse" type="button" onClick={() => setSidebarOpen(false)}>
          <MarketIcon name="logout" size={16} />
        </button>
      </aside>

      {sidebarOpen && <button aria-label="Close navigation" className="market-sidebar-backdrop" type="button" onClick={() => setSidebarOpen(false)} />}

      <div className="market-main">
        <header className="market-topbar">
          <div className="market-breadcrumb">
            <button aria-label="Open navigation" className="market-mobile-menu" type="button" onClick={() => setSidebarOpen(true)}>
              <MarketIcon name="menu" size={19} />
            </button>
          </div>

          <div className="market-topbar-actions">
            <button aria-label="View notifications" className="market-topbar-icon has-notification" type="button">
              <MarketIcon name="bell" size={17} />
              <span aria-hidden="true" className="market-notification-dot">2</span>
            </button>
            <button aria-label="Open messages" className="market-topbar-icon" type="button">
              <MarketIcon name="mail" size={17} />
            </button>
            <button aria-label="Open Alex Johnson profile" className="market-avatar" type="button">AJ</button>
            <button aria-label="Open account menu" className="market-account-chevron" type="button">
              <MarketIcon name="chevronDown" size={14} />
            </button>
          </div>
        </header>

        <main className="market-content">{children}</main>
      </div>
    </div>
  )
}
