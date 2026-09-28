export const authStorageKey = 'stocklab-auth'

export type AuthUser = {
  id: string
  displayName: string
  email: string
}

export type AuthSession = {
  accessToken: string
  tokenType: 'Bearer'
  expiresAtUtc: string
  user: AuthUser
}

type AuthStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

function browserStorage(): AuthStorage | undefined {
  if (typeof window === 'undefined') return undefined
  try {
    return window.localStorage
  } catch {
    return undefined
  }
}

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function nonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isAuthSession(value: unknown): value is AuthSession {
  if (!record(value) || value.tokenType !== 'Bearer' || !nonEmptyString(value.accessToken) || !nonEmptyString(value.expiresAtUtc)) return false
  if (!Number.isFinite(Date.parse(value.expiresAtUtc))) return false
  if (!record(value.user)) return false
  return nonEmptyString(value.user.id) && nonEmptyString(value.user.displayName) && nonEmptyString(value.user.email)
}

export function saveAuthSession(session: AuthSession, storage: AuthStorage | undefined = browserStorage()) {
  try {
    storage?.setItem(authStorageKey, JSON.stringify(session))
  } catch {
    // Storage can be blocked by browser privacy settings; API calls still remain safe.
  }
}

export function clearAuthSession(storage: AuthStorage | undefined = browserStorage()) {
  try {
    storage?.removeItem(authStorageKey)
  } catch {
    // A blocked storage must not prevent the caller from leaving the account.
  }
}

export function getAuthSession(storage: AuthStorage | undefined = browserStorage()): AuthSession | null {
  let raw: string | null = null
  try {
    raw = storage?.getItem(authStorageKey) ?? null
  } catch {
    return null
  }
  if (!raw) return null

  try {
    const value: unknown = JSON.parse(raw)
    if (!isAuthSession(value) || Date.parse(value.expiresAtUtc) <= Date.now()) {
      clearAuthSession(storage)
      return null
    }
    return value
  } catch {
    clearAuthSession(storage)
    return null
  }
}

export function getAuthorizationHeader(storage: AuthStorage | undefined = browserStorage()): { Authorization: string } | null {
  const session = getAuthSession(storage)
  return session ? { Authorization: `${session.tokenType} ${session.accessToken}` } : null
}
