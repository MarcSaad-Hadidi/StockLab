import { useTranslation } from 'react-i18next'
export function MarketRequestStatus({
  loading,
  error,
  retry,
  label,
  loadingMessage = 'marketApi.loading'
}: {
  loading: boolean
  error?: string
  retry: () => void
  label?: string
  loadingMessage?: string
}) {
  const { t } = useTranslation()
  if (loading) return <p className="market-request-status" role="status">{label && <strong>{t(label)} · </strong>}{t(loadingMessage)}</p>
  if (error)
    return (
      <div className="market-request-status market-request-error" role="alert">
        <p>{label && <strong>{t(label)} · </strong>}{t(error)}</p>
        <button
          className="market-retry-button"
          type="button"
          onClick={retry}
        >
          {t('marketApi.retry')}
        </button>
      </div>
    )
  return null
}
