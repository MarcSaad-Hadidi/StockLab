import { routeFor } from '../navigation/routes'
import { AuthBrand } from '../components/AuthBrand'
import { LanguageSelector } from '../components/LanguageSelector'
import { formatSignedPercent } from '../i18n/formatters'
import { AuthApiError, authApi } from '../api/authApi'
import { completeLogin, LoginCompletionError } from '../auth/completeLogin'
import { isSupersededLanguageChange } from '../i18n/languagePreference'
import { GoogleAuthButton } from '../components/GoogleAuthButton'
import { ProfileApiError } from '../api/profileApi'
import { useTranslation } from 'react-i18next'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { ChangeEvent, FormEvent } from 'react'
import './login.css'

type FieldErrors = Partial<Record<'email' | 'password', string>>
type ValidationState = { errors: FieldErrors; serverError?: string }

function withoutFieldErrors({ errors, serverError }: ValidationState, fields: (keyof FieldErrors)[]): ValidationState {
  const remaining = { ...errors }
  const removedError = fields.some(field => Boolean(errors[field]))
  for (const field of fields) delete remaining[field]
  return {
    errors: remaining,
    serverError: fields.length > 0 && (serverError === 'login.errors.invalidCredentials'
      || serverError === 'login.errors.invalidRequest' && removedError && Object.keys(remaining).length === 0)
      ? undefined : serverError,
  }
}

function loginErrorKey(code: AuthApiError['code']): string {
  return ({
    validation_error: 'login.errors.invalidRequest',
    email_already_registered: 'login.errors.server',
    invalid_credentials: 'login.errors.invalidCredentials',
    rate_limited: 'login.errors.rateLimited',
    offline: 'login.errors.offline',
    invalid_response: 'login.errors.server',
    server_error: 'login.errors.server',
  } satisfies Record<AuthApiError['code'], string>)[code]
}

function serverFieldErrors(fieldErrors: Record<string, string[]>): FieldErrors {
  const mapped: FieldErrors = {}
  for (const field of Object.keys(fieldErrors)) {
    const normalized = field.toLowerCase()
    if (normalized === 'email') mapped.email = 'login.errors.email'
    if (normalized === 'password') mapped.password = 'login.errors.password'
  }
  return mapped
}

function Icon({ name }: { name: 'mail' | 'lock' | 'eye' | 'eyeOff' }) {
  const paths = {
    mail: <><rect x="3.5" y="5" width="17" height="14" rx="2" /><path d="m4.5 7 7.5 5.5L19.5 7" /></>,
    lock: <><rect x="4.5" y="10" width="15" height="11" rx="2" /><path d="M8 10V7.5a4 4 0 0 1 8 0V10" /></>,
    eye: <><path d="M2.5 12s3.3-5 9.5-5 9.5 5 9.5 5-3.3 5-9.5 5-9.5-5-9.5-5Z" /><circle cx="12" cy="12" r="2.2" /></>,
    eyeOff: <><path d="m3 3 18 18M10.58 10.58a2 2 0 0 0 2.83 2.83M9.88 5.1A10.5 10.5 0 0 1 12 4.88c6.2 0 9.5 5.12 9.5 5.12a17.5 17.5 0 0 1-3.1 3.6M6.61 6.61C3.67 8.3 2.5 10 2.5 10s3.3 5.12 9.5 5.12a9.7 9.7 0 0 0 3.28-.56" /></>,
  }

  return <svg className="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}

export default function LoginPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('login.title')} | StockLab`
  }, [i18n.language, t])
  const [showPassword, setShowPassword] = useState(false)
  const [{ errors, serverError }, setValidation] = useState<ValidationState>({ errors: {} })
  const [isSubmitting, setIsSubmitting] = useState(false)
  const submitting = useRef(false)
  const [googleBusy, setGoogleBusy] = useState(false)
  const googleActive = useRef(false)
  const [retryUntil, setRetryUntil] = useState<number | null>(null)
  const [retrySeconds, setRetrySeconds] = useState(0)
  const [registrationComplete, setRegistrationComplete] = useState(() =>
    typeof window !== 'undefined' && new URLSearchParams(window.location.search).get('registered') === '1')

  const handleGoogleBusy = useCallback((busy: boolean) => {
    googleActive.current = busy
    setGoogleBusy(busy)
  }, [])
  const handleGoogleStart = useCallback(() => {
    if (submitting.current) return false
    setRegistrationComplete(false)
    setValidation({ errors: {} })
    return true
  }, [])

  function handleFieldChange(event: ChangeEvent<HTMLInputElement>) {
    const field = event.currentTarget.name as keyof FieldErrors
    setValidation(current => withoutFieldErrors(current, [field]))
  }

  useEffect(() => {
    if (retryUntil === null) return
    const update = () => {
      const seconds = Math.max(0, Math.ceil((retryUntil - Date.now()) / 1000))
      setRetrySeconds(seconds)
      if (seconds === 0) setRetryUntil(null)
    }
    const timer = window.setInterval(update, 1000)
    window.addEventListener('focus', update)
    document.addEventListener('visibilitychange', update)
    return () => {
      window.clearInterval(timer)
      window.removeEventListener('focus', update)
      document.removeEventListener('visibilitychange', update)
    }
  }, [retryUntil])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitting.current || googleActive.current || (retryUntil !== null && Date.now() < retryUntil)) return
    const formElement = event.currentTarget
    const form = new FormData(formElement)
    const email = String(form.get('email') ?? '').trim()
    const password = String(form.get('password') ?? '')
    const nextErrors: FieldErrors = {}

    setRegistrationComplete(false)
    if (!email) nextErrors.email = 'login.errors.email'
    else if (!/^\S+@\S+\.\S+$/.test(email)) nextErrors.email = 'login.errors.emailInvalid'
    if (!password) nextErrors.password = 'login.errors.password'

    setValidation({ errors: nextErrors })
    if (Object.keys(nextErrors).length > 0) return

    submitting.current = true
    setIsSubmitting(true)
    setRetryUntil(null)
    setRetrySeconds(0)
    try {
      const session = await authApi.login({ email, password })
      await completeLogin(session)
    } catch (error) {
      if (isSupersededLanguageChange(error)) return
      if (error instanceof LoginCompletionError) setValidation({ errors: {}, serverError: error.translationKey })
      else if (error instanceof ProfileApiError) setValidation({ errors: {}, serverError: 'login.errors.languageSave' })
      else if (error instanceof AuthApiError) {
        const current = new FormData(formElement)
        const changed = (['email', 'password'] as const).filter(field =>
          (field === 'email' ? String(current.get(field) ?? '').trim() : String(current.get(field) ?? ''))
            !== (field === 'email' ? email : password))
        // Errors belong to the submitted values, even if the user has edited the form meanwhile.
        setValidation(withoutFieldErrors({ errors: serverFieldErrors(error.fieldErrors), serverError: loginErrorKey(error.code) }, changed))
        if (error.code === 'rate_limited') {
          // A legacy/proxy response without a hint uses the default one-minute window.
          const seconds = error.retryAfterSeconds ?? 60
          setRetrySeconds(seconds)
          setRetryUntil(seconds > 0 ? Date.now() + seconds * 1000 : null)
        }
      } else {
        setValidation({ errors: {}, serverError: 'login.errors.server' })
      }
    } finally {
      submitting.current = false
      setIsSubmitting(false)
    }
  }

  return (
    <main className="login-page">
      <div className="page-frame">
        {/* Static design samples, not quotes from a market-data provider. */}
        <div className="market-decoration market-decoration-left" aria-hidden="true">
          <div className="quote-card quote-aapl"><strong>AAPL</strong><span>↗ {formatSignedPercent(1.35)}</span><small>{t('login.illustrativeMarketData')}</small></div>
          <div className="quote-card quote-tsla"><strong>TSLA</strong><span>↘ {formatSignedPercent(-0.41)}</span><small>{t('login.illustrativeMarketData')}</small></div>
        </div>
        <div className="market-decoration market-decoration-right" aria-hidden="true">
          <div className="quote-card quote-msft"><strong>MSFT</strong><span>↗ {formatSignedPercent(0.82)}</span><small>{t('login.illustrativeMarketData')}</small></div>
          <div className="quote-card quote-nvda"><strong>NVDA</strong><span>↗ {formatSignedPercent(2.18)}</span><small>{t('login.illustrativeMarketData')}</small></div>
        </div>
        <svg className="background-chart" viewBox="0 0 560 330" preserveAspectRatio="none" aria-hidden="true">
          <defs>
            <pattern id="login-chart-grid" width="24" height="24" patternUnits="userSpaceOnUse"><path d="M24 0H0V24" fill="none" stroke="#dbe2ff" strokeWidth="1" opacity=".55" /></pattern>
            <linearGradient id="login-chart-fill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#aab8ff" stopOpacity=".2" /><stop offset="1" stopColor="#aab8ff" stopOpacity="0" /></linearGradient>
          </defs>
          <rect width="560" height="330" fill="url(#login-chart-grid)" opacity=".55" />
          <path d="M0 288 C55 270 76 235 113 246 S174 318 210 278 S258 216 293 229 S339 266 370 214 S419 132 453 143 S509 92 560 41 V330 H0Z" fill="url(#login-chart-fill)" />
          <path d="M0 288 C55 270 76 235 113 246 S174 318 210 278 S258 216 293 229 S339 266 370 214 S419 132 453 143 S509 92 560 41" fill="none" stroke="#91a3ff" strokeWidth="2.4" strokeLinecap="round" />
          <g fill="#c2ceff"><rect x="16" y="277" width="10" height="53" rx="2" /><rect x="36" y="250" width="10" height="80" rx="2" /><rect x="56" y="216" width="10" height="114" rx="2" /><rect x="76" y="239" width="10" height="91" rx="2" /><rect x="96" y="271" width="10" height="59" rx="2" /><rect x="385" y="211" width="10" height="119" rx="2" /><rect x="405" y="183" width="10" height="147" rx="2" /><rect x="425" y="157" width="10" height="173" rx="2" /><rect x="445" y="133" width="10" height="197" rx="2" /><rect x="465" y="107" width="10" height="223" rx="2" /><rect x="485" y="76" width="10" height="254" rx="2" /></g>
        </svg>

        <section className="login-card" aria-labelledby="login-heading">
          <div className="card-topline">
            <AuthBrand />
            <LanguageSelector persistToAccount={false} disabled={isSubmitting || googleBusy} />
          </div>
          <header className="card-heading"><h1 id="login-heading">{t('login.title')}</h1><p>{t('login.subtitle')}</p></header>
          {registrationComplete && <p className="form-success" role="status">{t('login.registrationSuccess')}</p>}
          <form aria-busy={isSubmitting} className="login-form" onSubmit={handleSubmit} noValidate>
            {serverError && (serverError === 'login.errors.rateLimited'
              ? retrySeconds > 0
                ? <p className="form-error" id="login-retry-status" role="alert">{t(serverError)}{' '}<span role="timer" aria-live="off">{t('login.retryCountdown', { count: retrySeconds })}</span></p>
                : <p className="form-success" role="status">{t('login.retryReady')}</p>
              : <p className="form-error" role="alert">{t(serverError)}</p>)}
            <div className="field-group"><label htmlFor="email">{t('login.emailLabel')}</label><div className={`input-wrap ${errors.email ? 'has-error' : ''}`}><Icon name="mail" /><input id="email" name="email" type="email" placeholder={t('login.emailPlaceholder')} autoComplete="email" aria-invalid={Boolean(errors.email)} onChange={handleFieldChange} /></div>{errors.email && <p className="field-error">{t(errors.email)}</p>}</div>
            <div className="field-group"><div className="label-row"><label htmlFor="password">{t('login.passwordLabel')}</label><a href={routeFor('forgot-password')}>{t('login.forgot')}</a></div><div className={`input-wrap ${errors.password ? 'has-error' : ''}`}><Icon name="lock" /><input id="password" name="password" type={showPassword ? 'text' : 'password'} placeholder={t('login.passwordPlaceholder')} autoComplete="current-password" aria-invalid={Boolean(errors.password)} onChange={handleFieldChange} /><button className="visibility-button" type="button" aria-label={showPassword ? t('login.hidePassword') : t('login.showPassword')} onClick={() => setShowPassword((visible) => !visible)}><Icon name={showPassword ? 'eyeOff' : 'eye'} /></button></div>{errors.password && <p className="field-error">{t(errors.password)}</p>}</div>
            <button className="primary-button" disabled={isSubmitting || googleBusy || retrySeconds > 0} aria-describedby={retrySeconds > 0 ? 'login-retry-status' : undefined} type="submit">{t('login.signIn')}</button>
          </form>
          <div className="form-divider"><span>{t('login.divider')}</span></div>
          <GoogleAuthButton disabled={isSubmitting} onBusyChange={handleGoogleBusy} onStart={handleGoogleStart} />
          <p className="register-prompt">{t('login.prompt')} <a href={routeFor('register')}>{t('login.register')}</a></p>
        </section>
      </div>
    </main>
  )
}
