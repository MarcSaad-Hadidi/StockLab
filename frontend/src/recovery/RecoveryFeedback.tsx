import { useTranslation } from 'react-i18next'
import type { PasswordRecoveryErrorCode } from '../api/passwordRecoveryApi'
import { recoveryErrorKey } from './useRecoveryForm'

export function RecoveryFeedback({ failure, retrySeconds }: { failure?: PasswordRecoveryErrorCode; retrySeconds: number }) {
  const { t } = useTranslation()
  if (!failure) return null
  if (failure === 'too_many_requests') return retrySeconds > 0
    ? <p className="form-error" id="recovery-retry-status" role="alert">{t(recoveryErrorKey[failure])}{' '}<span role="timer" aria-live="off">{t('recovery.retryCountdown', { count: retrySeconds })}</span></p>
    : <p className="form-success" role="status">{t('recovery.retryReady')}</p>
  return <p className="form-error" role="alert">{t(recoveryErrorKey[failure])}</p>
}
