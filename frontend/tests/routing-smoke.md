# Routing compatibility validation (#127)

Base: `origin/develop` at `b4eea0cf1b02da300a82fc7fd6240dcbb704c922`.
Branch: `fix/frontend-routing-develop-compatibility`.
Installed versions: React 19.2.8, Vite 8.2.2, plugin-react 6.1.0.

## Reproduced before the fix

- Missing assets such as `/assets/missing.js` received HTTP 200 with the root
  HTML document in dev and preview, despite bypassing `documentUrl()`.
- The dashboard configuration returned the wrong document for `/dashboard/`
  in dev and HTTP 404 in preview. Its output omitted the other sidebar pages.
- `pageForPath()` and `isCurrentPage()` did not recognize paths containing query
  strings. Regression tests failed before the corrections and pass afterward.

## Automated checks

- `npm run test`: 59 passing tests, including real builds and HTTP requests
  against dev and preview for both configurations.
- `npm run lint`, `npm run build`, `npm run build:dashboard`: passed.
- HTTP checks cover every registered route, trailing slashes, legacy HTML,
  query parameters, unknown pages, missing assets/modules and the favicon.

## Browser checks

Performed against `npm run dev` and `npm run preview` after the production build:

- Opened every registered route directly and refreshed it; checked document
  title, rendered heading and active sidebar destination where applicable.
- Opened and refreshed an unknown nested path: Not Found, with the URL retained.
- Clicked every sidebar link in English and French; destinations and active
  states remained correct. Restored English afterward.
- Market → AAPL → browser Back → Forward → refresh: correct page and URL.
- Direct Stock Details access through clean, trailing-slash and legacy HTML
  URLs retained the symbol. Preview refresh also retained extra encoded params.
- Tested the existing internal Back to Market button for an unknown symbol in
  both modes, and its browser history in dev. On this develop revision that
  button exists only for an unknown symbol; valid stock details use the Market
  sidebar link or browser Back. No new control was added.
- Checked dashboard/market HTML aliases and portfolio HTML/trailing-slash URLs
  with query strings in both modes. Checked Logout → Login in dev.

No page components, styles, route registrations or Stock Details URL format
were changed. Non-Vite hosting still requires the host rewrites described in
the frontend README.
