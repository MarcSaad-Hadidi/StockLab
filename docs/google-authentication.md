# Google authentication

StockLab uses the Google Identity Services popup authorization-code flow with
the `openid email profile` scopes. The backend exchanges the code with Google,
validates the signed Google identity, and issues the same StockLab JWT used by
email/password login. Google access and refresh tokens are not retained.

## Create the Google client

Follow Google's [client setup guide](https://developers.google.com/identity/oauth2/web/guides/get-google-api-clientid)
and [authorization-code guide](https://developers.google.com/identity/oauth2/web/guides/use-code-model).

1. Create or select a Google Cloud project and configure its Google Auth Platform
   branding, audience and contact information. Add test users if the application
   is in testing mode.
2. Create an OAuth client of type **Web application**.
3. For the current local preview, add `http://127.0.0.1:5189` as an authorized
   JavaScript origin and as an authorized redirect URI. Popup code exchange uses
   the calling page's origin as its redirect URI, without a trailing slash.
   If using another host or port, configure its exact origin everywhere instead.
4. Keep the client secret on the backend. Do not put it in a `VITE_*` variable,
   source file, screenshot, issue, PR or chat message.

Use a separate client for production. Its origins must use HTTPS. If a production
host sets Cross-Origin-Opener-Policy, configure `same-origin-allow-popups` on the
authentication pages so the Google popup can communicate with its opener.

## Configure the backend

Apply the EF migrations before starting the new API. For development, use the
existing ASP.NET Core user-secrets store; the values below are placeholders:

```powershell
dotnet user-secrets --project backend/StockLab.Api set "GoogleAuth:ClientId" "<google-web-client-id>"
dotnet user-secrets --project backend/StockLab.Api set "GoogleAuth:ClientSecret" "<google-client-secret>"
dotnet user-secrets --project backend/StockLab.Api set "GoogleAuth:AllowedOrigins:0" "http://127.0.0.1:5189"
dotnet user-secrets --project backend/StockLab.Api set "GoogleAuth:Enabled" "true"
```

Enter real values privately on the development machine. User secrets are a local
development facility; use the deployment's secret store or environment variables
in production: `GoogleAuth__ClientId`, `GoogleAuth__ClientSecret`,
`GoogleAuth__AllowedOrigins__0` and `GoogleAuth__Enabled`.

The allowed origin must match the browser origin and the Google console exactly.
The API rejects an unlisted Origin or missing expected custom header. It does not
accept a redirect URI supplied in a login payload. With Google disabled, its
configuration endpoint reports that state and no StockLab session is created.
Invalid enabled configuration fails startup rather than falling back to a fake
identity.

## Account behavior

- A new verified Google identity creates a normal user and its initial paper
  trading portfolio. It has no local password until one is set through recovery.
- A returning linked identity is located by Google's stable `sub`, not by a
  caller-supplied email. Login does not overwrite the user's edited profile.
- An existing email address is never linked automatically. The user must also
  prove the existing StockLab password before linking. Link proofs expire, have
  a bounded number of attempts and cannot be used after account recovery.
- Password recovery sets a local password, revokes previous StockLab sessions and
  removes the Google link in one database write. This lets the mailbox owner
  recover all access even if an old external Google email claim is stale. The
  user can link Google again by proving the new password. An ordinary change that
  verifies the current password preserves the Google link.

Google documents why verified third-party email claims can become stale in its
[server-side identity validation guide](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token).

## Verify with a real Google client

Do not count isolated provider/SDK test fixtures as a Google end-to-end login.
After the real client has been configured, verify:

1. First Google sign-in from Register creates one user and one portfolio, then
   opens Dashboard with the selected language.
2. Returning Google sign-in opens the same account and uses the existing session
   protection and profile language rules.
3. A matching email requires the StockLab password; an incorrect password never
   creates a session or links the identities.
4. Closing the popup, denying consent, blocking the popup, or a provider failure
   displays a recoverable error and no success/session.
5. Password recovery revokes earlier JWTs and Google linking proofs. Re-linking
   requires the new password.

Without a configured real Google client, only the unavailable/configuration
states and the ordinary email/password flow can be verified against the real
local application. No simulated Google authentication is built into StockLab.
