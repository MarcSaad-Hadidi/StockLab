import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { passwordRecoveryApi } from '../api/passwordRecoveryApi'
import { clearAuthSession, getAuthSession } from '../auth/authStorage'
import { routeFor } from '../navigation/routes'
import { RecoveryLayout } from './RecoveryLayout'
import { RecoveryFeedback } from './RecoveryFeedback'
import { useRecoveryForm, type RecoveryFieldErrors } from './useRecoveryForm'
import { consumeResetLink, type ResetLink } from './resetLink'

export function ResetPasswordEntry({ initialLink }: { initialLink: ResetLink | null }) {
  const [current, setCurrent] = useState({ link: initialLink, revision: 0 })
  useEffect(() => {
    const followLink = () => {
      if (!window.location.hash) return
      const link = consumeResetLink()
      // A numeric key discards the previous draft and any pending form result.
      setCurrent(previous => ({ link, revision: previous.revision + 1 }))
    }
    window.addEventListener('hashchange', followLink)
    return () => window.removeEventListener('hashchange', followLink)
  }, [])
  return <ResetPasswordPage key={current.revision} initialLink={current.link} />
}

export default function ResetPasswordPage({ initialLink }: { initialLink: ResetLink | null }) {
  const { t } = useTranslation()
  const [link, setLink] = useState(initialLink)
  const [complete, setComplete] = useState(false)
  const active = useRef(false)
  useEffect(() => {
    active.current = true
    return () => { active.current = false }
  }, [])
  const form = useRecoveryForm()
  const invalid = !link || form.failure === 'invalid_reset_token'
  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!link) return
    const values = new FormData(event.currentTarget)
    const newPassword = String(values.get('newPassword') ?? '')
    const confirmPassword = String(values.get('confirmPassword') ?? '')
    const validation: RecoveryFieldErrors = {}
    if (!newPassword.trim()) validation.newPassword = 'recovery.errors.passwordRequired'
    else if (newPassword.length < 8) validation.newPassword = 'recovery.errors.passwordShort'
    else if (newPassword.length > 1024) validation.newPassword = 'recovery.errors.passwordLong'
    if (!confirmPassword) validation.confirmPassword = 'recovery.errors.confirmRequired'
    else if (newPassword !== confirmPassword) validation.confirmPassword = 'recovery.errors.mismatch'
    void form.submit(async () => {
      const submittedSession = getAuthSession()
      await passwordRecoveryApi.resetPassword({ ...link, newPassword, confirmPassword })
      if (!active.current) return
      const currentSession = getAuthSession()
      if (submittedSession?.user.id.toLowerCase() === link.userId.toLowerCase()
        && currentSession?.user.id.toLowerCase() === link.userId.toLowerCase()
        && currentSession.accessToken === submittedSession.accessToken) clearAuthSession()
      setComplete(true)
      setLink(null)
    }, validation)
  }
  return <RecoveryLayout title="recovery.reset.title" subtitle="recovery.reset.subtitle" busy={form.busy}>
    {complete ? <p className="form-success" role="status">{t('recovery.reset.success')}</p>
      : invalid ? <><p className="form-error" role="alert">{t('recovery.errors.invalidLink')}</p><p className="recovery-resend"><a href={routeFor('forgot-password')}>{t('recovery.requestNewLink')}</a></p></>
        : <form className="login-form" noValidate onSubmit={handleSubmit} aria-busy={form.busy}>
          <p className="recovery-hint">{t('recovery.reset.googleRecoveryNotice')}</p>
          <RecoveryFeedback failure={form.failure} retrySeconds={form.retrySeconds} />
          {(['newPassword', 'confirmPassword'] as const).map(field => <div className="field-group" key={field}>
            <label htmlFor={field}>{t(`recovery.${field}Label`)}</label>
            <div className={`input-wrap ${form.errors[field] ? 'has-error' : ''}`}><input id={field} name={field} type="password" autoComplete="new-password" disabled={form.busy} aria-invalid={Boolean(form.errors[field])} aria-describedby={form.errors[field] ? `${field}-error` : field === 'newPassword' ? 'password-hint' : undefined} onChange={() => form.clearField(field)} /></div>
            {field === 'newPassword' && <p className="recovery-hint" id="password-hint">{t('recovery.passwordHint')}</p>}
            {form.errors[field] && <p className="field-error" id={`${field}-error`}>{t(form.errors[field])}</p>}
          </div>)}
          <button className="primary-button" type="submit" disabled={form.busy || form.retrySeconds > 0} aria-describedby={form.retrySeconds > 0 ? 'recovery-retry-status' : undefined}>{t(form.busy ? 'recovery.submitting' : 'recovery.reset.submit')}</button>
        </form>}
  </RecoveryLayout>
}
