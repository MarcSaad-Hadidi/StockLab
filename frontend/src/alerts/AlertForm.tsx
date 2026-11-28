import { useCallback, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { marketDataApi } from '../api/marketDataClient'
import { MarketRequestStatus } from '../market/MarketRequestStatus'
import { useMarketRequest } from '../market/useMarketRequest'
import { parseAlertTarget, type AlertCondition, type AlertTerms, type PriceAlert } from './alertsData'

type Props = {
  editing: PriceAlert | null
  disabled: boolean
  saving: boolean
  onCancel: () => void
  onSave: (terms: AlertTerms & { symbol: string }) => Promise<boolean>
}

export function AlertForm({ editing, disabled, saving, onCancel, onSave }: Props) {
  const { t } = useTranslation()
  const [search, setSearch] = useState('')
  const [symbol, setSymbol] = useState(editing?.symbol ?? '')
  const [condition, setCondition] = useState<AlertCondition>(editing?.condition ?? 'above')
  const [target, setTarget] = useState(editing?.targetPrice ?? '')
  const [submitted, setSubmitted] = useState(false)
  const query = search.trim()
  const loadSearch = useCallback((signal: AbortSignal) => marketDataApi.search(query, signal, true), [query])
  const results = useMarketRequest(query, loadSearch, !editing && query.length > 0 && !symbol, 450)
  // The alerts service stores USD thresholds and skips quotes in another currency.
  const assets = results.data?.filter(asset => asset.currency === 'USD' && asset.symbol.length <= 32) ?? []
  const targetPrice = parseAlertTarget(target)
  const symbolError = submitted && !symbol
  const targetError = submitted && !targetPrice

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (disabled) return
    setSubmitted(true)
    if (!symbol || !targetPrice) return
    if (await onSave({ symbol, condition, targetPrice }) && !editing) {
      setSubmitted(false)
      setSearch('')
      setSymbol('')
      setTarget('')
      setCondition('above')
    }
  }

  return <form className="alert-form" id="create-alert-form" noValidate onSubmit={event => { void submit(event) }}>
    <fieldset className="alert-fields" disabled={disabled}>
      <legend className="visually-hidden">{t(editing ? 'alerts.editAlertTitle' : 'alerts.createAlert')}</legend>
      {editing ? <p>{t('alerts.currentAsset', { symbol: editing.symbol })}</p> :
        <div className="alert-form-field alert-asset-search">
          <label htmlFor="alert-market-search">{t('common.searchStocks')}</label>
          <p id="alert-search-description">{t('alerts.searchUsdHint')}</p>
          <input id="alert-market-search" type="search" aria-describedby={symbolError ? 'alert-search-description alert-symbol-error' : 'alert-search-description'}
            aria-invalid={symbolError} maxLength={80} placeholder={t('common.searchStocks')} value={search}
            onChange={event => { setSearch(event.target.value); setSymbol('') }} />
          <MarketRequestStatus {...results} />
          {results.data && assets.length === 0 && <p>{t('alerts.noUsdStocks')}</p>}
          {assets.length > 0 && <ul className="alert-search-results">
            {assets.map(asset => <li key={asset.symbol}><button className="outline-button" type="button" onClick={() => {
              setSymbol(asset.symbol)
              setSearch(asset.symbol)
            }}>{asset.symbol} — {asset.companyName}</button></li>)}
          </ul>}
          {symbol && <p>{t('alerts.currentAsset', { symbol })}</p>}
          {symbolError && <p className="form-error" id="alert-symbol-error">{t('alerts.chooseStock')}</p>}
        </div>}
      <div className="alert-form-grid">
        <fieldset className="alert-form-field alert-form-condition">
          <legend>{t('common.condition')}</legend>
          <div className="condition-options">
            {(['above', 'below'] as const).map(value => <button key={value} aria-pressed={condition === value}
              className={`condition-option${condition === value ? ' selected' : ''}`} type="button" onClick={() => setCondition(value)}>
              <span aria-hidden="true">{value === 'above' ? '↑' : '↓'}</span>{t(`common.${value}`)}
            </button>)}
          </div>
        </fieldset>
        <div className="alert-form-field alert-form-target">
          <label htmlFor="inline-alert-target">{t('common.targetPrice')} (USD)</label>
          <div className="price-input"><span aria-hidden="true">$</span><input id="inline-alert-target" inputMode="decimal"
            maxLength={24} aria-invalid={targetError} aria-describedby={targetError ? 'alert-target-error' : undefined}
            value={target} onChange={event => setTarget(event.target.value)} /></div>
          {targetError && <p className="form-error" id="alert-target-error">{t('alerts.invalidTarget')}</p>}
        </div>
      </div>
      <div className="alert-form-actions">
        {editing && <button className="cancel-button" type="button" onClick={onCancel}>{t('common.cancel')}</button>}
        <button className="modal-primary" type="submit">{t(saving ? 'alerts.saving' : editing ? 'alerts.saveChanges' : 'alerts.createAlert')}</button>
      </div>
    </fieldset>
  </form>
}
