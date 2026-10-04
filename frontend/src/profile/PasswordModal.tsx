import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { authStorageKey, getAuthSession } from '../auth/authStorage'
import { passwordApi, PasswordApiError, type ChangePasswordRequest } from '../api/passwordApi'

export function PasswordModal({ onClose, onSave }: { onClose: () => void; onSave: () => void }) {
  const { t } = useTranslation()
  const [error, setError] = useState('')
  const [fields, setFields] = useState<string[]>([])
  const [saving, setSaving] = useState(false)
  const savingRef = useRef(false)
  const controller = useRef<AbortController | null>(null)
  const formRef = useRef<HTMLFormElement | null>(null)
  const dialogRef = useRef<HTMLElement | null>(null)
  const session = useRef(getAuthSession())
  const validSession = () => {
    const current = getAuthSession()
    return current !== null && session.current !== null && current.user.id === session.current.user.id
      && current.accessToken === session.current.accessToken
  }
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    formRef.current?.querySelector<HTMLInputElement>('input')?.focus()
    function checkSession() {
      const current = getAuthSession()
      if (current && session.current && current.user.id === session.current.user.id && current.accessToken === session.current.accessToken) return
      controller.current?.abort()
      savingRef.current = false
      setSaving(false)
      formRef.current?.reset()
      setFields([])
      setError('profile.passwordModal.errors.session_changed')
    }
    const onStorage = (event: StorageEvent) => { if (event.key === authStorageKey || event.key === null) checkSession() }
    window.addEventListener('storage', onStorage)
    window.addEventListener('focus', checkSession)
    return () => {
      controller.current?.abort()
      window.removeEventListener('storage', onStorage)
      window.removeEventListener('focus', checkSession)
      previous?.focus()
    }
  }, [])
  const close = () => { if (!savingRef.current) onClose() }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (savingRef.current) return
    setError(''); setFields([])
    if (!validSession()) { setError('profile.passwordModal.errors.session_changed'); formRef.current?.reset(); return }
    const form = new FormData(event.currentTarget)
    const request: ChangePasswordRequest = {
      currentPassword: String(form.get('current-password') ?? ''), newPassword: String(form.get('new-password') ?? ''),
      confirmPassword: String(form.get('confirm-password') ?? ''),
    }
    if (!request.currentPassword.trim()) { setError('profile.passwordModal.errors.current_required'); setFields(['currentpassword']); return }
    if (!request.newPassword.trim() || request.newPassword.length < 8) { setError('profile.passwordModal.shortPassword'); setFields(['newpassword']); return }
    if (request.newPassword !== request.confirmPassword) { setError('profile.passwordModal.mismatch'); setFields(['confirmpassword']); return }
    if (request.newPassword === request.currentPassword) { setError('profile.passwordModal.errors.same_password'); setFields(['newpassword']); return }
    const activeController = new AbortController()
    controller.current = activeController
    savingRef.current = true; setSaving(true)
    try {
      await passwordApi.changePassword(request, activeController.signal)
      if (activeController.signal.aborted) return
      if (!validSession()) { setError('profile.passwordModal.errors.session_changed'); formRef.current?.reset(); return }
      formRef.current?.reset()
      onSave()
    } catch (failure) {
      if (!activeController.signal.aborted) {
        if (!validSession()) { setError('profile.passwordModal.errors.session_changed'); formRef.current?.reset() }
        else {
          const apiError = failure instanceof PasswordApiError ? failure : new PasswordApiError('server_error')
          setError(`profile.passwordModal.errors.${apiError.code}`); setFields(apiError.fields)
        }
      }
    } finally {
      if (controller.current === activeController) { savingRef.current = false; setSaving(false) }
    }
  }
  const changed = (field: string) => {
    const remaining = fields.filter(value => value !== field)
    setFields(remaining)
    if (remaining.length === 0) setError('')
  }
  return <div className="modal-backdrop" onClick={close} role="presentation">
    <section ref={dialogRef} aria-labelledby="password-title" aria-modal="true" className="password-modal" role="dialog"
      onClick={event => event.stopPropagation()} onKeyDown={event => {
        if (event.key === 'Escape') { event.preventDefault(); close() }
        if (event.key === 'Tab') {
          const items = dialogRef.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled)')
          if (!items?.length) { event.preventDefault(); return }
          const first = items[0], last = items[items.length - 1]
          if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
          else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
        }
      }}>
      <button aria-label={t('profile.passwordModal.close')} className="modal-close" disabled={saving} onClick={close} type="button"><svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18" /></svg></button>
      <div className="modal-icon"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><rect x="5" y="10" width="14" height="11" rx="2" /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v3" /></svg></div>
      <h2 id="password-title">{t('profile.passwordModal.title')}</h2><p>{t('profile.passwordModal.description')}</p>
      <form ref={formRef} onSubmit={submit} aria-busy={saving}>
        <label htmlFor="current-password">{t('profile.passwordModal.currentPassword')}</label>
        <input autoComplete="current-password" disabled={saving} aria-invalid={fields.includes('currentpassword')} aria-describedby={error ? 'password-error' : undefined} id="current-password" name="current-password" required type="password" onChange={() => changed('currentpassword')} />
        <label htmlFor="new-password">{t('profile.passwordModal.newPassword')}</label>
        <input autoComplete="new-password" disabled={saving} aria-invalid={fields.includes('newpassword')} aria-describedby={error ? 'password-error' : undefined} id="new-password" minLength={8} name="new-password" required type="password" onChange={() => changed('newpassword')} />
        <label htmlFor="confirm-password">{t('profile.passwordModal.confirmPassword')}</label>
        <input autoComplete="new-password" disabled={saving} aria-invalid={fields.includes('confirmpassword')} aria-describedby={error ? 'password-error' : undefined} id="confirm-password" minLength={8} name="confirm-password" required type="password" onChange={() => changed('confirmpassword')} />
        {error && <div id="password-error" className="form-error" role="alert">{t(error)}</div>}
        <div className="modal-actions"><button className="cancel-button" disabled={saving} onClick={close} type="button">{t('common.cancel')}</button>
          <button className="modal-primary" disabled={saving} type="submit">{t(saving ? 'profile.passwordModal.saving' : 'profile.passwordModal.save')}</button></div>
      </form>
    </section>
  </div>
}
