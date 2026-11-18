export type GoogleCodeResponse = { code?: string; error?: string }
export type GooglePopupError = { type?: 'popup_closed' | 'popup_failed_to_open' | 'unknown' }
export type GoogleCodeClient = { requestCode(): void }
export type GoogleCodeClientConfig = {
  client_id: string
  scope: 'openid email profile'
  ux_mode: 'popup'
  include_granted_scopes: false
  select_account: true
  callback(response: GoogleCodeResponse): void
  error_callback(error: GooglePopupError): void
}
export type GoogleOAuth2 = { initCodeClient(config: GoogleCodeClientConfig): GoogleCodeClient }

function sdk(): GoogleOAuth2 | undefined {
  const candidate = (window as Window & { google?: { accounts?: { oauth2?: GoogleOAuth2 } } }).google?.accounts?.oauth2
  return typeof candidate?.initCodeClient === 'function' ? candidate : undefined
}

let loading: Promise<GoogleOAuth2> | undefined
export function loadGoogleIdentitySdk(): Promise<GoogleOAuth2> {
  const ready = sdk()
  if (ready) return Promise.resolve(ready)
  if (loading) return loading
  const script = document.createElement('script')
  script.src = 'https://accounts.google.com/gsi/client'
  script.async = true
  script.defer = true
  const pending = new Promise<GoogleOAuth2>((resolve, reject) => {
    const cleanup = () => {
      window.clearTimeout(timer)
      script.removeEventListener('load', loaded)
      script.removeEventListener('error', failed)
      loading = undefined
    }
    const failed = () => {
      cleanup()
      script.remove()
      reject(new Error('Google Identity Services could not be loaded.'))
    }
    const loaded = () => {
      const identity = sdk()
      if (!identity) { failed(); return }
      cleanup()
      resolve(identity)
    }
    const timer = window.setTimeout(failed, 15000)
    script.addEventListener('load', loaded)
    script.addEventListener('error', failed)
    document.head.append(script)
  })
  loading = pending
  return pending
}
