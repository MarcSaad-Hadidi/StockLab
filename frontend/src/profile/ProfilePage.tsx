import { routeFor } from '../navigation/routes'
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
  | 'edit'
  | 'grid'
  | 'key'
  | 'lock'
  | 'logout'
  | 'mail'
  | 'menu'
  | 'more'
  | 'pie-chart'
  | 'search'
  | 'settings'
  | 'shield'
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
    edit: <><path d="m4 16.5-.8 3.3 3.3-.8L18 7.5 15.5 5 4 16.5Z" {...common} /><path d="m13.8 6.7 2.5 2.5M17.2 4.1l2.7 2.7" {...common} /></>,
    grid: <><rect x="3" y="3" width="7" height="7" rx="1" {...common} /><rect x="14" y="3" width="7" height="7" rx="1" {...common} /><rect x="3" y="14" width="7" height="7" rx="1" {...common} /><rect x="14" y="14" width="7" height="7" rx="1" {...common} /></>,
    key: <><circle cx="8" cy="15.5" r="3.5" {...common} /><path d="m10.5 13 8-8M16 5l3 3M14 7l3 3" {...common} /></>,
    lock: <><rect x="5" y="10" width="14" height="11" rx="2" {...common} /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v3" {...common} /></>,
    logout: <><path d="M10 4H5.5A1.5 1.5 0 0 0 4 5.5v13A1.5 1.5 0 0 0 5.5 20H10M14 8l4 4-4 4M18 12H9" {...common} /></>,
    mail: <><rect x="3" y="5" width="18" height="14" rx="2" {...common} /><path d="m4 7 8 6 8-6" {...common} /></>,
    menu: <path d="M4 7h16M4 12h16M4 17h16" {...common} />,
    more: <><circle cx="5" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="12" cy="12" r="1" fill="currentColor" stroke="none" /><circle cx="19" cy="12" r="1" fill="currentColor" stroke="none" /></>,
    'pie-chart': <><path d="M12 3v9h9" {...common} /><path d="M20.5 15A9 9 0 1 1 9 3.5" {...common} /></>,
    search: <><circle cx="10.8" cy="10.8" r="6.8" {...common} /><path d="m16 16 4.5 4.5" {...common} /></>,
    settings: <><circle cx="12" cy="12" r="3" {...common} /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-1.7 1.7-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5v.2h-2.4v-.2a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1L8 17l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H6.7v-2.4h.2a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.9L8 8.6l1.7-1.7.1.1a1.7 1.7 0 0 0 1.9.3 1.7 1.7 0 0 0 1-1.5v-.2h2.4v.2a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1 1.7 1.7-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.5 1h.2V14h-.2a1.7 1.7 0 0 0-1.5 1Z" {...common} /></>,
    shield: <path d="M12 3 5 6v5c0 4.7 2.8 8.1 7 10 4.2-1.9 7-5.3 7-10V6l-7-3Z" {...common} />,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z" {...common} />,
    user: <><circle cx="12" cy="8" r="3.2" {...common} /><path d="M5.2 20a6.8 6.8 0 0 1 13.6 0" {...common} /></>,
    x: <path d="m6 6 12 12M18 6 6 18" {...common} />,
  }

  return <svg aria-hidden="true" className="icon" height={size} viewBox="0 0 24 24" width={size}>{paths[name]}</svg>
}

function Brand() {
  return <div aria-label="StockLab" className="brand"><span aria-hidden="true" className="brand-mark"><i /><i /><i /></span><span>Stock<span>Lab</span></span></div>
}

function ProfileAvatar({ small = false }: { small?: boolean }) {
  return <span aria-label="Alex Johnson profile photo" className={`profile-avatar ${small ? 'profile-avatar-small' : ''}`}><svg aria-hidden="true" viewBox="0 0 96 96"><circle cx="48" cy="48" fill="#d7d9dc" r="48" /><path d="M16 96c2-21 14-31 32-31s30 10 32 31" fill="#4d86be" /><path d="M31 62c3 11 10 17 17 17s14-6 17-17c-5 4-11 6-17 6s-12-2-17-6Z" fill="#d99b79" /><ellipse cx="48" cy="43" fill="#efb38c" rx="18" ry="22" /><path d="M30 40c0-19 8-27 20-27 14 0 21 10 18 28l-5-6c-7 4-16 3-25-3-1 4-4 7-8 8Z" fill="#3a2b27" /><path d="M39 46h3M54 46h3" stroke="#4b3028" strokeLinecap="round" strokeWidth="2" /><path d="M43 56c3 2 7 2 10 0" fill="none" stroke="#a45e52" strokeLinecap="round" strokeWidth="1.6" /></svg></span>
}

function ProfileField({ label, value, editing, type = 'text', options, onChange, onEdit, readOnly = false }: { label: string; value: string; editing: boolean; type?: 'text' | 'email' | 'password' | 'select'; options?: string[]; onChange?: (value: string) => void; onEdit: () => void; readOnly?: boolean }) {
  const isSelect = type === 'select' && options !== undefined
  const fieldControl = isSelect ? <select aria-label={label} onChange={(event) => onChange?.(event.target.value)} required value={value}>{options?.map((option) => <option key={option} value={option}>{option}</option>)}</select> : <input aria-label={label} onChange={(event) => onChange?.(event.target.value)} required type={type} value={value} />
  return <div className="profile-info-row"><span className="profile-info-label">{label}</span>{isSelect ? fieldControl : editing && !readOnly ? fieldControl : <strong>{value}</strong>}<button aria-label={`Edit ${label}`} className="row-edit-button" onClick={onEdit} type="button"><Icon name="edit" size={14} /></button></div>
}

function PreferenceToggle({ label, description, enabled, onChange }: { label: string; description: string; enabled: boolean; onChange: () => void }) {
  return <div className="preference-row"><div><strong>{label}</strong><small>{description}</small></div><button aria-checked={enabled} aria-label={label} className={`toggle ${enabled ? 'toggle-on' : ''}`} onClick={onChange} role="switch" type="button"><span /></button></div>
}

function PasswordModal({ onClose, onSave }: { onClose: () => void; onSave: () => void }) {
  const [error, setError] = useState('')
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const password = String(form.get('new-password') ?? '')
    const confirmation = String(form.get('confirm-password') ?? '')
    if (password.length < 8) { setError('Use at least 8 characters for your new password.'); return }
    if (password !== confirmation) { setError('The passwords do not match.'); return }
    onSave()
  }

  return <div className="modal-backdrop" onClick={onClose} role="presentation"><section aria-labelledby="password-title" aria-modal="true" className="password-modal" onClick={(event) => event.stopPropagation()} role="dialog"><button aria-label="Close change password dialog" className="modal-close" onClick={onClose} type="button"><Icon name="x" size={17} /></button><div className="modal-icon"><Icon name="lock" size={20} /></div><h2 id="password-title">Change Password</h2><p>Choose a new password for your StockLab account.</p><form onSubmit={submit}><label htmlFor="current-password">Current password</label><input id="current-password" name="current-password" required type="password" /><label htmlFor="new-password">New password</label><input id="new-password" minLength={8} name="new-password" required type="password" /><label htmlFor="confirm-password">Confirm new password</label><input id="confirm-password" minLength={8} name="confirm-password" required type="password" />{error && <div className="form-error" role="alert">{error}</div>}<div className="modal-actions"><button className="cancel-button" onClick={onClose} type="button">Cancel</button><button className="modal-primary" type="submit">Save password</button></div></form></section></div>
}

const navigation = [
  { label: 'Dashboard', icon: 'grid' as IconName, href: routeFor('dashboard') },
  { label: 'Market', icon: 'chart' as IconName, href: routeFor('market') },
  { label: 'Portfolio', icon: 'briefcase' as IconName, href: routeFor('portfolio') },
  { label: 'Transactions', icon: 'activity' as IconName, href: routeFor('transactions') },
  { label: 'Watchlist', icon: 'star' as IconName, href: routeFor('watchlist') },
  { label: 'Alerts', icon: 'bell' as IconName, href: routeFor('alerts') },
]

const countryOptions = ['United States', 'Canada', 'United Kingdom', 'France', 'Germany', 'Australia', 'Japan']
const timezoneOptions = [
  '(UTC-8) Pacific Time (US & Canada)',
  '(UTC-7) Mountain Time (US & Canada)',
  '(UTC-6) Central Time (US & Canada)',
  '(UTC-5) Eastern Time (US & Canada)',
  '(UTC-4) Eastern Time (US & Canada)',
  '(UTC+0) Greenwich Mean Time',
  '(UTC+1) Central European Time',
  '(UTC+9) Japan Standard Time',
]

export default function ProfilePage() {
  const [profile, setProfile] = useState<ProfileData>(initialProfile)
  const [draft, setDraft] = useState<ProfileData>(initialProfile)
  const [editing, setEditing] = useState(false)
  const [passwordModalOpen, setPasswordModalOpen] = useState(false)
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [toast, setToast] = useState('')
  const [preferences, setPreferences] = useState({ email: true, alerts: true, marketing: false, currency: 'USD ($)', darkMode: false })

  const showToast = (message: string) => {
    setToast(message)
    window.setTimeout(() => setToast(''), 2300)
  }

  const beginEditing = () => { setDraft(profile); setEditing(true) }
  const cancelEditing = () => { setDraft(profile); setEditing(false) }
  const saveProfile = (event?: FormEvent<HTMLFormElement>) => {
    event?.preventDefault()
    setProfile(draft)
    setEditing(false)
    showToast('Profile changes saved for this preview.')
  }
  const updatePreference = (key: 'email' | 'alerts' | 'marketing' | 'darkMode') => setPreferences((current) => ({ ...current, [key]: !current[key] }))

  const updateDraftField = (key: 'name' | 'email' | 'phone' | 'country' | 'timezone', value: string, commitImmediately = false) => {
    setDraft((current) => ({ ...current, [key]: value }))
    if (commitImmediately) setProfile((current) => ({ ...current, [key]: value }))
  }

  return <div className={`profile-page ${sidebarOpen ? 'sidebar-open' : ''} ${preferences.darkMode ? 'dark-mode' : ''}`}><button aria-label="Close navigation" className="sidebar-backdrop" onClick={() => setSidebarOpen(false)} type="button" /><aside className="profile-sidebar"><Brand /><nav aria-label="Primary navigation" className="sidebar-nav"><span className="nav-label">Overview</span>{navigation.slice(0, 2).map((item) => <a className="nav-item" href={item.href} key={item.label} onClick={() => setSidebarOpen(false)}><Icon name={item.icon} size={16} /><span>{item.label}</span></a>)}<span className="nav-label nav-label-spaced">Your trading</span>{navigation.slice(2).map((item) => <a className="nav-item" href={item.href} key={item.label} onClick={() => setSidebarOpen(false)}><Icon name={item.icon} size={16} /><span>{item.label}</span></a>)}<span className="nav-label nav-label-spaced">AI</span><a className="nav-item" href={routeFor('ai-trader')} onClick={() => setSidebarOpen(false)}><Icon name="activity" size={16} /><span>AI Trader</span></a><span className="nav-label nav-label-spaced">Account</span><a aria-current="page" className="nav-item active" href={routeFor('profile')} onClick={() => setSidebarOpen(false)}><Icon name="user" size={16} /><span>Profile</span></a><button className="nav-item nav-button" onClick={() => window.location.assign(routeFor('logout'))} type="button"><Icon name="logout" size={16} /><span>Logout</span></button></nav></aside><main className="profile-main"><header className="profile-topbar"><button aria-label="Open navigation" className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={20} /></button><div className="breadcrumb"><strong>Profile</strong><span>—</span><a href={routeFor('profile')}>/profile</a></div><div className="topbar-actions"><label className="global-search"><Icon name="search" size={16} /><input aria-label="Search stocks" placeholder="Search stocks, ETFs, news..." /></label><button aria-label="Notifications" className="icon-button notification-button" onClick={() => showToast('You are all caught up.')} type="button"><Icon name="bell" size={18} /><i>2</i></button><button aria-label="Open messages" className="icon-button mail-button" onClick={() => showToast('No new messages.')} type="button"><Icon name="mail" size={17} /></button><button aria-label="Open account menu" className="topbar-account" onClick={() => window.location.assign(routeFor('profile'))} type="button"><ProfileAvatar small /><Icon name="chevron-down" size={14} /></button></div></header><div className="profile-content"><section aria-labelledby="profile-summary-title" className="profile-summary-card"><div className="summary-identity"><ProfileAvatar /><div><h1 id="profile-summary-title">{profile.name}</h1><p>{profile.email}</p><span className="verified-badge"><i /> Verified Account</span></div></div><div className="summary-details"><div><span><Icon name="activity" size={15} /> Member Since</span><strong>May 20, 2024</strong></div><div><span><Icon name="activity" size={15} /> Account Created</span><strong>{profile.createdAt}</strong></div><div><span><Icon name="activity" size={15} /> Initial Capital</span><strong>{profile.initialCapital}</strong></div><div><span>Account Status</span><strong className="active-text">Active</strong></div></div><div className="summary-actions"><button className="summary-primary" onClick={() => editing ? saveProfile() : beginEditing()} type="button"><Icon name="edit" size={14} /> {editing ? 'Save Profile' : 'Edit Profile'}</button><button className="summary-secondary" onClick={() => setPasswordModalOpen(true)} type="button"><Icon name="lock" size={14} /> Change Password</button><button className="summary-logout" onClick={() => window.location.assign(routeFor('logout'))} type="button"><Icon name="logout" size={14} /> Log Out</button></div></section><div className="profile-grid"><section aria-labelledby="personal-info-title" className="panel personal-panel"><div className="panel-heading"><div><h2 id="personal-info-title">Personal Information</h2><p>Manage your personal details and contact information.</p></div></div><form id="profile-form" onSubmit={saveProfile}><div className="profile-info-list"><ProfileField editing={editing} label="Full Name" onChange={(value) => updateDraftField('name', value)} onEdit={beginEditing} value={draft.name} /><ProfileField editing={editing} label="Email Address" onChange={(value) => updateDraftField('email', value)} onEdit={beginEditing} type="email" value={draft.email} /><ProfileField editing={false} label="Password" onEdit={() => setPasswordModalOpen(true)} readOnly value="••••••••••••" /><ProfileField editing={editing} label="Phone Number" onChange={(value) => updateDraftField('phone', value)} onEdit={beginEditing} value={draft.phone} /><ProfileField editing={editing} label="Country" onChange={(value) => updateDraftField('country', value, !editing)} onEdit={beginEditing} options={countryOptions} type="select" value={draft.country} /><ProfileField editing={editing} label="Time Zone" onChange={(value) => updateDraftField('timezone', value, !editing)} onEdit={beginEditing} options={timezoneOptions} type="select" value={draft.timezone} /></div>{editing && <div className="form-actions"><button className="cancel-button" onClick={cancelEditing} type="button">Cancel</button><button className="modal-primary" type="submit">Save changes</button></div>}</form></section><section aria-labelledby="preferences-title" className="panel preferences-panel"><div className="panel-heading"><div><h2 id="preferences-title">Preferences</h2><p>Customize your account preferences.</p></div></div><div className="preferences-list"><PreferenceToggle description="Receive updates about your account and trades." enabled={preferences.email} label="Email Notifications" onChange={() => updatePreference('email')} /><PreferenceToggle description="Get notified about price movements and opportunities." enabled={preferences.alerts} label="Trade Alerts" onChange={() => updatePreference('alerts')} /><PreferenceToggle description="Receive news, tips, and promotional content." enabled={preferences.marketing} label="Marketing Communications" onChange={() => updatePreference('marketing')} /><div className="preference-row select-row"><div><strong>Preferred Currency</strong><small>Choose a default currency for the platform.</small></div><label className="currency-select"><select aria-label="Preferred Currency" onChange={(event) => setPreferences((current) => ({ ...current, currency: event.target.value }))} value={preferences.currency}><option>USD ($)</option><option>CAD ($)</option><option>EUR (€)</option></select><Icon name="chevron-down" size={13} /></label></div><PreferenceToggle description="Switch between light and dark themes." enabled={preferences.darkMode} label="Dark Mode" onChange={() => updatePreference('darkMode')} /></div></section></div><section aria-labelledby="security-title" className="panel account-security-panel"><div className="panel-heading"><div><h2 id="security-title">Account Security</h2><p>Monitor and manage your account security.</p></div></div><div className="security-grid"><button className="security-item" onClick={() => showToast('Two-factor authentication is enabled in this preview.')} type="button"><span className="security-icon security-green"><Icon name="shield" size={20} /></span><span><strong>Two-Factor Authentication</strong><small>Add an extra layer of security to your account.</small></span><b className="security-badge">Enabled</b><Icon name="chevron-right" size={15} /></button><button className="security-item" onClick={() => showToast('You have 3 active sessions.')} type="button"><span className="security-icon security-purple"><Icon name="key" size={20} /></span><span><strong>Active Sessions</strong><small>Manage your active sessions across devices.</small></span><b className="session-count">3 Active</b><Icon name="chevron-right" size={15} /></button></div></section><p className="simulation-note"><Icon name="activity" size={14} /> This is a frontend preview. Profile changes are not saved to a backend.</p></div></main><div aria-live="polite" className={`toast ${toast ? 'visible' : ''}`}>{toast}</div>{passwordModalOpen && <PasswordModal onClose={() => setPasswordModalOpen(false)} onSave={() => { setPasswordModalOpen(false); showToast('Password change is simulated in this preview.') }} />}</div>
}
