import { useTranslation } from 'react-i18next'
import { routeFor } from '../../navigation/routes'
import type { ReactNode } from 'react'
import { useAuthUser } from '../../auth/useAuthUser'
import './topbar.css'

type TopBarIconName = 'bell' | 'chevron' | 'menu' | 'user'

function TopBarIcon({ name }: { name: TopBarIconName }) {
  const paths: Record<TopBarIconName, ReactNode> = {
    user: <><circle cx="12" cy="8" r="3.2" /><path d="M5.2 20a6.8 6.8 0 0 1 13.6 0" /></>,
    bell: <path d="M18 9a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9ZM10 21h4" />,
    chevron: <path d="m9 6 6 6-6 6" />,
    menu: <path d="M4 7h16M4 12h16M4 17h16" />,
  }

  return <svg aria-hidden="true" className="app-topbar-icon" fill="none" viewBox="0 0 24 24"><g stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.55">{paths[name]}</g></svg>
}

type TopBarProps = {
  onMenuOpen: () => void
  title: ReactNode
}

export function TopBar({ onMenuOpen, title }: TopBarProps) {
  const { t } = useTranslation()
  const user = useAuthUser()
  const letters = user?.displayName.normalize('NFC').trim().split(/\s+/u)
    .map(word => word.match(/\p{L}/u)?.[0]).filter((letter): letter is string => Boolean(letter)) ?? []
  const initials = letters.length > 1 ? `${letters[0]}${letters[letters.length - 1]}`.toUpperCase() : letters[0]?.toUpperCase()
  const identity = user ? `${user.displayName} (${user.email})` : t('common.openProfile')

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
        </button>
        <button aria-label={t('common.openProfile')} className="app-topbar-account" title={identity} onClick={() => window.location.assign(routeFor('profile'))} type="button">
          <span aria-hidden="true" className="app-topbar-avatar">{initials || <TopBarIcon name="user" />}</span>
          <TopBarIcon name="chevron" />
        </button>
      </div>
    </header>
  )
}
