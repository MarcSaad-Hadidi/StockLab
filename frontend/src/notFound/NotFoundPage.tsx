import { routeFor } from '../navigation/routes'
import { useTranslation } from 'react-i18next'
import { useEffect } from 'react'
import './not-found.css'

function Brand() {
  return <div aria-label="StockLab" className="brand"><span aria-hidden="true" className="brand-mark"><i /><i /><i /></span><span>Stock<span>Lab</span></span></div>
}

function NotFoundGraphic() {
  const { t } = useTranslation()
  return <div aria-hidden="true" className="not-found-graphic"><span className="graphic-orbit graphic-orbit-one" /><span className="graphic-orbit graphic-orbit-two" /><svg className="graphic-chart" fill="none" viewBox="0 0 440 190"><path className="graphic-grid" d="M32 30h376M32 79h376M32 128h376M32 177h376M32 30v147M126 30v147M220 30v147M314 30v147M408 30v147" /><path className="graphic-line" d="M32 150 79 130l44 18 53-54 42 28 48-50 46 21 52-59 42 28" /><circle className="graphic-point" cx="408" cy="62" r="5" /><path className="graphic-arrow" d="m395 62 13-1-5 12" /></svg><strong>404</strong><span className="graphic-label">{t('notFound.signalNotFound')}</span></div>
}

export default function NotFoundPage() {
  const { i18n, t } = useTranslation()
  useEffect(() => {
    document.title = `${t('notFound.pageNotFound')} | StockLab`
  }, [i18n.language, t])
  return <div className="not-found-page"><header className="not-found-header"><Brand /><span className="status-pill"><i /> {t('notFound.workspaceOnline')}</span></header><main className="not-found-main"><section aria-labelledby="not-found-title" className="not-found-card"><div className="not-found-copy"><p className="not-found-eyebrow">{t('notFound.error')} <span /> {t('notFound.workspace')}</p><h1 id="not-found-title">{t('notFound.pageNotFound')}</h1><p className="not-found-message">{t('notFound.message')}</p><a className="dashboard-button" href={routeFor('dashboard')}><span className="button-arrow">←</span> {t('notFound.backToDashboard')}</a><p className="not-found-hint">{t('notFound.hint')}</p></div><NotFoundGraphic /></section></main><footer className="not-found-footer"><span>© 2026 StockLab</span><span>{t('notFound.simulationWorkspace')}</span></footer></div>
}
