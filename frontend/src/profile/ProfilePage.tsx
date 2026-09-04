import { useState, type FormEvent, type ReactNode } from 'react'
import { initialProfile, type ProfileData } from './profileData'
import './profile.css'

type IconName =
  | 'activity'
  | 'bell'
  | 'briefcase'
  | 'chart'
  | 'chevron-down'
  | 'chevron-right'
  | 'close'
  | 'grid'
  | 'lock'
  | 'logout'
  | 'menu'
  | 'more'
  | 'pie-chart'
  | 'search'
  | 'settings'
  | 'star'
  | 'user'
  | 'x'

function Icon({ name, size = 18 }: { name: IconName; size?: number }) {
  const common = {
    fill: 'none',
    stroke: 'currentColor',
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    strokeWidth: 1.8,
  }

  const paths: Record<IconName, ReactNode> = {
    activity: <path d="M3 12h3l2.2-6 3.6 12 2.2-6H21" {...common} />,
    bell: <><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9" {...common} /><path d="M10 21h4" {...common} /></>,
    briefcase: <><rect x="3" y="7" width="18" height="13" rx="2" {...common} /><path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 12h18M10 12v2h4v-2" {...common} /></>,
    chart: <><path d="M4 19V5M4 19h17" {...common} /><path d="m7 15 3-4 3 2 5-7" {...common} /><path d="M16 6h2v2" {...common} /></>,
    'chevron-down': <path d="m6 9 6 6 6-6" {...common} />,
    'chevron-right': <path d="m9 6 6 6-6 6" {...common} />,
    close: <path d="m6 6 12 12M18 6 6 18" {...common} />,
    grid: <><rect x="3" y="3" width="7" height="7" rx="1" {...common} /><rect x="14" y="3" width="7" height="7" rx="1" {...common} /><rect x="3" y="14" width="7" height="7" rx="1" {...common} /><rect x="14" y="14" width="7" height="7" rx="1" {...common} /></>,
    lock: <><rect x="5" y="10" width="14" height="11" rx="2" {...common} /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v3" {...common} /></>,
    logout: <><path d="M10 4H5.5A1.5 1.5 0 0 0 4 5.5v13A1.5 1.5 0 0 0 5.5 20H10M14 8l4 4-4 4M18 12H9" {...common} /></>,
    menu: <path d="M4 7h16M4 12h16M4 17h16" {...common} />,
    more: <><circle cx="5" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="12" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="19" cy="12" r="1" fill="currentColor" stroke="none" /></>,
    'pie-chart': <><path d="M12 3v9h9" {...common} /><path d="M20.5 15A9 9 0 1 1 9 3.5" {...common} /></>,
    search: <><circle cx="10.8" cy="10.8" r="6.8" {...common} /><path d="m16 16 4.5 4.5" {...common} /></>,
    settings: <><circle cx="12" cy="12" r="3" {...common} /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-1.7 1.7-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5v.2h-2.4v-.2a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1L8 17l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H6.7v-2.4h.2a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9L8 8.6l1.7-1.7.1.1a1.7 1.7 0 0 0 1.9.3 1.7 1.7 0 0 0 1-1.5v-.2h2.4v.2a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1 1.7 1.7-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.2V14h-.2a1.7 1.7 0 0 0-1.5 1Z" {...common} /></>,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z" {...common} />,
    user: <><circle cx="12" cy="8" r="3.2" {...common} /><path d="M5.2 20a6.8 6.8 0 0 1 13.6 0" {...common} /></>,
    x: <path d="m6 6 12 12M18 6 6 18" {...common} />,
  }

  return <svg aria-hidden="true" className="icon" height={size} viewBox="0 0 24 24" width={size}>{paths[name]}</svg>
}

function Brand() {
  return <div aria-label="StockLab" className="brand"><span aria-hidden="true" className="brand-mark"><i /><i /><i /></span><span>Stock<span>Lab</span></span></div>
}

function ProfileField({ label, value, editing, name, onChange, type = 'text' }: { label: string; value: string; editing: boolean; name: 'name' | 'email'; onChange: (value: string) => void; type?: 'text' | 'email' }) {
  return <label className="profile-field"><span>{label}</span>{editing ? <input aria-label={label} name={name} onChange={(event) => onChange(event.target.value)} required type={type} value={value} /> : <strong>{value}</strong>}</label>
}

function PasswordModal({ onClose, onSave }: { onClose: () => void; onSave: () => void }) {
  const [error, setError] = useState('')
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const password = String(form.get('new-password') ?? '')
    const confirmation = String(form.get('confirm-password') ?? '')
    if (password.length < 8) {
      setError('Use at least 8 characters for your new password.')
      return
    }
    if (password !== confirmation) {
      setError('The passwords do not match.')
      return
    }
    onSave()
  }

  return <div className="modal-backdrop" onClick={onClose} role="presentation"><section aria-labelledby="password-title" aria-modal="true" className="password-modal" onClick={(event) => event.stopPropagation()} role="dialog"><button aria-label="Close change password dialog" className="modal-close" onClick={onClose} type="button"><Icon name="x" size={17} /></button><div className="modal-icon"><Icon name="lock" size={20} /></div><h2 id="password-title">Change password</h2><p>Set a new password for your StockLab account.</p><form onSubmit={submit}><label htmlFor="current-password">Current password</label><input id="current-password" name="current-password" required type="password" /><label htmlFor="new-password">New password</label><input id="new-password" minLength={8} name="new-password" required type="password" /><label htmlFor="confirm-password">Confirm new password</label><input id="confirm-password" minLength={8} name="confirm-password" required type="password" />{error && <div className="form-error" role="alert">{error}</div>}<div className="modal-actions"><button className="cancel-button" onClick={onClose} type="button">Cancel</button><button className="modal-primary" type="submit">Save password</button></div></form></section></div>
}

const navigation = [
  { label: 'Dashboard', icon: 'grid' as IconName, href: '/dashboard.html' },
  { label: 'Market', icon: 'chart' as IconName, href: '/market.html' },
  { label: 'Portfolio', icon: 'briefcase' as IconName, href: '/portfolio/' },
  { label: 'Watchlist', icon: 'star' as IconName, href: '/watchlist/' },
  { label: 'Alerts', icon: 'bell' as IconName, href: '#alerts' },
]

export default function ProfilePage() {
  const [profile, setProfile] = useState<ProfileData>(initialProfile)
  const [draft, setDraft] = useState<ProfileData>(initialProfile)
  const [editing, setEditing] = useState(false)
  const [passwordModalOpen, setPasswordModalOpen] = useState(false)
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [toast, setToast] = useState('')

  const showToast = (message: string) => {
    setToast(message)
    window.setTimeout(() => setToast(''), 2300)
  }

  const beginEditing = () => {
    setDraft(profile)
    setEditing(true)
  }

  const cancelEditing = () => {
    setDraft(profile)
    setEditing(false)
  }

  const saveProfile = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setProfile(draft)
    setEditing(false)
    showToast('Profile changes saved for this preview.')
  }

  return <div className={`profile-page ${sidebarOpen ? 'sidebar-open' : ''}`}><button aria-label="Close navigation" className="sidebar-backdrop" onClick={() => setSidebarOpen(false)} type="button" /><aside className="profile-sidebar"><Brand /><div className="workspace-switcher"><span className="workspace-avatar">MS</span><span><strong>My portfolio</strong><small>Personal account</small></span><Icon name="chevron-down" size={15} /></div><nav aria-label="Primary navigation" className="sidebar-nav"><span className="nav-label">Overview</span>{navigation.map((item) => <a aria-current={item.label === 'Profile' ? 'page' : undefined} className="nav-item" href={item.href} key={item.label} onClick={() => setSidebarOpen(false)}><Icon name={item.icon} size={18} /><span>{item.label}</span></a>)}<span className="nav-label nav-label-spaced">Manage</span><a aria-current="page" className="nav-item active" href="/profile/"><Icon name="user" size={18} /><span>Profile</span></a><a className="nav-item" href="#settings"><Icon name="settings" size={18} /><span>Settings</span></a></nav><div className="sidebar-footer"><div className="help-card"><span className="help-icon"><Icon name="activity" size={17} /></span><span><strong>Need a hand?</strong><small>Explore StockLab tips</small></span><Icon name="chevron-right" size={16} /></div><div className="user-card"><span className="user-avatar">MS</span><span><strong>{profile.name}</strong><small>Free plan</small></span><button aria-label="More profile options" className="icon-button" type="button"><Icon name="more" size={18} /></button></div></div></aside><main className="profile-main"><header className="profile-topbar"><button aria-label="Open navigation" className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={22} /></button><div className="breadcrumb"><span>Workspace</span><Icon name="chevron-right" size={14} /><strong>Profile</strong></div><div className="topbar-actions"><label className="global-search"><Icon name="search" size={17} /><input aria-label="Search stocks" placeholder="Search stocks..." /></label><button aria-label="Notifications" className="icon-button notification-button" onClick={() => showToast('You are all caught up.')} type="button"><Icon name="bell" size={19} /><i /></button><span className="topbar-avatar">MS</span></div></header><div className="profile-content"><section className="profile-welcome"><div><p className="eyebrow">Account settings</p><h1>Your profile <span>✦</span></h1><p className="welcome-copy">Manage your personal information and account preferences.</p></div></section><div className="profile-layout"><section aria-labelledby="personal-info-title" className="panel personal-panel"><div className="panel-heading"><div><h2 id="personal-info-title">Personal information</h2><p>Keep your account details up to date.</p></div>{!editing && <button className="panel-action" onClick={beginEditing} type="button"><Icon name="user" size={14} /> Edit profile</button>}</div><form onSubmit={saveProfile}><div className="profile-identity"><span className="profile-avatar">{profile.name.split(' ').map((part) => part[0]).join('').slice(0, 2).toUpperCase()}</span><div><strong>{profile.name}</strong><span>StockLab member</span></div></div><div className="profile-fields"><ProfileField editing={editing} label="Full name" name="name" onChange={(value) => setDraft((current) => ({ ...current, name: value }))} value={draft.name} /><ProfileField editing={editing} label="Email address" name="email" onChange={(value) => setDraft((current) => ({ ...current, email: value }))} type="email" value={draft.email} /><ProfileField editing={false} label="Account created" name="name" onChange={() => undefined} value={profile.createdAt} /><ProfileField editing={false} label="Initial capital" name="name" onChange={() => undefined} value={profile.initialCapital} /></div>{editing && <div className="form-actions"><button className="cancel-button" onClick={cancelEditing} type="button">Cancel</button><button className="modal-primary" type="submit">Save changes</button></div>}</form></section><aside className="profile-side-column"><section aria-labelledby="account-summary-title" className="panel account-summary-panel"><div className="panel-heading"><div><h2 id="account-summary-title">Account overview</h2><p>Your StockLab account at a glance.</p></div><span className="verified-badge"><i /> Active</span></div><div className="account-overview-list"><div><span className="overview-icon blue"><Icon name="user" size={16} /></span><span><small>Account type</small><strong>Personal account</strong></span></div><div><span className="overview-icon purple"><Icon name="briefcase" size={16} /></span><span><small>Initial capital</small><strong>{profile.initialCapital}</strong></span></div><div><span className="overview-icon green"><Icon name="activity" size={16} /></span><span><small>Account status</small><strong>Ready to invest</strong></span></div></div></section><section aria-labelledby="security-title" className="panel security-panel"><div className="panel-heading"><div><h2 id="security-title">Security</h2><p>Protect access to your account.</p></div><span className="security-icon"><Icon name="lock" size={16} /></span></div><button className="security-action" onClick={() => setPasswordModalOpen(true)} type="button"><span><strong>Change password</strong><small>Last changed never</small></span><Icon name="chevron-right" size={15} /></button><button className="logout-action" onClick={() => showToast('Logout is simulated in this preview.')} type="button"><Icon name="logout" size={15} /> Logout</button></section></aside></div><p className="simulation-note"><Icon name="activity" size={14} /> This is a frontend preview. Profile changes are not saved to a backend.</p></div></main><div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast}</div>{passwordModalOpen && <PasswordModal onClose={() => setPasswordModalOpen(false)} onSave={() => { setPasswordModalOpen(false); showToast('Password change is simulated in this preview.') }} />}</div>
}
