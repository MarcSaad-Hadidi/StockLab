# React + TypeScript + Vite

## StockLab navigation

Run `npm run dev`, or `npm run build` followed by `npm run preview`.
The legacy `npm run build:dashboard` command uses the same multi-page configuration
and writes all linked pages to `dist-dashboard`. Preview that output with
`npm run preview -- --config vite.dashboard.config.ts`.
The existing multipage app uses full-document navigation; no client router or
authentication service is required. Logout only navigates to Login.

`src/navigation/routes.ts` is the shared registry for page destinations and HTML
entries. It drives both page links and the Vite dev/preview middleware. Canonical
routes are `/dashboard`, `/market`, `/portfolio`, `/transactions`, `/watchlist`,
`/alerts`, `/ai-trader`, `/profile`, `/login`, `/register` and `/not-found`.
Trailing slashes and previous HTML entry URLs remain supported, as do Market's
stock-detail query parameters. Unknown page paths display Not Found. Static assets
remain served by Vite. The existing root template at `/` is preserved.

Sidebars mark their displayed page with `aria-current="page"`; navigation loads
the destination page rather than changing a local menu selection. Settings links
open Profile. Controls for unimplemented features are otherwise unchanged.

Run `npm run test` for frontend tests, including real HTTP checks against dev and
built preview for both Vite configurations. Vite runs in `mpa` mode so missing
static files cannot fall back to the root HTML document.
For deployment outside Vite, configure the host to map these URLs to the same HTML
entries and provide the Not Found fallback.

This template provides a minimal setup to get React working in Vite with HMR and some ESLint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react) uses [Oxc](https://oxc.rs)
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react-swc) uses [SWC](https://swc.rs/)

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the ESLint configuration

If you are developing a production application, we recommend updating the configuration to enable type-aware lint rules:

```js
export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      // Other configs...

      // Remove tseslint.configs.recommended and replace with this
      tseslint.configs.recommendedTypeChecked,
      // Alternatively, use this for stricter rules
      tseslint.configs.strictTypeChecked,
      // Optionally, add this for stylistic rules
      tseslint.configs.stylisticTypeChecked,

      // Other configs...
    ],
    languageOptions: {
      parserOptions: {
        project: ['./tsconfig.node.json', './tsconfig.app.json'],
        tsconfigRootDir: import.meta.dirname,
      },
      // other options...
    },
  },
])

```

You can also install [eslint-plugin-react-x](https://npmx.dev/package/eslint-plugin-react-x) and [eslint-plugin-react-dom](https://npmx.dev/package/eslint-plugin-react-dom) for React-specific lint rules:

```js
// eslint.config.js
import reactX from 'eslint-plugin-react-x'
import reactDom from 'eslint-plugin-react-dom'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      // Other configs...
      // Enable lint rules for React
      reactX.configs['recommended-typescript'],
      // Enable lint rules for React DOM
      reactDom.configs.recommended,
    ],
    languageOptions: {
      parserOptions: {
        project: ['./tsconfig.node.json', './tsconfig.app.json'],
        tsconfigRootDir: import.meta.dirname,
      },
      // other options...
    },
  },
])

```


## Market integration (#82)

The central typed client in `src/api/marketDataApi.ts` calls only StockLab endpoints.
Vite dev and preview proxy `/api` to `http://localhost:5274`; optionally configure the
public `VITE_STOCKLAB_API_BASE_URL`. Never put provider credentials in Vite or frontend.

Market automatically loads five curated identifiers (AAPL, MSFT, NVDA, AMZN, GOOGL)
with at most three concurrent quote requests. They contain no simulated prices.
Search is debounced 450ms, cancelled when stale, and never calls the API for an empty
query. Search rows use provider metadata without an N+1 quote fanout. Popular-company logos load automatically (five maximum). Search and movers reuse cached logos or a ticker fallback, with no logo fanout. Clicking a result preserves its exchange-qualified symbol in the route.

Featured gainers/losers are explicitly labelled as a ranking of those five quotes.
The user can load the separate latest available EOD market movers on demand. This
single call supplies both lists. Provider errors never become fabricated movers.
Stock Details quotes/history load independently. Fundamentals + earnings load on explicit demand to protect the 25/day enrichment
plan. Company logos load automatically for each visible company. Partial
failures preserve the quote and chart. Logos use a public credential-free CDN URL;
missing or broken images fall back to the ticker letter. Analyst counts are labelled
individually and are not StockLab AI predictions. AI remains unavailable.

All chart points come from returned OHLCV bars. 1D uses Minute bars for 24 hours ending
at the last quote observation (+ one minute to include its bar), including when the
market is closed. 5D uses Hour bars; 1M/3M/6M/1Y use calendar month subtraction with
month-end clamping; YTD begins January 1; 5Y uses Week; Max uses Month across the
backend-supported 4999-day span (about 13 years), not all-time history. Calendar bars
preserve PeriodDate, intraday bars display OpenTimeUtc. Range responses have a bounded
five-minute memory cache. No prefetch, polling, automatic retry or synthetic points.

Run `npm run lint`, `npm run build`, `npm test`; development: `npm run dev -- --host
127.0.0.1`, preview: `npm run preview -- --host 127.0.0.1`. The API must be running for
market data. Its committed default remains Mock for offline development; final local
real-data verification uses TwelveData configured externally. Trading controls only preview estimates. Order submission, watchlist saving and alert saving are disabled until their dedicated services exist.


## Account data audit (#82)

| Page | Runtime source / honest state |
| --- | --- |
| Market / Stock Details | Twelve quote/search/history; optional Alpha metadata, logos, earnings and EOD movers |
| Dashboard | No account balances, holdings, watchlist, trades or synthetic performance; cards and tables retained |
| Portfolio | Empty holdings and performance, unavailable totals; never substitute a stock chart for account value |
| Transactions | Empty history; execution prices are never replaced with current prices; filters/table retained |
| Watchlist | Empty list pending persistence; no manufactured saved stocks or market status |
| Alerts | Empty alerts; draft asset search uses Twelve; saving/monitoring unavailable |
| AI Trader | Empty positions/decisions/trades/model/backtests; metrics unavailable and activation disabled |
| Profile | Explicit preview label; existing profile mock remains a known auth/persistence gap |
| Login / Register | Existing authentication previews, unchanged; no real auth in #82 |

All business seed arrays and generated 2024 transactions were removed from production
modules. Transaction test fixtures remain under `tests/`; backend MockMarketDataProvider
remains available for explicit offline development. Curated symbols, UI range labels,
filter options, icon geometry and CSS colors are presentation constants, not observations.
No fake quote/history/fundamentals are substituted on API failure. The shared StockLogo
uses only a public logo returned by StockLab or the requested symbol's first letter.

EN/FR unavailable states distinguish unconnected account services from market API errors.
No account backend is fabricated here: persistence, trading, alert delivery and ML remain
separate future work. The original cards, sections, filters and AI tabs remain available.
