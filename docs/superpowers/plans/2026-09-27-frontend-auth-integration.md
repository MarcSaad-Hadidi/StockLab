# Frontend authentication integration

## Goal

Connect the existing Login and Register pages to the backend authentication endpoints from issue #81 without changing their visual layout.

## Contract

- Register with `POST /api/auth/register` using `displayName`, `email`, and `password`.
- Login with `POST /api/auth/login` using `email` and `password`.
- Store the returned Bearer token and user identity in browser storage for later authenticated API calls.
- Redirect a successful login to the dashboard and a successful registration to login with a confirmation message.
- Translate safe API errors for validation, duplicate email, invalid credentials, rate limiting, offline access, and unexpected server responses.

## Tasks

1. Add tests for request payloads, response validation, safe error mapping, and auth session storage.
2. Add a typed auth API client and local-storage session helpers.
3. Replace Login's submitted mock state with a real login request and authenticated redirect.
4. Replace Register's submitted mock state with a real registration request and login redirect.
5. Add English/French messages and verify frontend lint, build, and tests.
