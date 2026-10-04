import { useTranslation } from 'react-i18next'
import type { WatchlistErrorCode } from '../api/watchlistApi'
import { routeFor } from '../navigation/routes'
import './watchlist-feedback.css'

export function WatchlistFeedback({ error, loading = false, retry }: { error: WatchlistErrorCode | null; loading?: boolean; retry?: () => void }) {
  const { t } = useTranslation()
  if (!error && !loading) return null
  return <div className="watchlist-feedback" role={error ? 'alert' : 'status'}>
    <span>{t(error ? `watchlist.errors.${error}` : 'watchlist.loading')}</span>
    {error === 'unauthorized' ? <a href={routeFor('login')}>{t('login.signIn')}</a>
      : error && retry && <button type="button" onClick={retry}>{t('watchlist.retry')}</button>}
  </div>
}
