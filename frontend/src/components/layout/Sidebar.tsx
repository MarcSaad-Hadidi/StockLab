import { Fragment } from 'react'
import { useTranslation } from 'react-i18next'
import { LanguageSelector } from '../LanguageSelector'
import { isCurrentPage, routeFor } from '../../navigation/routes'
import { SidebarIcon } from './SidebarIcon'
import styles from './Sidebar.module.css'
import './layout.css'

const sections = [
  { key: 'overview', items: [{ key: 'dashboard', label: 'Dashboard', icon: 'grid' }, { key: 'market', label: 'Market', icon: 'chart' }] },
  { key: 'yourTrading', items: [
    { key: 'portfolio', label: 'Portfolio', icon: 'briefcase' }, { key: 'transactions', label: 'Transactions', icon: 'activity' },
    { key: 'watchlist', label: 'Watchlist', icon: 'star' }, { key: 'alerts', label: 'Alerts', icon: 'bell' },
  ] },
  { key: 'ai', items: [{ key: 'aiTrader', label: 'AI Trader', icon: 'activity' }] },
  { key: 'account', items: [{ key: 'profile', label: 'Profile', icon: 'user' }] },
] as const

type SidebarProps = { open: boolean; onClose: () => void }

/** The corrected Alerts sidebar is the shared navigation reference. */
export function Sidebar({ open, onClose }: SidebarProps) {
  const { t } = useTranslation()

  return <>
    <button aria-label={t('common.closeNavigation')} className={`${styles.backdrop} ${open ? styles.backdropOpen : ''}`} onClick={onClose} type="button" />
    <aside className={`${styles.sidebar} ${open ? styles.open : ''}`}>
      <div aria-label="StockLab" className={styles.brand}>
        <span aria-hidden="true" className={styles.brandMark}><i /><i /><i /></span>
        <span>Stock<span>Lab</span></span>
      </div>
      <nav aria-label={t('common.primaryNavigation')} className={styles.nav}>
        {sections.map((section, index) => <Fragment key={section.key}>
          <span className={`${styles.label} ${index ? styles.spaced : ''}`}>{t(`common.navigationSections.${section.key}`)}</span>
          {section.items.map(item => {
            const active = isCurrentPage(item.label, window.location.pathname)
            return <a aria-current={active ? 'page' : undefined} className={`${styles.item} ${active ? styles.active : ''}`} href={routeFor(item.label)} key={item.label} onClick={onClose}>
              <SidebarIcon name={item.icon} size={16} /><span>{t(`common.navigation.${item.key}`)}</span>
            </a>
          })}
        </Fragment>)}
        <button className={`${styles.item} ${styles.button}`} onClick={() => window.location.assign(routeFor('logout'))} type="button"><SidebarIcon name="logout" size={16} /><span>{t('common.navigation.logout')}</span></button>
      </nav>
      <div className={styles.footer}><LanguageSelector className="language-switch-sidebar" /><p>{t('common.preview')}</p></div>
    </aside>
  </>
}
