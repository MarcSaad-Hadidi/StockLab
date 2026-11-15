import { useEffect, useRef, useState } from 'react'
import { PasswordRecoveryApiError, type PasswordRecoveryErrorCode } from '../api/passwordRecoveryApi'

export type RecoveryField = 'email' | 'newPassword' | 'confirmPassword'
export type RecoveryFieldErrors = Partial<Record<RecoveryField, string>>
export const recoveryErrorKey: Record<PasswordRecoveryErrorCode, string> = {
  invalid_reset_token: 'recovery.errors.invalidLink', validation_error: 'recovery.errors.validation',
  too_many_requests: 'recovery.errors.rateLimited', password_recovery_unavailable: 'recovery.errors.unavailable',
  password_update_conflict: 'recovery.errors.conflict', internal_server_error: 'recovery.errors.server',
  offline: 'recovery.errors.offline', invalid_response: 'recovery.errors.server',
}
const serverFields: Record<string, [RecoveryField, string]> = {
  email: ['email', 'recovery.errors.emailInvalid'], newpassword: ['newPassword', 'recovery.errors.passwordInvalid'],
  confirmpassword: ['confirmPassword', 'recovery.errors.mismatch'],
}

export function useRecoveryForm() {
  const [errors, setErrors] = useState<RecoveryFieldErrors>({})
  const [failure, setFailure] = useState<PasswordRecoveryErrorCode>()
  const [busy, setBusy] = useState(false)
  const submitting = useRef(false)
  const [retryUntil, setRetryUntil] = useState<number | null>(null)
  const [retrySeconds, setRetrySeconds] = useState(0)
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
    return () => {
      window.clearInterval(timer)
      window.removeEventListener('focus', update)
      document.removeEventListener('visibilitychange', update)
    }
  }, [retryUntil])

  function clearField(field: RecoveryField) {
    const remaining = { ...errors }
    delete remaining[field]
    // Confirmation also depends on the new password.
    if (field === 'newPassword') delete remaining.confirmPassword
    setErrors(remaining)
    if (failure === 'validation_error' && Object.keys(remaining).length === 0) setFailure(undefined)
  }
  async function submit(action: () => Promise<void>, validation: RecoveryFieldErrors) {
    if (submitting.current || retryUntil !== null && Date.now() < retryUntil) return
    setFailure(undefined)
    setErrors(validation)
    if (Object.keys(validation).length > 0) return
    submitting.current = true
    setBusy(true)
    try { await action() }
    catch (error) {
      const safe = error instanceof PasswordRecoveryApiError ? error : new PasswordRecoveryApiError('internal_server_error')
      setFailure(safe.code)
      setErrors(Object.fromEntries(safe.fields.flatMap(field => serverFields[field] ? [serverFields[field]] : [])))
      if (safe.code === 'too_many_requests') {
        const seconds = safe.retryAfterSeconds ?? 60
        setRetrySeconds(seconds)
        setRetryUntil(seconds > 0 ? Date.now() + seconds * 1000 : null)
      }
    } finally { submitting.current = false; setBusy(false) }
  }
  return { errors, failure, busy, retrySeconds, clearField, submit }
}
