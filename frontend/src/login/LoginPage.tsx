import { routeFor } from '../navigation/routes'
import { LanguageSelector } from '../components/LanguageSelector'
import { formatSignedPercent } from '../i18n/formatters'
import { useTranslation } from 'react-i18next'
import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import './login.css'

type FieldErrors = Partial<Record<'email' | 'password', string>>

function Icon({ name }: { name: 'mail' | 'lock' | 'eye' | 'eyeOff' | 'google' }) {
  if (name === 'google') {
    return (
      <svg className="icon google-icon" viewBox="0 0 18 18" aria-hidden="true">
        <path fill="#4285F4" d="M17.64 9.205c0-.638-.057-1.252-.164-1.841H9v3.482h4.844a4.14 4.14 0 0 1-1.797 2.716v2.258h2.908c1.702-1.568 2.685-3.878 2.685-6.615Z" />
        <path fill="#34A853" d="M9 18c2.43 0 4.467-.806 5.955-2.18l-2.908-2.258c-.806.54-1.835.86-3.047.86-2.345 0-4.332-1.584-5.043-3.716H.951v2.332A9 9 0 0 0 9 18Z" />
        <path fill="#FBBC05" d="M3.957 10.706A5.41 5.41 0 0 1 3.674 9c0-.592.102-1.167.283-1.706V4.962H.951A9 9 0 0 0 0 9c0 1.452.348 2.826.951 4.038l3.006-2.332Z" />
        <path fill="#EA4335" d="M9 3.578c1.323 0 2.51.455 3.445 1.348l2.584-2.584C13.463.892 11.426 0 9 0A9 9 0 0 0 .951 4.962l3.006 2.332C4.668 5.162 6.655 3.578 9 3.578Z" />
      </svg>
    )
  }

  const paths = {
    mail: <><rect x="3.5" y="5" width="17" height="14" rx="2" /><path d="m4.5 7 7.5 5.5L19.5 7" /></>,
    lock: <><rect x="4.5" y="10" width="15" height="11" rx="2" /><path d="M8 10V7.5a4 4 0 0 1 8 0V10" /></>,
    eye: <><path d="M2.5 12s3.3-5 9.5-5 9.5 5 9.5 5-3.3 5-9.5 5-9.5-5-9.5-5Z" /><circle cx="12" cy="12" r="2.2" /></>,
    eyeOff: <><path d="m3 3 18 18M10.58 10.58a2 2 0 0 0 2.83 2.83M9.88 5.1A10.5 10.5 0 0 1 12 4.88c6.2 0 9.5 5.12 9.5 5.12a17.5 17.5 0 0 1-3.1 3.6M6.61 6.61C3.67 8.3 2.5 10 2.5 10s3.3 5.12 9.5 5.12a9.7 9.7 0 0 0 3.28-.56" /></>,
  }

  return <svg className="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}

function Brand() {
  return <div className="brand" aria-label="StockLab"><span className="brand-mark" aria-hidden="true"><i /><i /><i /></span><span>Stock<span>Lab</span></span></div>
}

export default function LoginPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('login.title')} | StockLab`
  }, [i18n.language, t])
  const [showPassword, setShowPassword] = useState(false)
  const [errors, setErrors] = useState<FieldErrors>({})
  const [submitted, setSubmitted] = useState(false)

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const email = String(form.get('email') ?? '').trim()
    const password = String(form.get('password') ?? '')
    const nextErrors: FieldErrors = {}

    if (!email) nextErrors.email = 'login.errors.email'
    else if (!/^\S+@\S+\.\S+$/.test(email)) nextErrors.email = 'login.errors.emailInvalid'
    if (!password) nextErrors.password = 'login.errors.password'

    setErrors(nextErrors)
    setSubmitted(Object.keys(nextErrors).length === 0)
  }

  return (
    <main className="login-page">
      <div className="page-frame">
        <div className="market-decoration market-decoration-left" aria-hidden="true">
          <div className="quote-card quote-aapl"><strong>AAPL</strong><span>↗ {formatSignedPercent(1.35)}</span></div>
          <div className="quote-card quote-tsla"><strong>TSLA</strong><span>↘ {formatSignedPercent(-0.41)}</span></div>
        </div>
        <div className="market-decoration market-decoration-right" aria-hidden="true">
          <div className="quote-card quote-msft"><strong>MSFT</strong><span>↗ {formatSignedPercent(0.82)}</span></div>
          <div className="quote-card quote-nvda"><strong>NVDA</strong><span>↗ {formatSignedPercent(2.18)}</span></div>
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
            <Brand />
            <LanguageSelector />
          </div>
          <header className="card-heading"><h1 id="login-heading">{t('login.title')}</h1><p>{t('login.subtitle')}</p></header>
          <form className="login-form" onSubmit={handleSubmit} noValidate>
            <div className="field-group"><label htmlFor="email">{t('login.emailLabel')}</label><div className={`input-wrap ${errors.email ? 'has-error' : ''}`}><Icon name="mail" /><input id="email" name="email" type="email" placeholder={t('login.emailPlaceholder')} autoComplete="email" aria-invalid={Boolean(errors.email)} /></div>{errors.email && <p className="field-error">{t(errors.email)}</p>}</div>
            <div className="field-group"><div className="label-row"><label htmlFor="password">{t('login.passwordLabel')}</label><a href="#forgot-password">{t('login.forgot')}</a></div><div className={`input-wrap ${errors.password ? 'has-error' : ''}`}><Icon name="lock" /><input id="password" name="password" type={showPassword ? 'text' : 'password'} placeholder={t('login.passwordPlaceholder')} autoComplete="current-password" aria-invalid={Boolean(errors.password)} /><button className="visibility-button" type="button" aria-label={showPassword ? t('login.hidePassword') : t('login.showPassword')} onClick={() => setShowPassword((visible) => !visible)}><Icon name={showPassword ? 'eyeOff' : 'eye'} /></button></div>{errors.password && <p className="field-error">{t(errors.password)}</p>}</div>
            <button className="primary-button" type="submit">{t('login.signIn')}</button>
            {submitted && <p className="form-success" role="status">{t('login.success')}</p>}
          </form>
          <div className="form-divider"><span>{t('login.divider')}</span></div>
          <button className="google-button" type="button"><Icon name="google" /><span>{t('login.google')}</span></button>
          <p className="register-prompt">{t('login.prompt')} <a href={routeFor('register')}>{t('login.register')}</a></p>
        </section>
      </div>
    </main>
  )
}
