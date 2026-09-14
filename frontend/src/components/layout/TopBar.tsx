import { useTranslation } from 'react-i18next'
import { routeFor } from '../../navigation/routes'
import type { ReactNode } from 'react'
import './topbar.css'

type TopBarIconName = 'bell' | 'chevron' | 'menu'

function TopBarIcon({ name }: { name: TopBarIconName }) {
  const paths: Record<TopBarIconName, ReactNode> = {
    bell: <><path d="M18 9a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9ZM10 21h4" /><circle cx="19" cy="5" r="3" fill="#4353f5" stroke="#fff" strokeWidth="1.5" /></>,
    chevron: <path d="m9 6 6 6-6 6" />,
    menu: <path d="M4 7h16M4 12h16M4 17h16" />,
  }

  return <svg aria-hidden="true" className="app-topbar-icon" fill="none" viewBox="0 0 24 24"><g stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.55">{paths[name]}</g></svg>
}

type TopBarProps = {
  onMenuOpen: () => void
  title: ReactNode
  avatar?: string
}

export function TopBar({ avatar = 'MS', onMenuOpen, title }: TopBarProps) {
  const { t } = useTranslation()

  return (
    <header className="app-topbar">
      <div className="app-topbar-title">
        <button aria-label={t('common.openNavigation')} className="app-topbar-menu" onClick={onMenuOpen} type="button">
          <TopBarIcon name="menu" />
        </button>
        <span className="app-topbar-title-text">{title}</span>
      </div>
      <div className="app-topbar-actions">
        <button aria-label={t('common.notifications')} className="app-topbar-notification" onClick={() => window.location.assign(routeFor('alerts'))} type="button">
          <TopBarIcon name="bell" />
          <span aria-hidden="true" className="app-topbar-badge">2</span>
        </button>
        <button aria-label={t('common.openProfile')} className="app-topbar-account" onClick={() => window.location.assign(routeFor('profile'))} type="button">
          <span className="app-topbar-avatar">{avatar}</span>
          <TopBarIcon name="chevron" />
        </button>
      </div>
    </header>
  )
}
