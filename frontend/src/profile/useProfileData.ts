import { useEffect, useRef, useState } from 'react'
import { profileApi, ProfileApiError, type ProfileErrorCode, type UpdateProfileRequest, type UserProfile } from '../api/profileApi'
import { portfolioApi } from '../api/portfolioApi'
import { authStorageKey, clearAuthSession, getAuthSession, saveAuthSession, type AuthSession } from '../auth/authStorage'

const sameSession = (expected: AuthSession | null) => {
  const current = getAuthSession()
  return expected !== null && current !== null
    && expected.user.id === current.user.id && expected.accessToken === current.accessToken
}
const errorCode = (error: unknown): ProfileErrorCode => error instanceof ProfileApiError ? error.code : 'server_error'

export function useProfileData() {
  const [profile, setProfile] = useState<UserProfile | null>(null)
  const [capital, setCapital] = useState<{ initialCapital: number; currency: string } | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<ProfileErrorCode | null>(null)
  const [saveError, setSaveError] = useState<ProfileErrorCode | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)
  const [sessionWarning, setSessionWarning] = useState(false)
  const [revision, setRevision] = useState(0)
  const sessionRef = useRef<AuthSession | null>(null)
  const savingRef = useRef(false)
  const saveController = useRef<AbortController | null>(null)
  const loadController = useRef<AbortController | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    loadController.current = controller
    const session = getAuthSession()
    sessionRef.current = session

    function invalidateSession() {
      const expected = sessionRef.current
      const current = getAuthSession()
      if (sameSession(expected)) {
        if (expected!.user.displayName === current!.user.displayName && expected!.user.email === current!.user.email) return
        controller.abort()
        saveController.current?.abort()
        savingRef.current = false
        setSaving(false)
        setProfile(null)
        setCapital(null)
        setLoading(true)
        setLoadError(null)
        setSaveError(null)
        setFieldErrors({})
        setSessionWarning(false)
        setRevision(value => value + 1)
        return
      }
      controller.abort()
      saveController.current?.abort()
      setProfile(null)
      setCapital(null)
      setLoading(false)
      setLoadError('unauthorized')
      setSaving(false)
    }
    const onStorage = (event: StorageEvent) => {
      if (event.key === authStorageKey || event.key === null) invalidateSession()
    }
    window.addEventListener('storage', onStorage)
    window.addEventListener('focus', invalidateSession)
    if (session) {
      void profileApi.getProfile(controller.signal).then(result => {
        if (controller.signal.aborted) return
        if (!sameSession(session)) { invalidateSession(); return }
        if (result.id !== session.user.id) throw new ProfileApiError(502, 'invalid_response')
        setProfile(result)
        const current = getAuthSession()!
        if (current.user.displayName !== result.displayName || current.user.email !== result.email)
          setSessionWarning(!saveAuthSession({ ...current, user: { id: result.id, displayName: result.displayName, email: result.email } }))
        sessionRef.current = getAuthSession()
      }).catch(error => {
        if (!controller.signal.aborted) {
          if (!sameSession(session)) { invalidateSession(); return }
          if (errorCode(error) === 'unauthorized') {
            if (sameSession(session)) clearAuthSession()
            controller.abort()
            setCapital(null)
            setLoading(false)
          }
          setLoadError(errorCode(error))
        }
      }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
      // The profile remains editable when the independent portfolio endpoint is unavailable.
      void portfolioApi.getPortfolio(controller.signal).then(result => {
        const currency = result.currency.trim().toUpperCase()
        if (!controller.signal.aborted && sameSession(session) && /^[A-Z]{3}$/.test(currency))
          setCapital({ initialCapital: result.initialCapital, currency })
      }).catch(() => { /* Unavailable capital is displayed as unavailable, never as a default. */ })
    } else {
      // Resolve the missing-session state through the same asynchronous load lifecycle.
      void Promise.resolve().then(() => {
        if (!controller.signal.aborted) { setLoading(false); setLoadError('unauthorized') }
      })
    }
    return () => {
      controller.abort()
      saveController.current?.abort()
      window.removeEventListener('storage', onStorage)
      window.removeEventListener('focus', invalidateSession)
    }
  }, [revision])

  async function save(request: UpdateProfileRequest): Promise<boolean> {
    if (savingRef.current || !profile) return false
    const session = sessionRef.current
    if (!sameSession(session)) {
      setProfile(null); setCapital(null); setLoadError('unauthorized')
      return false
    }
    const currentSession = getAuthSession()!
    if (session!.user.displayName !== currentSession.user.displayName || session!.user.email !== currentSession.user.email) {
      window.dispatchEvent(new window.Event('focus'))
      return false
    }
    const controller = new AbortController()
    saveController.current = controller
    savingRef.current = true
    setSaving(true)
    setSaveError(null)
    setFieldErrors({})
    try {
      const result = await profileApi.updateProfile(request, controller.signal)
      if (controller.signal.aborted) return false
      if (!sameSession(session)) {
        setProfile(null); setCapital(null); setLoadError('unauthorized')
        return false
      }
      if (result.id !== session!.user.id) throw new ProfileApiError(502, 'invalid_response')
      const current = getAuthSession()!
      if (session!.user.displayName !== current.user.displayName || session!.user.email !== current.user.email) {
        window.dispatchEvent(new window.Event('focus'))
        return false
      }
      setProfile(result)
      setSessionWarning(!saveAuthSession({ ...current, user: { id: result.id, displayName: result.displayName, email: result.email } }))
      sessionRef.current = getAuthSession()
      return true
    } catch (error) {
      if (!controller.signal.aborted) {
        if (errorCode(error) === 'unauthorized' || !sameSession(session)) {
          if (sameSession(session)) clearAuthSession()
          loadController.current?.abort()
          setProfile(null); setCapital(null); setLoadError('unauthorized')
        } else {
          setSaveError(errorCode(error))
          if (error instanceof ProfileApiError) setFieldErrors(error.fieldErrors)
        }
      }
      return false
    } finally {
      if (saveController.current === controller) {
        savingRef.current = false
        if (!controller.signal.aborted) setSaving(false)
      }
    }
  }

  function clearSaveErrors(field?: keyof UpdateProfileRequest) {
    const remaining = field ? Object.fromEntries(Object.entries(fieldErrors)
      .filter(([key]) => key.toLowerCase() !== field.toLowerCase())) : {}
    setFieldErrors(remaining)
    if (!field || (saveError === 'email_already_registered' ? field === 'email'
      : saveError !== 'validation_error' || Object.keys(remaining).length === 0)) setSaveError(null)
  }

  function reload() {
    if (savingRef.current) return
    setProfile(null)
    setCapital(null)
    setLoading(true)
    setLoadError(null)
    setSaveError(null)
    setFieldErrors({})
    setSessionWarning(false)
    setRevision(value => value + 1)
  }
  return { profile, capital, loading, loadError, saveError, fieldErrors, saving, sessionWarning, save, reload, clearSaveErrors }
}
