import { routeFor } from '../navigation/routes'
import { useState } from 'react'
import type { FormEvent } from 'react'
import './register.css'

type FieldErrors = Partial<Record<'name' | 'email' | 'password' | 'confirmPassword', string>>
type Language = 'fr' | 'en'

function Icon({ name }: { name: 'user' | 'mail' | 'lock' | 'eye' | 'eyeOff' | 'gift' | 'google' }) {
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
    user: <><circle cx="12" cy="8" r="3.25" /><path d="M5.5 20a6.5 6.5 0 0 1 13 0" /></>,
    mail: <><rect x="3.5" y="5" width="17" height="14" rx="2" /><path d="m4.5 7 7.5 5.5L19.5 7" /></>,
    lock: <><rect x="4.5" y="10" width="15" height="11" rx="2" /><path d="M8 10V7.5a4 4 0 0 1 8 0V10" /></>,
    eye: <><path d="M2.5 12s3.3-5 9.5-5 9.5 5 9.5 5-3.3 5-9.5 5-9.5-5-9.5-5Z" /><circle cx="12" cy="12" r="2.2" /></>,
    eyeOff: <><path d="m3 3 18 18M10.58 10.58a2 2 0 0 0 2.83 2.83M9.88 5.1A10.5 10.5 0 0 1 12 4.88c6.2 0 9.5 5.12 9.5 5.12a17.5 17.5 0 0 1-3.1 3.6M6.61 6.61C3.67 8.3 2.5 10 2.5 10s3.3 5.12 9.5 5.12a9.7 9.7 0 0 0 3.28-.56" /></>,
    gift: <><path d="M4 10h16v10H4zM3 6.5h18v3.5H3z" /><path d="M12 6.5V20M12 6.5H8.75a2.25 2.25 0 1 1 2.25-2.25V6.5ZM12 6.5h3.25a2.25 2.25 0 1 0-2.25-2.25V6.5Z" /></>,
  }

  return <svg className="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}

function Brand() {
  return <div className="brand" aria-label="StockLab"><span className="brand-mark" aria-hidden="true"><i /><i /><i /></span><span>Stock<span>Lab</span></span></div>
}

export default function RegisterPage() {
  const [language, setLanguage] = useState<Language>('fr')
  const [showPassword, setShowPassword] = useState(false)
  const [showConfirmation, setShowConfirmation] = useState(false)
  const [errors, setErrors] = useState<FieldErrors>({})
  const [submitted, setSubmitted] = useState(false)
  const copy = language === 'fr' ? {
    title: 'Créez votre compte', subtitle: 'Rejoignez StockLab et commencez à investir', nameLabel: 'Nom complet', namePlaceholder: 'Entrez votre nom complet', emailLabel: 'Adresse e-mail', emailPlaceholder: 'vous@exemple.com', passwordLabel: 'Mot de passe', passwordPlaceholder: 'Créez un mot de passe', confirmLabel: 'Confirmer le mot de passe', confirmPlaceholder: 'Confirmez votre mot de passe', capitalTitle: 'Capital virtuel offert', capitalLine: 'Commencez avec 100 000 $ pour vos simulations.', create: 'Créer un compte', divider: 'ou continuer avec', google: 'S’inscrire avec Google', loginPrompt: 'Vous avez déjà un compte ?', loginLink: 'Se connecter', success: 'Vos informations sont prêtes à être envoyées.', errors: { name: 'Veuillez saisir votre nom complet.', email: 'Veuillez saisir votre adresse e-mail.', emailInvalid: 'Veuillez saisir une adresse valide.', password: 'Créez un mot de passe.', passwordShort: '8 caractères minimum.', confirm: 'Confirmez votre mot de passe.', mismatch: 'Les mots de passe diffèrent.' }, showPassword: 'Afficher le mot de passe', hidePassword: 'Masquer le mot de passe', showConfirmation: 'Afficher la confirmation', hideConfirmation: 'Masquer la confirmation', languageLabel: 'Choisir la langue',
  } : {
    title: 'Create your account', subtitle: 'Join StockLab and start your investing journey', nameLabel: 'Full name', namePlaceholder: 'Enter your full name', emailLabel: 'Email address', emailPlaceholder: 'you@example.com', passwordLabel: 'Password', passwordPlaceholder: 'Create a password', confirmLabel: 'Confirm password', confirmPlaceholder: 'Confirm your password', capitalTitle: 'Virtual capital included', capitalLine: 'Start with $100,000 for paper trading.', create: 'Create account', divider: 'or continue with', google: 'Sign up with Google', loginPrompt: 'Already have an account?', loginLink: 'Sign in', success: 'Your details are ready to submit.', errors: { name: 'Please enter your full name.', email: 'Please enter your email address.', emailInvalid: 'Please enter a valid email address.', password: 'Please create a password.', passwordShort: 'Use at least 8 characters.', confirm: 'Please confirm your password.', mismatch: 'Passwords do not match.' }, showPassword: 'Show password', hidePassword: 'Hide password', showConfirmation: 'Show confirmation', hideConfirmation: 'Hide confirmation', languageLabel: 'Choose language',
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const name = String(form.get('name') ?? '').trim()
    const email = String(form.get('email') ?? '').trim()
    const password = String(form.get('password') ?? '')
    const confirmPassword = String(form.get('confirmPassword') ?? '')
    const nextErrors: FieldErrors = {}

    if (!name) nextErrors.name = copy.errors.name
    if (!email) nextErrors.email = copy.errors.email
    else if (!/^\S+@\S+\.\S+$/.test(email)) nextErrors.email = copy.errors.emailInvalid
    if (!password) nextErrors.password = copy.errors.password
    else if (password.length < 8) nextErrors.password = copy.errors.passwordShort
    if (!confirmPassword) nextErrors.confirmPassword = copy.errors.confirm
    else if (confirmPassword !== password) nextErrors.confirmPassword = copy.errors.mismatch

    setErrors(nextErrors)
    setSubmitted(Object.keys(nextErrors).length === 0)
  }

  return (
    <main className="register-page">
      <div className="page-frame">
        <div className="market-decoration market-decoration-left" aria-hidden="true"><span className="decoration-tile tile-pie"><span /></span><span className="decoration-orb orb-dollar">$</span></div>
        <div className="market-decoration market-decoration-right" aria-hidden="true"><span className="decoration-tile tile-trend"><i /><i /><i /><i /></span><span className="decoration-tile tile-bars"><i /><i /><i /><i /></span></div>
        <svg className="background-chart" viewBox="0 0 430 250" preserveAspectRatio="none" aria-hidden="true">
          <defs>
            <pattern id="chart-grid" width="24" height="24" patternUnits="userSpaceOnUse"><path d="M24 0H0V24" fill="none" stroke="#dbe2ff" strokeWidth="1" opacity=".55" /></pattern>
            <linearGradient id="chart-fill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#aab8ff" stopOpacity=".2" /><stop offset="1" stopColor="#aab8ff" stopOpacity="0" /></linearGradient>
          </defs>
          <rect width="430" height="250" fill="url(#chart-grid)" opacity=".55" />
          <path d="M0 183 L24 177 L42 184 L63 160 L79 173 L98 148 L115 159 L130 138 L149 146 L168 120 L184 127 L204 96 L223 104 L239 80 L254 94 L271 63 L286 76 L306 49 L322 61 L337 35 L353 48 L372 21 L389 35 L410 5 L430 12 V250 H0Z" fill="url(#chart-fill)" />
          <polyline points="0,183 24,177 42,184 63,160 79,173 98,148 115,159 130,138 149,146 168,120 184,127 204,96 223,104 239,80 254,94 271,63 286,76 306,49 322,61 337,35 353,48 372,21 389,35 410,5 430,12" fill="none" stroke="#91a3ff" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
          <g fill="#c2ceff"><rect x="190" y="218" width="8" height="32" rx="2" /><rect x="206" y="202" width="8" height="48" rx="2" /><rect x="222" y="185" width="8" height="65" rx="2" /><rect x="238" y="164" width="8" height="86" rx="2" /><rect x="254" y="144" width="8" height="106" rx="2" /><rect x="270" y="125" width="8" height="125" rx="2" /><rect x="286" y="103" width="8" height="147" rx="2" /></g>
        </svg>

        <section className="register-card" aria-labelledby="register-heading">
          <div className="card-topline"><Brand /><div className="language-switch" role="group" aria-label={copy.languageLabel}>
            <button type="button" className={language === 'fr' ? 'active' : ''} aria-pressed={language === 'fr'} onClick={() => setLanguage('fr')}>FR</button>
            <button type="button" className={language === 'en' ? 'active' : ''} aria-pressed={language === 'en'} onClick={() => setLanguage('en')}>EN</button>
          </div></div>
          <header className="card-heading"><h1 id="register-heading">{copy.title}</h1><p>{copy.subtitle}</p></header>
          <form className="register-form" onSubmit={handleSubmit} noValidate>
            <div className="field-group"><label htmlFor="name">{copy.nameLabel}</label><div className={`input-wrap ${errors.name ? 'has-error' : ''}`}><Icon name="user" /><input id="name" name="name" type="text" placeholder={copy.namePlaceholder} autoComplete="name" aria-invalid={Boolean(errors.name)} /></div>{errors.name && <p className="field-error">{errors.name}</p>}</div>
            <div className="field-group"><label htmlFor="email">{copy.emailLabel}</label><div className={`input-wrap ${errors.email ? 'has-error' : ''}`}><Icon name="mail" /><input id="email" name="email" type="email" placeholder={copy.emailPlaceholder} autoComplete="email" aria-invalid={Boolean(errors.email)} /></div>{errors.email && <p className="field-error">{errors.email}</p>}</div>
            <div className="field-group"><label htmlFor="password">{copy.passwordLabel}</label><div className={`input-wrap ${errors.password ? 'has-error' : ''}`}><Icon name="lock" /><input id="password" name="password" type={showPassword ? 'text' : 'password'} placeholder={copy.passwordPlaceholder} autoComplete="new-password" aria-invalid={Boolean(errors.password)} /><button className="visibility-button" type="button" aria-label={showPassword ? copy.hidePassword : copy.showPassword} onClick={() => setShowPassword((visible) => !visible)}><Icon name={showPassword ? 'eyeOff' : 'eye'} /></button></div>{errors.password && <p className="field-error">{errors.password}</p>}</div>
            <div className="field-group"><label htmlFor="confirmPassword">{copy.confirmLabel}</label><div className={`input-wrap ${errors.confirmPassword ? 'has-error' : ''}`}><Icon name="lock" /><input id="confirmPassword" name="confirmPassword" type={showConfirmation ? 'text' : 'password'} placeholder={copy.confirmPlaceholder} autoComplete="new-password" aria-invalid={Boolean(errors.confirmPassword)} /><button className="visibility-button" type="button" aria-label={showConfirmation ? copy.hideConfirmation : copy.showConfirmation} onClick={() => setShowConfirmation((visible) => !visible)}><Icon name={showConfirmation ? 'eyeOff' : 'eye'} /></button></div>{errors.confirmPassword && <p className="field-error">{errors.confirmPassword}</p>}</div>
            <div className="capital-note"><span className="capital-icon"><Icon name="gift" /></span><div className="capital-copy"><strong>{copy.capitalTitle}</strong><p>{copy.capitalLine}</p></div></div>
            <button className="primary-button" type="submit">{copy.create}</button>
            {submitted && <p className="form-success" role="status">{copy.success}</p>}
          </form>
          <div className="form-divider"><span>{copy.divider}</span></div>
          <button className="google-button" type="button"><Icon name="google" /><span>{copy.google}</span></button>
          <p className="login-prompt">{copy.loginPrompt} <a href={routeFor('login')}>{copy.loginLink}</a></p>
        </section>
      </div>
    </main>
  )
}
