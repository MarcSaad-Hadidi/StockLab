import { useEffect, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { AuthBrand } from '../components/AuthBrand'
import { LanguageSelector } from '../components/LanguageSelector'
import { routeFor } from '../navigation/routes'
import '../login/login.css'
import './recovery.css'

export function RecoveryLayout({ title, subtitle, busy, children }: { title: string; subtitle: string; busy: boolean; children: ReactNode }) {
  const { t } = useTranslation()
  useEffect(() => { document.title = `${t(title)} | StockLab` }, [title, t])
  return <main className="login-page recovery-page"><div className="page-frame">
    <section className="login-card recovery-card" aria-labelledby="recovery-heading">
      <div className="card-topline"><AuthBrand /><LanguageSelector persistToAccount={false} disabled={busy} /></div>
      <header className="card-heading"><h1 id="recovery-heading">{t(title)}</h1><p>{t(subtitle)}</p></header>
      {children}
      <p className="register-prompt"><a href={routeFor('login')}>{t('recovery.backToLogin')}</a></p>
    </section>
  </div></main>
}
