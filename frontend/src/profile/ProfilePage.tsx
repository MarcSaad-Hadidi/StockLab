import { Sidebar } from '../components/layout/Sidebar'
import { formatCurrency, formatDate } from '../i18n/formatters'
import { routeFor } from '../navigation/routes'
import { useTranslation } from 'react-i18next'
import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
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

function ProfileAvatar({ small = false }: { small?: boolean }) {
  const { t } = useTranslation()
  return <span aria-label={t('profile.avatarAlt')} className={`profile-avatar ${small ? 'profile-avatar-small' : ''}`}><svg aria-hidden="true" viewBox="0 0 96 96"><circle cx="48" cy="48" fill="#d7d9dc" r="48" /><path d="M16 96c2-21 14-31 32-31s30 10 32 31" fill="#4d86be" /><path d="M31 62c3 11 10 17 17 17s14-6 17-17c-5 4-11 6-17 6s-12-2-17-6Z" fill="#d99b79" /><ellipse cx="48" cy="43" fill="#efb38c" rx="18" ry="22" /><path d="M30 40c0-19 8-27 20-27 14 0 21 10 18 28l-5-6c-7 4-16 3-25-3-1 4-4 7-8 8Z" fill="#3a2b27" /><path d="M39 46h3M54 46h3" stroke="#4b3028" strokeLinecap="round" strokeWidth="2" /><path d="M43 56c3 2 7 2 10 0" fill="none" stroke="#a45e52" strokeLinecap="round" strokeWidth="1.6" /></svg></span>
}

type ProfileOption = { label: string; value: string }

function ProfileField({ label, value, editing, type = 'text', options, onChange, onEdit, readOnly = false }: { label: string; value: string; editing: boolean; type?: 'text' | 'email' | 'password' | 'select'; options?: ProfileOption[]; onChange?: (value: string) => void; onEdit: () => void; readOnly?: boolean }) {
  const { t } = useTranslation()
  const isSelect = type === 'select' && options !== undefined
  const fieldControl = isSelect ? <select aria-label={label} onChange={(event) => onChange?.(event.target.value)} required value={value}>{options?.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select> : <input aria-label={label} onChange={(event) => onChange?.(event.target.value)} required type={type} value={value} />
  return <div className="profile-info-row"><span className="profile-info-label">{label}</span>{isSelect ? fieldControl : editing && !readOnly ? fieldControl : <strong>{value}</strong>}<button aria-label={t('common.editField', { field: label })} className="row-edit-button" onClick={onEdit} type="button"><Icon name="edit" size={14} /></button></div>
}

function PreferenceToggle({ label, description, enabled, onChange }: { label: string; description: string; enabled: boolean; onChange: () => void }) {
  return <div className="preference-row"><div><strong>{label}</strong><small>{description}</small></div><button aria-checked={enabled} aria-label={label} className={`toggle ${enabled ? 'toggle-on' : ''}`} onClick={onChange} role="switch" type="button"><span /></button></div>
}

function PasswordModal({ onClose, onSave }: { onClose: () => void; onSave: () => void }) {
  const { t } = useTranslation()
  const [error, setError] = useState('')
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const password = String(form.get('new-password') ?? '')
    const confirmation = String(form.get('confirm-password') ?? '')
    if (password.length < 8) { setError('profile.passwordModal.shortPassword'); return }
    if (password !== confirmation) { setError('profile.passwordModal.mismatch'); return }
    onSave()
  }

  return <div className="modal-backdrop" onClick={onClose} role="presentation"><section aria-labelledby="password-title" aria-modal="true" className="password-modal" onClick={(event) => event.stopPropagation()} role="dialog"><button aria-label={t('profile.passwordModal.close')} className="modal-close" onClick={onClose} type="button"><Icon name="x" size={17} /></button><div className="modal-icon"><Icon name="lock" size={20} /></div><h2 id="password-title">{t('profile.passwordModal.title')}</h2><p>{t('profile.passwordModal.description')}</p><form onSubmit={submit}><label htmlFor="current-password">{t('profile.passwordModal.currentPassword')}</label><input id="current-password" name="current-password" required type="password" /><label htmlFor="new-password">{t('profile.passwordModal.newPassword')}</label><input id="new-password" minLength={8} name="new-password" required type="password" /><label htmlFor="confirm-password">{t('profile.passwordModal.confirmPassword')}</label><input id="confirm-password" minLength={8} name="confirm-password" required type="password" />{error && <div className="form-error" role="alert">{t(error)}</div>}<div className="modal-actions"><button className="cancel-button" onClick={onClose} type="button">{t('common.cancel')}</button><button className="modal-primary" type="submit">{t('profile.passwordModal.save')}</button></div></form></section></div>
}

export default function ProfilePage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('profile.title')} | StockLab`
  }, [i18n.language, t])
  const [profile, setProfile] = useState<ProfileData>(initialProfile)
  const [draft, setDraft] = useState<ProfileData>(initialProfile)
  const [editing, setEditing] = useState(false)
  const [passwordModalOpen, setPasswordModalOpen] = useState(false)
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [toast, setToast] = useState<{ key: string; options?: { count: number } }>({ key: '' })
  const [preferences, setPreferences] = useState({ email: true, alerts: true, marketing: false, currency: 'USD ($)', darkMode: false })
  const countryOptions = ['us', 'ca', 'gb', 'fr', 'de', 'au', 'jp'].map((value) => ({ label: t(`profile.countries.${value}`), value }))
  const timezoneOptions = ['americaPacific', 'americaMountain', 'americaCentral', 'americaEasternStandard', 'americaEasternDaylight', 'greenwich', 'europeCentral', 'japan'].map((value) => ({ label: t(`profile.timezones.${value}`), value }))

  const showToast = (key: string, options?: { count: number }) => {
    setToast({ key, options })
    window.setTimeout(() => setToast({ key: '' }), 2300)
  }

  const beginEditing = () => { setDraft(profile); setEditing(true) }
  const cancelEditing = () => { setDraft(profile); setEditing(false) }
  const saveProfile = (event?: FormEvent<HTMLFormElement>) => {
    event?.preventDefault()
    setProfile(draft)
    setEditing(false)
    showToast('profile.profileSavedToast')
  }
  const updatePreference = (key: 'email' | 'alerts' | 'marketing' | 'darkMode') => setPreferences((current) => ({ ...current, [key]: !current[key] }))

  const updateDraftField = (key: 'name' | 'email' | 'phone' | 'country' | 'timezone', value: string, commitImmediately = false) => {
    setDraft((current) => ({ ...current, [key]: value }))
    if (commitImmediately) setProfile((current) => ({ ...current, [key]: value }))
  }

  return <div className={`profile-page stocklab-layout ${preferences.darkMode ? 'dark-mode' : ''}`}>
    <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
    <main className="profile-main">
      <header className="profile-topbar">
        <button aria-label={t('common.openNavigation')} className="mobile-menu-button icon-button" onClick={() => setSidebarOpen(true)} type="button"><Icon name="menu" size={20} /></button>
        <div className="breadcrumb"><strong>{t('profile.title')}</strong></div>
        <div className="topbar-actions">
          <label className="global-search"><Icon name="search" size={16} /><input aria-label={t('common.searchStocks')} placeholder={t('common.searchStocksEtfsNewsPlaceholder')} /></label>
          <button aria-label={t('common.notifications')} className="icon-button notification-button" onClick={() => showToast('common.notificationsCaughtUp')} type="button"><Icon name="bell" size={18} /><i>2</i></button>
          <button aria-label={t('common.openMessages')} className="icon-button mail-button" onClick={() => showToast('common.noNewMessages')} type="button"><Icon name="mail" size={17} /></button>
          <button aria-label={t('common.openAccountMenu')} className="topbar-account" onClick={() => window.location.assign(routeFor('profile'))} type="button"><ProfileAvatar small /><Icon name="chevron-down" size={14} /></button>
        </div>
      </header>
      <div className="profile-content"><p role="status">{t('businessData.profileDemo')}</p>
        <section aria-labelledby="profile-summary-title" className="profile-summary-card">
          <div className="summary-identity"><ProfileAvatar /><div><h1 id="profile-summary-title">{profile.name}</h1><p>{profile.email}</p><span className="verified-badge"><i /> {t('profile.verifiedAccount')}</span></div></div>
          <div className="summary-details">
            <div><span><Icon name="activity" size={15} /> {t('profile.memberSince')}</span><strong>{formatDate(profile.createdAt)}</strong></div>
            <div><span><Icon name="activity" size={15} /> {t('profile.accountCreated')}</span><strong>{formatDate(profile.createdAt)}</strong></div>
            <div><span><Icon name="activity" size={15} /> {t('profile.initialCapital')}</span><strong>{formatCurrency(profile.initialCapital)}</strong></div>
            <div><span>{t('profile.accountStatus')}</span><strong className="active-text">{t('profile.active')}</strong></div>
          </div>
          <div className="summary-actions">
            <button className="summary-primary" onClick={() => editing ? saveProfile() : beginEditing()} type="button"><Icon name="edit" size={14} /> {editing ? t('profile.saveProfile') : t('profile.editProfile')}</button>
            <button className="summary-secondary" onClick={() => setPasswordModalOpen(true)} type="button"><Icon name="lock" size={14} /> {t('profile.changePassword')}</button>
            <button className="summary-logout" onClick={() => window.location.assign(routeFor('logout'))} type="button"><Icon name="logout" size={14} /> {t('profile.logOut')}</button>
          </div>
        </section>
        <div className="profile-grid">
          <section aria-labelledby="personal-info-title" className="panel personal-panel">
            <div className="panel-heading"><div><h2 id="personal-info-title">{t('profile.personalInformation')}</h2><p>{t('profile.personalInformationDescription')}</p></div></div>
            <form id="profile-form" onSubmit={saveProfile}>
              <div className="profile-info-list">
                <ProfileField editing={editing} label={t('profile.fullName')} onChange={(value) => updateDraftField('name', value)} onEdit={beginEditing} value={draft.name} />
                <ProfileField editing={editing} label={t('profile.emailAddress')} onChange={(value) => updateDraftField('email', value)} onEdit={beginEditing} type="email" value={draft.email} />
                <ProfileField editing={false} label={t('profile.password')} onEdit={() => setPasswordModalOpen(true)} readOnly value="••••••••••••" />
                <ProfileField editing={editing} label={t('profile.phoneNumber')} onChange={(value) => updateDraftField('phone', value)} onEdit={beginEditing} value={draft.phone} />
                <ProfileField editing={editing} label={t('profile.country')} onChange={(value) => updateDraftField('country', value, !editing)} onEdit={beginEditing} options={countryOptions} type="select" value={draft.country} />
                <ProfileField editing={editing} label={t('profile.timeZone')} onChange={(value) => updateDraftField('timezone', value, !editing)} onEdit={beginEditing} options={timezoneOptions} type="select" value={draft.timezone} />
              </div>
              {editing && <div className="form-actions"><button className="cancel-button" onClick={cancelEditing} type="button">{t('common.cancel')}</button><button className="modal-primary" type="submit">{t('common.saveChanges')}</button></div>}
            </form>
          </section>
          <section aria-labelledby="preferences-title" className="panel preferences-panel">
            <div className="panel-heading"><div><h2 id="preferences-title">{t('profile.preferences')}</h2><p>{t('profile.preferencesDescription')}</p></div></div>
            <div className="preferences-list">
              <PreferenceToggle description={t('profile.emailNotificationsDescription')} enabled={preferences.email} label={t('profile.emailNotifications')} onChange={() => updatePreference('email')} />
              <PreferenceToggle description={t('profile.tradeAlertsDescription')} enabled={preferences.alerts} label={t('profile.tradeAlerts')} onChange={() => updatePreference('alerts')} />
              <PreferenceToggle description={t('profile.marketingCommunicationsDescription')} enabled={preferences.marketing} label={t('profile.marketingCommunications')} onChange={() => updatePreference('marketing')} />
              <div className="preference-row select-row"><div><strong>{t('profile.preferredCurrency')}</strong><small>{t('profile.preferredCurrencyDescription')}</small></div><label className="currency-select"><select aria-label={t('profile.preferredCurrency')} onChange={(event) => setPreferences((current) => ({ ...current, currency: event.target.value }))} value={preferences.currency}><option>USD ($)</option><option>CAD ($)</option><option>EUR (€)</option></select><Icon name="chevron-down" size={13} /></label></div>
              <PreferenceToggle description={t('profile.darkModeDescription')} enabled={preferences.darkMode} label={t('profile.darkMode')} onChange={() => updatePreference('darkMode')} />
            </div>
          </section>
        </div>
        <section aria-labelledby="security-title" className="panel account-security-panel">
          <div className="panel-heading"><div><h2 id="security-title">{t('profile.accountSecurity')}</h2><p>{t('profile.accountSecurityDescription')}</p></div></div>
          <div className="security-grid">
            <button className="security-item" onClick={() => showToast('profile.twoFactorToast')} type="button"><span className="security-icon security-green"><Icon name="shield" size={20} /></span><span><strong>{t('profile.twoFactorAuthentication')}</strong><small>{t('profile.twoFactorDescription')}</small></span><b className="security-badge">{t('profile.enabled')}</b><Icon name="chevron-right" size={15} /></button>
            <button className="security-item" onClick={() => showToast('profile.sessionsToast', { count: 3 })} type="button"><span className="security-icon security-purple"><Icon name="key" size={20} /></span><span><strong>{t('profile.activeSessions')}</strong><small>{t('profile.activeSessionsDescription')}</small></span><b className="session-count">{t('profile.activeSessionsCount', { count: 3 })}</b><Icon name="chevron-right" size={15} /></button>
          </div>
        </section>
        <p className="simulation-note"><Icon name="activity" size={14} /> {t('profile.previewNote')}</p>
      </div>
    </main>
    <div aria-live="polite" className={`toast ${toast.key ? 'visible' : ''}`}>{toast.key ? t(toast.key, toast.options) : ''}</div>
    {passwordModalOpen && <PasswordModal onClose={() => setPasswordModalOpen(false)} onSave={() => { setPasswordModalOpen(false); showToast('profile.passwordChangeToast') }} />}
  </div>
}
