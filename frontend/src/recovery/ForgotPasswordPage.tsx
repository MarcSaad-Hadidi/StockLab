import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { passwordRecoveryApi } from '../api/passwordRecoveryApi'
import { isSupportedLanguage } from '../i18n/languages'
import { RecoveryLayout } from './RecoveryLayout'
import { RecoveryFeedback } from './RecoveryFeedback'
import { useRecoveryForm, type RecoveryFieldErrors } from './useRecoveryForm'

export default function ForgotPasswordPage() {
  const { t, i18n } = useTranslation()
  const [accepted, setAccepted] = useState(false)
  const form = useRecoveryForm()
  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const email = String(new FormData(event.currentTarget).get('email') ?? '').trim()
    const validation: RecoveryFieldErrors = !email ? { email: 'recovery.errors.emailRequired' }
      : !/^\S+@\S+\.\S+$/.test(email) ? { email: 'recovery.errors.emailInvalid' } : {}
    void form.submit(async () => {
      const language = isSupportedLanguage(i18n.resolvedLanguage) ? i18n.resolvedLanguage : 'en'
      await passwordRecoveryApi.forgotPassword({ email, language })
      setAccepted(true)
    }, validation)
  }
  return <RecoveryLayout title="recovery.forgot.title" subtitle="recovery.forgot.subtitle" busy={form.busy}>
    {accepted ? <p className="form-success" role="status">{t('recovery.forgot.accepted')}</p>
      : <form className="login-form" noValidate onSubmit={handleSubmit} aria-busy={form.busy}>
        <RecoveryFeedback failure={form.failure} retrySeconds={form.retrySeconds} />
        <div className="field-group"><label htmlFor="email">{t('recovery.emailLabel')}</label>
          <div className={`input-wrap ${form.errors.email ? 'has-error' : ''}`}><input id="email" name="email" type="email" autoComplete="email" placeholder={t('login.emailPlaceholder')} disabled={form.busy} aria-invalid={Boolean(form.errors.email)} aria-describedby={form.errors.email ? 'email-error' : undefined} onChange={() => form.clearField('email')} /></div>
          {form.errors.email && <p className="field-error" id="email-error">{t(form.errors.email)}</p>}
        </div>
        <button className="primary-button" type="submit" disabled={form.busy || form.retrySeconds > 0} aria-describedby={form.retrySeconds > 0 ? 'recovery-retry-status' : undefined}>{t(form.busy ? 'recovery.submitting' : 'recovery.forgot.submit')}</button>
      </form>}
  </RecoveryLayout>
}
