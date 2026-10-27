import { clearAuthSession, getAuthorizationHeader } from './authStorage'

/** A rejected request may invalidate only the token it actually sent. */
export function handleUnauthorized(response: Response, authorization: string, signal?: AbortSignal) {
  if (response.status === 401 && !signal?.aborted
    && getAuthorizationHeader()?.Authorization === authorization) clearAuthSession()
}
