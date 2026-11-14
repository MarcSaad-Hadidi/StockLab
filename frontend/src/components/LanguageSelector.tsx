import { useTranslation } from 'react-i18next'
import { isSupportedLanguage, setLanguage, supportedLanguages, type Language } from '../i18n/i18n'
import './LanguageSelector.css'
import { useEffect, useEffectEvent, useState } from 'react'
import { getAuthSession, saveAuthSession } from '../auth/authStorage'
import { useAuthUser } from '../auth/useAuthUser'
import { chooseLoginLanguage, getPendingLanguageIntent, isLanguageSaving, isSupersededLanguageChange, saveLanguagePreference, useLanguageSaving, type LanguageIntent } from '../i18n/languagePreference'
import { ProfileApiError } from '../api/profileApi'
import type { AuthSession } from '../auth/authStorage'

type LanguageSelectorProps = {
  className?: string
  persistToAccount?: boolean
  disabled?: boolean
}

const languageLabels: Record<Language, string> = {
  en: 'English',
  fr: 'Français',
}

export function LanguageSelector({ className = '', persistToAccount = true, disabled = false }: LanguageSelectorProps) {
  const { i18n, t } = useTranslation()
  const user = useAuthUser()
  const saving = useLanguageSaving(persistToAccount ? user?.id : undefined)
  const [failure, setFailure] = useState<{ userId: string; kind: 'save' | 'storage' | 'session' } | null>(null)
  const currentLanguage = isSupportedLanguage(i18n.resolvedLanguage) ? i18n.resolvedLanguage : 'en'

  async function choose(language: Language) {
    if (disabled) return
    const session = persistToAccount ? getAuthSession() : null
    if (!session) {
      chooseLoginLanguage(language)
      await setLanguage(language)
      return
    }
    if (isLanguageSaving(session.user.id)) return
    await persist(language, session)
  }

  async function persist(language: Language, session: AuthSession, resume?: LanguageIntent) {
    setFailure(null)
    const request = saveLanguagePreference(language, session, undefined, false, resume)
    // The explicit browser choice survives navigation even while persistence is pending.
    void setLanguage(language)
    try {
      const result = await request
      const current = getAuthSession()
      if (current?.user.id !== session.user.id) return
      if (!saveAuthSession({ ...current, user: { ...current.user, preferredLanguage: result.preferredLanguage } })) {
        setFailure({ userId: session.user.id, kind: 'storage' })
      }
    } catch (error) {
      if (isSupersededLanguageChange(error)) return
      const current = getAuthSession()
      if (current?.user.id === session.user.id) setFailure({ userId: session.user.id, kind: 'save' })
      else if (!current && error instanceof ProfileApiError && error.code === 'unauthorized') setFailure({ userId: session.user.id, kind: 'session' })
    }
  }

  const resumeChoice = useEffectEvent(() => {
    const session = getAuthSession()
    if (!session || isLanguageSaving(session.user.id)) return
    const intent = getPendingLanguageIntent(session.user.id)
    if (intent) void persist(intent.language, session, intent)
  })
  useEffect(() => { if (persistToAccount) resumeChoice() }, [user?.id, persistToAccount])
  const showFailure = failure && (failure.userId === user?.id || !user && failure.kind !== 'save')

  return (
    <div className="language-control"><div aria-busy={saving || undefined} aria-label={t('common.languageLabel')} className={`language-switch ${className}`.trim()} role="group">
      {supportedLanguages.map((language) => (
        <button
          aria-pressed={currentLanguage === language}
          key={language}
          disabled={disabled || saving}
          onClick={() => { void choose(language) }}
          title={languageLabels[language]}
          type="button"
        >
          {language.toUpperCase()}
        </button>
      ))}
    </div>{showFailure && <p className="language-save-error" role="alert">
      {t(failure.kind === 'storage' ? 'common.languageSessionStorageError' : failure.kind === 'session' ? 'common.languageSessionExpired' : 'common.languageSaveError')}
      {user && <button type="button" disabled={saving} onClick={() => { void choose(currentLanguage) }}>{t('watchlist.retry')}</button>}
    </p>}</div>
  )
}
