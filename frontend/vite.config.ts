import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import type { Plugin } from 'vite'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const rootDir = fileURLToPath(new URL('.', import.meta.url))
const routePaths = new Set(['/register', '/login', '/not-found'])
const documentPaths = new Set(['/', '/market.html', '/dashboard.html'])
const assetPrefixes = ['/@', '/src/', '/node_modules/', '/assets/']

function pageRoutes(): Plugin {
  const rewriteRequest = (request: { url?: string }) => {
    if (!request.url) return

    const parsedUrl = new URL(request.url, 'http://localhost')
    const pathname = parsedUrl.pathname
    const routePath = pathname.endsWith('/') ? pathname.slice(0, -1) : pathname

    if (routePaths.has(routePath)) {
      parsedUrl.pathname = `${routePath}/`
      request.url = `${parsedUrl.pathname}${parsedUrl.search}`
      return
    }

    const isAsset = assetPrefixes.some((prefix) => pathname.startsWith(prefix))
    const isFile = pathname.includes('.')
    if (!documentPaths.has(pathname) && !isAsset && !isFile && pathname !== '/') request.url = '/not-found/'
  }

  return {
    name: 'stocklab-page-routes',
    configureServer(server) {
      server.middlewares.use((request, _response, next) => {
        rewriteRequest(request)
        next()
      })
    },
    configurePreviewServer(server) {
      server.middlewares.use((request, _response, next) => {
        rewriteRequest(request)
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
        notFound: resolve(rootDir, 'not-found/index.html'),
      },
    },
  },
})
