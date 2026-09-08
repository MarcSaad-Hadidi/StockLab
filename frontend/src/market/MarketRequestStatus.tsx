import { useTranslation } from 'react-i18next'
export function MarketRequestStatus({
  loading,
  error,
  retry
}: {
  loading: boolean
  error?: string
  retry: () => void
}) {
  const { t } = useTranslation()
  if (loading) return <p role="status">{t('marketApi.loading')}</p>
  if (error)
    return (
      <div role="alert">
        <p>{t(error)}</p>
        <button
          className="stock-secondary-button"
          type="button"
          onClick={retry}
        >
          {t('marketApi.retry')}
        </button>
      </div>
    )
  return null
}
