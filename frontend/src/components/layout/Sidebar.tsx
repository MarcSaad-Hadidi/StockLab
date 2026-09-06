import { Fragment } from 'react'
import { isCurrentPage, routeFor } from '../../navigation/routes'
import { SidebarIcon } from './SidebarIcon'
import styles from './Sidebar.module.css'
import './layout.css'

const sections = [
  { label: 'Overview', items: [{ label: 'Dashboard', icon: 'grid' }, { label: 'Market', icon: 'chart' }] },
  { label: 'Your trading', items: [
    { label: 'Portfolio', icon: 'briefcase' }, { label: 'Transactions', icon: 'activity' },
    { label: 'Watchlist', icon: 'star' }, { label: 'Alerts', icon: 'bell' },
  ] },
  { label: 'AI', items: [{ label: 'AI Trader', icon: 'activity' }] },
  { label: 'Account', items: [{ label: 'Profile', icon: 'user' }] },
] as const

type SidebarProps = { open: boolean; onClose: () => void }

/** The corrected Alerts sidebar is the shared navigation reference. */
export function Sidebar({ open, onClose }: SidebarProps) {
  return <>
    <button aria-label="Close navigation" className={`${styles.backdrop} ${open ? styles.backdropOpen : ''}`} onClick={onClose} type="button" />
    <aside className={`${styles.sidebar} ${open ? styles.open : ''}`}>
      <div aria-label="StockLab" className={styles.brand}>
        <span aria-hidden="true" className={styles.brandMark}><i /><i /><i /></span>
        <span>Stock<span>Lab</span></span>
      </div>
      <nav aria-label="Primary navigation" className={styles.nav}>
        {sections.map((section, index) => <Fragment key={section.label}>
          <span className={`${styles.label} ${index ? styles.spaced : ''}`}>{section.label}</span>
          {section.items.map(item => {
            const active = isCurrentPage(item.label, window.location.pathname)
            return <a aria-current={active ? 'page' : undefined} className={`${styles.item} ${active ? styles.active : ''}`} href={routeFor(item.label)} key={item.label} onClick={onClose}>
              <SidebarIcon name={item.icon} size={16} /><span>{item.label}</span>
            </a>
          })}
        </Fragment>)}
        <button className={`${styles.item} ${styles.button}`} onClick={() => window.location.assign(routeFor('logout'))} type="button"><SidebarIcon name="logout" size={16} /><span>Logout</span></button>
      </nav>
      <div className={styles.footer}><p>StockLab preview</p></div>
    </aside>
  </>
}
