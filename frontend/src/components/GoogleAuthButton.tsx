import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { googleAuthApi, GoogleAuthApiError, type GoogleLinkTicket } from '../api/googleAuthApi'
import { ProfileApiError } from '../api/profileApi'
import { completeLogin, LoginCompletionError } from '../auth/completeLogin'
import { loadGoogleIdentitySdk, type GoogleCodeClient, type GoogleCodeResponse, type GoogleOAuth2 } from '../auth/googleIdentitySdk'
import type { AuthSession } from '../auth/authStorage'
import { getLoginLanguage, isSupersededLanguageChange } from '../i18n/languagePreference'
import type { Language } from '../i18n/languages'
import { i18n } from '../i18n/i18n'
import { routeFor } from '../navigation/routes'
import './googleAuth.css'

type Status = 'loading' | 'unavailable' | 'failed' | 'ready' | 'popup' | 'exchange' | 'link' | 'linkSubmitting' | 'finishing'
type Attempt = { id: number; stage: Status; language: Language; controller: AbortController; ticket?: GoogleLinkTicket }
type Controls = { start(): void; cancel(): void; link(password: string): void; expire(): void }
const ignoreBusy = () => { /* Optional for standalone use. */ }
const allowStart = () => true

function apiErrorKey(error: unknown, linking = false) {
  if (error instanceof LoginCompletionError) return error.translationKey
  if (error instanceof ProfileApiError) return 'login.errors.languageSave'
  if (!(error instanceof GoogleAuthApiError)) return 'googleAuth.errors.unavailable'
  return ({
    google_link_required: 'googleAuth.errors.invalidResponse',
    google_link_expired: 'googleAuth.errors.expired',
    google_account_conflict: 'googleAuth.errors.conflict',
    google_invalid_response: 'googleAuth.errors.invalidResponse',
    google_unavailable: 'googleAuth.errors.unavailable',
    invalid_credentials: linking ? 'googleAuth.errors.invalidPassword' : 'googleAuth.errors.invalidResponse',
    validation_error: 'googleAuth.errors.invalidRequest',
    rate_limited: 'googleAuth.errors.rateLimited',
    offline: 'googleAuth.errors.offline',
    invalid_response: 'googleAuth.errors.invalidResponse',
    server_error: 'googleAuth.errors.unavailable',
  } satisfies Record<GoogleAuthApiError['code'], string>)[error.code]
}

export function GoogleAuthButton({ disabled = false, onBusyChange = ignoreBusy, onStart = allowStart }: {
  disabled?: boolean
  onBusyChange?: (busy: boolean) => void
  onStart?: () => boolean | void
}) {
  const { t } = useTranslation()
  const [status, setStatus] = useState<Status>('loading')
  const [errorKey, setErrorKey] = useState<string>()
  const [ticket, setTicket] = useState<GoogleLinkTicket>()
  const [revision, setRevision] = useState(0)
  const [retryUntil, setRetryUntil] = useState<number | null>(null)
  const [retrySeconds, setRetrySeconds] = useState(0)
  const blockedUntil = useRef(0)
  const controls = useRef<Controls | null>(null)

  useEffect(() => {
    let alive = true
    let sequence = 0
    let attempt: Attempt | undefined
    let identity: GoogleOAuth2
    let clientId: string
    let prepared: { client: GoogleCodeClient; id: number } | undefined
    const configuration = new AbortController()
    const current = (id: number) => alive && attempt?.id === id
    const clearAttempt = () => {
      attempt?.controller.abort()
      attempt = undefined
      setTicket(undefined)
      onBusyChange(false)
    }
    const finish = (key?: string) => {
      if (!alive) return
      clearAttempt()
      setErrorKey(key)
      prepare()
    }
    const cooldown = (error: unknown) => {
      if (!(error instanceof GoogleAuthApiError) || error.code !== 'rate_limited') return
      const seconds = error.retryAfterSeconds ?? 60
      blockedUntil.current = Date.now() + seconds * 1000
      setRetryUntil(seconds > 0 ? blockedUntil.current : null)
      setRetrySeconds(seconds)
    }
    const acceptSession = async (id: number, session: AuthSession) => {
      if (!current(id)) return
      attempt!.stage = 'finishing'
      setTicket(undefined)
      setStatus('finishing')
      await completeLogin(session, () => current(id))
    }
    const failed = (id: number, error: unknown, linking: boolean) => {
      if (!current(id)) return
      if (isSupersededLanguageChange(error)) { finish(); return }
      cooldown(error)
      if (error instanceof GoogleAuthApiError && error.code === 'google_link_required' && error.linkTicket) {
        if (Date.parse(error.linkTicket.expiresAtUtc) <= Date.now()) { finish('googleAuth.errors.expired'); return }
        attempt!.stage = 'link'
        attempt!.ticket = error.linkTicket
        setTicket(error.linkTicket)
        setStatus('link')
        setErrorKey(undefined)
      } else if (linking && error instanceof GoogleAuthApiError
        && ['invalid_credentials', 'validation_error', 'rate_limited', 'offline', 'server_error', 'google_unavailable'].includes(error.code)) {
        attempt!.stage = 'link'
        setStatus('link')
        setErrorKey(apiErrorKey(error, true))
      } else finish(apiErrorKey(error, linking))
    }
    const response = (id: number, result: GoogleCodeResponse) => {
      if (!current(id) || attempt!.stage !== 'popup') return
      if (result.error) { finish('googleAuth.errors.refused'); return }
      if (typeof result.code !== 'string' || !result.code.trim()) { finish('googleAuth.errors.invalidResponse'); return }
      attempt!.stage = 'exchange'
      setStatus('exchange')
      void googleAuthApi.exchange({ code: result.code, language: attempt!.language }, attempt!.controller.signal)
        .then(session => acceptSession(id, session)).catch(error => failed(id, error, false))
    }
    function prepare() {
      prepared = undefined
      try {
        const id = ++sequence
        const client = identity.initCodeClient({ client_id: clientId, scope: 'openid email profile', ux_mode: 'popup',
          include_granted_scopes: false, select_account: true,
          callback: result => response(id, result),
          error_callback: error => {
            if (!current(id) || attempt!.stage !== 'popup') return
            finish(error.type === 'popup_closed' ? 'googleAuth.errors.cancelled'
              : error.type === 'popup_failed_to_open' ? 'googleAuth.errors.popupBlocked' : 'googleAuth.errors.unavailable')
          },
        })
        prepared = { client, id }
        setStatus('ready')
      } catch {
        setStatus('failed')
        setErrorKey('googleAuth.errors.sdk')
      }
    }
    controls.current = {
      start() {
        if (!alive || !prepared || attempt || Date.now() < blockedUntil.current || onStart() === false) return
        const next = prepared
        prepared = undefined
        attempt = { id: next.id, stage: 'popup', language: getLoginLanguage() ?? (i18n.language === 'fr' ? 'fr' : 'en'), controller: new AbortController() }
        setErrorKey(undefined)
        setStatus('popup')
        onBusyChange(true)
        // Keep this synchronous with the user's click so browsers can open the popup.
        try { next.client.requestCode() } catch { finish('googleAuth.errors.popupBlocked') }
      },
      cancel() { finish('googleAuth.errors.cancelled') },
      expire() { finish('googleAuth.errors.expired') },
      link(password) {
        if (!alive || !attempt?.ticket || attempt.stage !== 'link' || Date.now() < blockedUntil.current) return
        if (Date.parse(attempt.ticket.expiresAtUtc) <= Date.now()) { finish('googleAuth.errors.expired'); return }
        if (!password) { setErrorKey('googleAuth.errors.passwordRequired'); return }
        const { id, ticket: proof, language, controller } = attempt
        attempt.stage = 'linkSubmitting'
        setStatus('linkSubmitting')
        setErrorKey(undefined)
        void googleAuthApi.link({ linkToken: proof.linkToken, password, language }, controller.signal)
          .then(session => acceptSession(id, session)).catch(error => failed(id, error, true))
      },
    }
    void (async () => {
      try {
        const config = await googleAuthApi.getConfig(window.location.origin, configuration.signal)
        if (!alive) return
        if (!config.enabled) { setStatus('unavailable'); return }
        clientId = config.clientId
        try { identity = await loadGoogleIdentitySdk() } catch {
          if (alive) { setStatus('failed'); setErrorKey('googleAuth.errors.sdk') }
          return
        }
        if (alive) prepare()
      } catch (error) {
        if (alive) { setStatus('failed'); setErrorKey(apiErrorKey(error)) }
      }
    })()
    return () => {
      alive = false
      configuration.abort()
      attempt?.controller.abort()
      if (attempt) onBusyChange(false)
      attempt = undefined
      controls.current = null
    }
  }, [revision, onBusyChange, onStart])

  useEffect(() => {
    if (!ticket) return
    const expire = () => { if (Date.parse(ticket.expiresAtUtc) <= Date.now()) controls.current?.expire() }
    const timer = window.setInterval(expire, 1000)
    window.addEventListener('focus', expire)
    return () => { window.clearInterval(timer); window.removeEventListener('focus', expire) }
  }, [ticket])
  useEffect(() => {
    if (retryUntil === null) return
    const update = () => {
      const seconds = Math.max(0, Math.ceil((retryUntil - Date.now()) / 1000))
      setRetrySeconds(seconds)
      if (seconds === 0) setRetryUntil(null)
    }
    const timer = window.setInterval(update, 1000)
    window.addEventListener('focus', update)
    document.addEventListener('visibilitychange', update)
    return () => { window.clearInterval(timer); window.removeEventListener('focus', update); document.removeEventListener('visibilitychange', update) }
  }, [retryUntil])

  function submitLink(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    controls.current?.link(String(new FormData(event.currentTarget).get('googleLinkPassword') ?? ''))
  }
  const busy = ['popup', 'exchange', 'link', 'linkSubmitting', 'finishing'].includes(status)
  const linking = status === 'link' || status === 'linkSubmitting'
  return <div className="google-auth" aria-busy={busy}>
    <button className="google-button" type="button" disabled={disabled || status !== 'ready' || retrySeconds > 0}
      onClick={() => { if (!disabled) controls.current?.start() }}>
      <svg className="icon google-icon" viewBox="0 0 18 18" aria-hidden="true">
        <path fill="#4285F4" d="M17.64 9.205c0-.638-.057-1.252-.164-1.841H9v3.482h4.844a4.14 4.14 0 0 1-1.797 2.716v2.258h2.908c1.702-1.568 2.685-3.878 2.685-6.615Z" />
        <path fill="#34A853" d="M9 18c2.43 0 4.467-.806 5.955-2.18l-2.908-2.258c-.806.54-1.835.86-3.047.86-2.345 0-4.332-1.584-5.043-3.716H.951v2.332A9 9 0 0 0 9 18Z" />
        <path fill="#FBBC05" d="M3.957 10.706A5.41 5.41 0 0 1 3.674 9c0-.592.102-1.167.283-1.706V4.962H.951A9 9 0 0 0 0 9c0 1.452.348 2.826.951 4.038l3.006-2.332Z" />
        <path fill="#EA4335" d="M9 3.578c1.323 0 2.51.455 3.445 1.348l2.584-2.584C13.463.892 11.426 0 9 0A9 9 0 0 0 .951 4.962l3.006 2.332C4.668 5.162 6.655 3.578 9 3.578Z" />
      </svg>
      <span>{t(status === 'loading' ? 'googleAuth.loading' : busy ? 'googleAuth.working' : 'googleAuth.continue')}</span>
    </button>
    {status === 'unavailable' && <p className="google-auth-hint" role="status">{t('googleAuth.errors.notConfigured')}</p>}
    {status === 'popup' && <p className="google-auth-hint" role="status">{t('googleAuth.popupHint')}</p>}
    {errorKey && <p className="form-error" role="alert">{t(errorKey)}{retrySeconds > 0 && <> <span role="timer" aria-live="off">{t('login.retryCountdown', { count: retrySeconds })}</span></>}</p>}
    {linking && <form className="google-link-form" onSubmit={submitLink}>
      <p className="google-auth-hint">{t('googleAuth.link.description')}</p>
      <label htmlFor="google-link-password">{t('googleAuth.link.password')}</label>
      <div className="input-wrap"><input id="google-link-password" name="googleLinkPassword" type="password" autoComplete="current-password" disabled={status === 'linkSubmitting'} /></div>
      <a href={routeFor('forgot-password')}>{t('login.forgot')}</a>
      <button className="primary-button" type="submit" disabled={status === 'linkSubmitting' || retrySeconds > 0}>{t(status === 'linkSubmitting' ? 'googleAuth.working' : 'googleAuth.link.submit')}</button>
    </form>}
    {busy && <button className="google-auth-cancel" type="button" onClick={() => controls.current?.cancel()}>{t('googleAuth.cancel')}</button>}
    {(status === 'failed' || status === 'unavailable') && <button className="google-auth-retry" type="button" disabled={disabled} onClick={() => {
      setErrorKey(undefined); setStatus('loading'); setRevision(value => value + 1)
    }}>{t('googleAuth.retry')}</button>}
  </div>
}
