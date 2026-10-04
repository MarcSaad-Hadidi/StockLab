import { Sidebar } from '../components/layout/Sidebar'
import { TopBar } from '../components/layout/TopBar'
import { formatCurrency, formatDate } from '../i18n/formatters'
import { routeFor } from '../navigation/routes'
import { clearAuthSession } from '../auth/authStorage'
import { useTranslation } from 'react-i18next'
import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { useProfileData } from './useProfileData'
import type { UpdateProfileRequest, UserProfile } from '../api/profileApi'
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

function ProfileField({ label, value, editing = false, type = 'text', onChange, onEdit, disabled = false, error = false }: {
  label: string; value: string; editing?: boolean; type?: 'text' | 'email'
  onChange?: (value: string) => void; onEdit?: () => void; disabled?: boolean; error?: boolean
}) {
  const { t } = useTranslation()
  return <div className="profile-info-row">
    <span className="profile-info-label">{label}</span>
    {editing && onChange ? <input aria-invalid={error} aria-label={label} disabled={disabled}
      maxLength={type === 'email' ? 254 : 100} onChange={event => onChange(event.target.value)}
      required type={type} value={value} /> : <strong title={value}>{value}</strong>}
    <button aria-label={t('common.editField', { field: label })} className="row-edit-button"
      disabled={disabled || !onEdit} onClick={onEdit} title={!onEdit ? t('businessData.unavailable') : undefined}
      type="button"><Icon name="edit" size={14} /></button>
  </div>
}

function UnavailablePreference({ label, description }: { label: string; description: string }) {
  const { t } = useTranslation()
  return <div className="preference-row"><div><strong>{label}</strong><small>{description}</small></div>
    <span aria-label={t('businessData.unavailable')} className="profile-unavailable">—</span></div>
}

export default function ProfilePage() {
  const { i18n, t } = useTranslation()
  useEffect(() => { document.title = `${t('profile.title')} | StockLab` }, [i18n.language, t])
  const { profile, capital, loading, loadError, saveError, fieldErrors, saving, sessionWarning, save, reload } = useProfileData()
  const [draft, setDraft] = useState<UpdateProfileRequest>({ displayName: '', email: '' })
  const [editingProfile, setEditingProfile] = useState<UserProfile | null>(null)
  const editing = profile !== null && editingProfile === profile
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [saved, setSaved] = useState(false)
  const canEdit = profile !== null && !loading && !saving && loadError === null
  const nameError = Object.keys(fieldErrors).some(key => key.toLowerCase() === 'displayname')
  const emailError = saveError === 'email_already_registered' || Object.keys(fieldErrors).some(key => key.toLowerCase() === 'email')

  const beginEditing = () => {
    if (!canEdit) return
    setDraft({ displayName: profile.displayName, email: profile.email })
    setSaved(false)
    setEditingProfile(profile)
  }
  const saveProfile = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!canEdit || !draft.displayName.trim()) return
    setSaved(false)
    if (await save(draft)) { setEditingProfile(null); setSaved(true) }
  }
  const logout = () => { clearAuthSession(); window.location.assign(routeFor('logout')) }

  return <div className="profile-page stocklab-layout">
    <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
    <main className="profile-main">
      <TopBar onMenuOpen={() => setSidebarOpen(true)} title={t('profile.title')} />
      <div className="profile-content" aria-busy={loading}>
        {loading && <p className="profile-feedback" role="status">{t('profile.loading')}</p>}
        {loadError && <div className="profile-feedback form-error" role="alert">
          <p>{t(`profile.errors.${loadError}`)}</p>
          {loadError === 'unauthorized' ? <a href={routeFor('login')}>{t('profile.signIn')}</a>
            : <button className="cancel-button" onClick={() => { setEditingProfile(null); setSaved(false); reload() }} type="button">{t('marketApi.retry')}</button>}
        </div>}
        {sessionWarning && <p className="profile-feedback form-error" role="alert">{t('profile.sessionWarning')}</p>}
        <section aria-labelledby="profile-summary-title" className="profile-summary-card">
          <div className="summary-identity"><div><h1 id="profile-summary-title" title={profile?.displayName}>{profile?.displayName ?? '—'}</h1><p title={profile?.email}>{profile?.email ?? '—'}</p></div></div>
          <div className="summary-details">
            <div><span>{t('profile.memberSince')}</span><strong>{profile ? formatDate(profile.createdAtUtc) : '—'}</strong></div>
            <div><span>{t('profile.accountCreated')}</span><strong>{profile ? formatDate(profile.createdAtUtc) : '—'}</strong></div>
            <div><span>{t('profile.initialCapital')}</span><strong>{formatCurrency(capital?.initialCapital, i18n.language, 2, capital?.currency)}</strong></div>
            <div><span>{t('profile.accountStatus')}</span><strong title={t('businessData.unavailable')}>—</strong></div>
          </div>
          <div className="summary-actions">
            {editing ? <button key="save" className="summary-primary" disabled={!canEdit || !draft.displayName.trim()} form="profile-form" type="submit"><Icon name="edit" size={14} />{t(saving ? 'profile.saving' : 'profile.saveProfile')}</button>
              : <button key="edit" className="summary-primary" disabled={!canEdit} onClick={beginEditing} type="button"><Icon name="edit" size={14} />{t('profile.editProfile')}</button>}
            <button className="summary-secondary" disabled title={t('businessData.unavailable')} type="button"><Icon name="lock" size={14} />{t('profile.changePassword')}</button>
            <button className="summary-logout" onClick={logout} type="button"><Icon name="logout" size={14} />{t('profile.logOut')}</button>
          </div>
        </section>
        <div className="profile-grid">
          <section aria-labelledby="personal-info-title" className="panel personal-panel">
            <div className="panel-heading"><div><h2 id="personal-info-title">{t('profile.personalInformation')}</h2><p>{t('profile.personalInformationDescription')}</p></div></div>
            <form id="profile-form" onSubmit={saveProfile}>
              <div className="profile-info-list">
                <ProfileField disabled={!canEdit} editing={editing && profile !== null} error={nameError} label={t('profile.fullName')}
                  onChange={displayName => setDraft(current => ({ ...current, displayName }))} onEdit={beginEditing} value={editing && profile ? draft.displayName : profile?.displayName ?? '—'} />
                <ProfileField disabled={!canEdit} editing={editing && profile !== null} error={emailError} label={t('profile.emailAddress')}
                  onChange={email => setDraft(current => ({ ...current, email }))} onEdit={beginEditing} type="email" value={editing && profile ? draft.email : profile?.email ?? '—'} />
                <ProfileField label={t('profile.password')} value={profile ? '••••••••••••' : '—'} />
                <ProfileField label={t('profile.phoneNumber')} value="—" />
                <ProfileField label={t('profile.country')} value="—" />
                <ProfileField label={t('profile.timeZone')} value="—" />
              </div>
              {editing && saveError && <div className="profile-feedback form-error" role="alert"><p>{t(`profile.errors.${saveError}`)}</p>
                {saveError === 'profile_update_conflict' && <button className="cancel-button" onClick={() => { setEditingProfile(null); reload() }} type="button">{t('marketApi.retry')}</button>}
              </div>}
              {editing && profile && <div className="form-actions">
                <button className="cancel-button" disabled={saving} onClick={() => { setEditingProfile(null); setSaved(false) }} type="button">{t('common.cancel')}</button>
                <button className="modal-primary" disabled={!canEdit || !draft.displayName.trim()} type="submit">{t(saving ? 'profile.saving' : 'common.saveChanges')}</button>
              </div>}
              {saved && profile && <p className="profile-saved" role="status">{t('profile.profileSavedToast')}</p>}
            </form>
          </section>
          <section aria-labelledby="preferences-title" className="panel preferences-panel">
            <div className="panel-heading"><div><h2 id="preferences-title">{t('profile.preferences')}</h2><p>{t('profile.preferencesDescription')}</p></div></div>
            <div className="preferences-list">
              <UnavailablePreference description={t('profile.emailNotificationsDescription')} label={t('profile.emailNotifications')} />
              <UnavailablePreference description={t('profile.tradeAlertsDescription')} label={t('profile.tradeAlerts')} />
              <UnavailablePreference description={t('profile.marketingCommunicationsDescription')} label={t('profile.marketingCommunications')} />
              <div className="preference-row select-row"><div><strong>{t('profile.accountCurrency')}</strong><small>{t('profile.accountCurrencyDescription')}</small></div><strong>{capital?.currency ?? '—'}</strong></div>
              <UnavailablePreference description={t('profile.darkModeDescription')} label={t('profile.darkMode')} />
            </div>
          </section>
        </div>
        <section aria-labelledby="security-title" className="panel account-security-panel">
          <div className="panel-heading"><div><h2 id="security-title">{t('profile.accountSecurity')}</h2><p>{t('profile.accountSecurityDescription')}</p></div></div>
          <div className="security-grid">
            <button className="security-item" disabled title={t('businessData.unavailable')} type="button"><span className="security-icon security-green"><Icon name="shield" size={20} /></span><span><strong>{t('profile.twoFactorAuthentication')}</strong><small>{t('profile.twoFactorDescription')}</small></span><span>—</span></button>
            <button className="security-item" disabled title={t('businessData.unavailable')} type="button"><span className="security-icon security-purple"><Icon name="key" size={20} /></span><span><strong>{t('profile.activeSessions')}</strong><small>{t('profile.activeSessionsDescription')}</small></span><span>—</span></button>
          </div>
        </section>
        <p className="simulation-note">{t('profile.unavailableNote')}</p>
      </div>
    </main>
  </div>
}
