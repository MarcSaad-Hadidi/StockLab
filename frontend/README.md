# React + TypeScript + Vite

## StockLab navigation

Run `npm run dev`, or `npm run build` followed by `npm run preview`.
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

Run `node --experimental-strip-types --test tests/*.test.ts` for frontend tests.
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
