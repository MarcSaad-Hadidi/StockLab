import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import type { Plugin } from 'vite'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const rootDir = fileURLToPath(new URL('.', import.meta.url))
const routePaths = new Set(['/register', '/login', '/profile'])

function pageRoutes(): Plugin {
  const normalizeRoute = (url: string | undefined) => {
    if (!url) return url
    const parsedUrl = new URL(url, 'http://localhost')
    const pathname = parsedUrl.pathname.endsWith('/') ? parsedUrl.pathname.slice(0, -1) : parsedUrl.pathname
    if (!routePaths.has(pathname)) return url
    parsedUrl.pathname = `${pathname}/`
    return `${parsedUrl.pathname}${parsedUrl.search}`
  }

  return {
    name: 'stocklab-page-routes',
    configureServer(server) {
      server.middlewares.use((request, _response, next) => {
        request.url = normalizeRoute(request.url)
        next()
      })
    },
    configurePreviewServer(server) {
      server.middlewares.use((request, _response, next) => {
        request.url = normalizeRoute(request.url)
        next()
      })
    },
  }
}

export default defineConfig({
  plugins: [react(), pageRoutes()],
  build: {
    rollupOptions: {
      input: {
        main: resolve(rootDir, 'index.html'),
        market: resolve(rootDir, 'market.html'),
        register: resolve(rootDir, 'register/index.html'),
        login: resolve(rootDir, 'login/index.html'),
        profile: resolve(rootDir, 'profile/index.html'),
      },
    },
  },
})
