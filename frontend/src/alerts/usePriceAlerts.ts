import { useEffect, useRef, useState } from 'react'
import { PriceAlertApiError, priceAlertsApi, type PriceAlertErrorCode } from '../api/priceAlertsApi'
import { getAuthorizationHeader } from '../auth/authStorage'
import type { AlertTerms, PriceAlert } from './alertsData'

type Mutation =
  | { kind: 'create'; terms: AlertTerms & { symbol: string } }
  | { kind: 'update'; id: string; terms: AlertTerms }
  | { kind: 'toggle'; id: string; enabled: boolean }
  | { kind: 'remove'; id: string }

type Result = { ok: true } | { ok: false; error?: PriceAlertErrorCode }
type Listing = { attempt: number; alerts: PriceAlert[]; error?: PriceAlertErrorCode }

const errorCode = (error: unknown) => error instanceof PriceAlertApiError ? error.code : 'server_error'

export function usePriceAlerts(authorization: string | null) {
  const [attempt, setAttempt] = useState(0)
  const [listing, setListing] = useState<Listing | null>(null)
  const [saving, setSaving] = useState(false)
  const mutation = useRef<AbortController | null>(null)

  useEffect(() => {
    if (!authorization) return
    const read = new AbortController()
    const isCurrent = () => !read.signal.aborted && getAuthorizationHeader()?.Authorization === authorization
    void priceAlertsApi.list(authorization, read.signal).then(alerts => {
      if (isCurrent()) setListing({ alerts, attempt })
    }).catch(error => {
      if (isCurrent()) {
        setListing(current => ({ alerts: current?.alerts ?? [], attempt, error: errorCode(error) }))
      }
    })
    return () => read.abort()
  }, [attempt, authorization])

  useEffect(() => () => mutation.current?.abort(), [authorization])

  const loading = authorization !== null && listing?.attempt !== attempt
  const loadError = authorization === null ? 'unauthorized' : loading ? undefined : listing?.error

  async function change(action: Mutation): Promise<Result> {
    if (!authorization || getAuthorizationHeader()?.Authorization !== authorization
      || mutation.current || loading || loadError) return { ok: false }

    const controller = new AbortController()
    const signal = controller.signal
    const isCurrent = () => !signal.aborted && getAuthorizationHeader()?.Authorization === authorization
    mutation.current = controller
    setSaving(true)
    try {
      let changed: PriceAlert | null = null
      switch (action.kind) {
        case 'create':
          changed = await priceAlertsApi.create(authorization, signal, action.terms)
          break
        case 'update':
          changed = await priceAlertsApi.update(authorization, signal, action.id, action.terms)
          break
        case 'toggle':
          changed = await priceAlertsApi.setEnabled(authorization, signal, action.id, action.enabled)
          break
        case 'remove':
          await priceAlertsApi.remove(authorization, signal, action.id)
          break
      }
      if (!isCurrent()) return { ok: false }
      const saved = changed
      setListing(current => {
        if (!current) return current
        let alerts: PriceAlert[]
        if (action.kind === 'remove') alerts = current.alerts.filter(alert => alert.id !== action.id)
        else if (!saved) return current
        else if (action.kind === 'create') alerts = [saved, ...current.alerts.filter(alert => alert.id !== saved.id)]
        else alerts = current.alerts.map(alert => alert.id === saved.id ? saved : alert)
        return { ...current, alerts }
      })
      return { ok: true }
    } catch (error) {
      if (!isCurrent()) return { ok: false }
      const code = errorCode(error)
      // Reconcile conflicts and writes with an unknown outcome without replaying them.
      if (code !== 'validation') setAttempt(value => value + 1)
      return { ok: false, error: code }
    } finally {
      if (mutation.current === controller) {
        mutation.current = null
        if (!signal.aborted) setSaving(false)
      }
    }
  }

  return {
    alerts: listing?.alerts ?? [], loading, loadError, saving, change,
    refresh: () => { if (!mutation.current && !loading) setAttempt(value => value + 1) },
  }
}
