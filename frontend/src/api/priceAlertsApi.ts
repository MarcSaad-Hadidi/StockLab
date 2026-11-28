import { isAlertPrice, type AlertCondition, type AlertStatus, type AlertTerms, type PriceAlert } from '../alerts/alertsData'
import { getAuthorizationHeader } from '../auth/authStorage'
import { handleUnauthorized } from '../auth/handleUnauthorized'

export type PriceAlertErrorCode = 'unauthorized' | 'validation' | 'not_found' | 'triggered' | 'conflict'
  | 'offline' | 'invalid_response' | 'server_error'

export class PriceAlertApiError extends Error {
  readonly code: PriceAlertErrorCode

  constructor(code: PriceAlertErrorCode) {
    super(code)
    this.name = 'PriceAlertApiError'
    this.code = code
  }
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null

const isDate = (value: unknown): value is string =>
  typeof value === 'string' && value.endsWith('Z') && Number.isFinite(Date.parse(value))

const conditions = { Above: 'above', Below: 'below' } satisfies Record<string, AlertCondition>
const statuses = { Active: 'active', Disabled: 'disabled', Triggered: 'triggered' } satisfies Record<string, AlertStatus>

function parseAlert(value: unknown, expectedId?: string): PriceAlert {
  if (!isRecord(value)
    || typeof value.id !== 'string' || !/^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(value.id)
    || (expectedId !== undefined && value.id !== expectedId)
    || typeof value.symbol !== 'string' || !value.symbol.trim() || value.symbol.length > 32
    || typeof value.currency !== 'string' || !/^[A-Z]{3}$/.test(value.currency)
    || typeof value.condition !== 'string' || !Object.hasOwn(conditions, value.condition)
    || typeof value.status !== 'string' || !Object.hasOwn(statuses, value.status)
    || !isAlertPrice(value.targetPriceDecimal) || !isDate(value.createdAtUtc)) {
    throw new PriceAlertApiError('invalid_response')
  }
  const status = statuses[value.status as keyof typeof statuses]
  const triggeredPrice = value.triggeredPriceDecimal
  const triggeredAtUtc = value.triggeredAtUtc
  if (status === 'triggered' ? !isAlertPrice(triggeredPrice) || !isDate(triggeredAtUtc)
    : triggeredPrice !== null || triggeredAtUtc !== null) {
    throw new PriceAlertApiError('invalid_response')
  }
  return {
    id: value.id, symbol: value.symbol, currency: value.currency, status,
    condition: conditions[value.condition as keyof typeof conditions],
    targetPrice: value.targetPriceDecimal, createdAtUtc: value.createdAtUtc,
    triggeredPrice: isAlertPrice(triggeredPrice) ? triggeredPrice : null,
    triggeredAtUtc: isDate(triggeredAtUtc) ? triggeredAtUtc : null,
  }
}

async function readError(response: Response): Promise<PriceAlertErrorCode> {
  if (response.status === 401) return 'unauthorized'
  if (response.status === 400) return 'validation'
  if (response.status === 404) return 'not_found'
  if (response.status === 409) {
    const value: unknown = await response.json().catch(() => null)
    return isRecord(value) && value.error === 'alert_already_triggered' ? 'triggered' : 'conflict'
  }
  return 'server_error'
}

export function createPriceAlertsApi(baseUrl: string, fetcher: typeof fetch = fetch, getAuthorization = getAuthorizationHeader) {
  const path = `${baseUrl.replace(/\/$/, '')}/api/alerts`

  async function request(
    authorization: string, signal: AbortSignal, expectedStatus: number, method: string, suffix = '',
    body?: AlertTerms & { symbol?: string },
  ): Promise<unknown> {
    signal.throwIfAborted()
    if (getAuthorization()?.Authorization !== authorization) throw new PriceAlertApiError('unauthorized')
    let response: Response
    try {
      response = await fetcher(path + suffix, {
        method, signal,
        headers: { Accept: 'application/json', Authorization: authorization, ...(body && { 'Content-Type': 'application/json' }) },
        ...(body && { body: JSON.stringify(body) }),
      })
    } catch {
      signal.throwIfAborted()
      throw new PriceAlertApiError('offline')
    }
    signal.throwIfAborted()
    handleUnauthorized(response, authorization, signal)
    if (!response.ok) throw new PriceAlertApiError(await readError(response))
    if (response.status !== expectedStatus) throw new PriceAlertApiError('invalid_response')
    if (expectedStatus === 204) return undefined
    const value: unknown = await response.json().catch(() => { throw new PriceAlertApiError('invalid_response') })
    signal.throwIfAborted()
    return value
  }

  return {
    async list(authorization: string, signal: AbortSignal) {
      const value = await request(authorization, signal, 200, 'GET')
      if (!Array.isArray(value)) throw new PriceAlertApiError('invalid_response')
      return value.map(item => parseAlert(item))
    },
    async create(authorization: string, signal: AbortSignal, body: AlertTerms & { symbol: string }) {
      return parseAlert(await request(authorization, signal, 201, 'POST', '', body))
    },
    async update(authorization: string, signal: AbortSignal, id: string, body: AlertTerms) {
      return parseAlert(await request(authorization, signal, 200, 'PUT', `/${encodeURIComponent(id)}`, body), id)
    },
    async setEnabled(authorization: string, signal: AbortSignal, id: string, enabled: boolean) {
      const alert = parseAlert(await request(authorization, signal, 200, 'POST', `/${encodeURIComponent(id)}/${enabled ? 'enable' : 'disable'}`), id)
      if (alert.status !== (enabled ? 'active' : 'disabled')) throw new PriceAlertApiError('invalid_response')
      return alert
    },
    async remove(authorization: string, signal: AbortSignal, id: string) {
      await request(authorization, signal, 204, 'DELETE', `/${encodeURIComponent(id)}`)
    },
  }
}

const environment = (import.meta as ImportMeta & { env?: { VITE_STOCKLAB_API_BASE_URL?: string } }).env
export const priceAlertsApi = createPriceAlertsApi(environment?.VITE_STOCKLAB_API_BASE_URL ?? '')
