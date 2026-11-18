import { saveAuthSession, type AuthSession } from './authStorage'
import { setLanguage } from '../i18n/i18n'
import { clearLoginLanguage, getLoginLanguage, getPendingLanguageIntent, saveLanguagePreference } from '../i18n/languagePreference'
import { routeFor } from '../navigation/routes'

export class LoginCompletionError extends Error {
  readonly translationKey = 'login.errors.sessionStorage'
  constructor() { super('The browser could not save the StockLab session.') }
}

// Password and Google login must apply the same account/language/session rules.
export async function completeLogin(session: AuthSession, isCurrent: () => boolean = () => true) {
  const ensureCurrent = () => { if (!isCurrent()) throw new DOMException('Login cancelled', 'AbortError') }
  ensureCurrent()
  const explicit = getLoginLanguage()
  const pending = getPendingLanguageIntent(session.user.id)
  const language = explicit ?? pending?.language
  if (language) {
    const result = await saveLanguagePreference(language, session, undefined, true, explicit ? undefined : pending ?? undefined)
    session.user = { ...session.user, preferredLanguage: result.preferredLanguage }
  }
  ensureCurrent()
  if (session.user.preferredLanguage) await setLanguage(session.user.preferredLanguage)
  ensureCurrent()
  if (!saveAuthSession(session)) throw new LoginCompletionError()
  clearLoginLanguage()
  window.location.assign(routeFor('dashboard'))
}
