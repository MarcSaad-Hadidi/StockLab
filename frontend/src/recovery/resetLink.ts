export type ResetLink = { userId: string; token: string }

// Called before mounting React so StrictMode cannot consume the fragment twice.
// Keep the secret in memory; refreshing the sanitized URL requires a new link.
export function consumeResetLink(): ResetLink | null {
  const url = new URL(window.location.href)
  const fragment = new URLSearchParams(url.hash.slice(1))
  const userId = fragment.get('userId') ?? ''
  const token = fragment.get('token') ?? ''
  const unique = fragment.getAll('userId').length === 1 && fragment.getAll('token').length === 1
  url.hash = ''
  // Discard credentials from old or malformed query-based links as well.
  for (const key of [...url.searchParams.keys()]) if (['userid', 'token'].includes(key.toLowerCase())) url.searchParams.delete(key)
  window.history.replaceState(window.history.state, '', `${url.pathname}${url.search}`)
  return unique && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(userId) && /^[0-9a-f]{64}$/i.test(token)
    ? { userId, token } : null
}
