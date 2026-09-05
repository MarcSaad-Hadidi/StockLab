import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import type { Plugin } from 'vite'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const rootDir = fileURLToPath(new URL('.', import.meta.url))

function registerRoute(): Plugin {
  return {
    name: 'stocklab-register-route',
    configureServer(server) {
      server.middlewares.use((request, _response, next) => {
        if (request.url === '/register') request.url = '/register/'
        next()
      })
    },
    configurePreviewServer(server) {
      server.middlewares.use((request, _response, next) => {
        if (request.url === '/register') request.url = '/register/'
        next()
      })
    },
  }
}

function loginRoute(): Plugin {
  return {
    name: 'stocklab-login-route',
    configureServer(server) {
      server.middlewares.use((request, _response, next) => {
        if (request.url === '/login') request.url = '/login/'
        next()
      })
    },
    configurePreviewServer(server) {
      server.middlewares.use((request, _response, next) => {
        if (request.url === '/login') request.url = '/login/'
        next()
      })
    },
  }
}

function profileRoute(): Plugin {
  const normalizeRoute = (url: string | undefined) => {
    if (!url) return url
    const parsedUrl = new URL(url, 'http://localhost')
    if (parsedUrl.pathname !== '/profile' && parsedUrl.pathname !== '/profile/') return url
    return `/profile/${parsedUrl.search}`
  }

  return {
    name: 'stocklab-profile-route',
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

function alertsRoute(): Plugin {
  const normalizeRoute = (url: string | undefined) => {
    if (!url) return url
    const parsedUrl = new URL(url, 'http://localhost')
    if (parsedUrl.pathname !== '/alerts' && parsedUrl.pathname !== '/alerts/') return url
    return `/alerts/${parsedUrl.search}`
  }

  return {
    name: 'stocklab-alerts-route',
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

function portfolioRoute(): Plugin {
  const normalizeRoute = (url: string | undefined) => {
    if (!url) return url
    const parsedUrl = new URL(url, 'http://localhost')
    if (parsedUrl.pathname !== '/portfolio' && parsedUrl.pathname !== '/portfolio/') return url
    return `/portfolio/${parsedUrl.search}`
  }

  return {
    name: 'stocklab-portfolio-route',
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

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), registerRoute(), loginRoute(), profileRoute(), alertsRoute(), portfolioRoute()],
  build: {
    rollupOptions: {
      input: {
        main: resolve(rootDir, 'index.html'),
        market: resolve(rootDir, 'market.html'),
        register: resolve(rootDir, 'register/index.html'),
        login: resolve(rootDir, 'login/index.html'),
        profile: resolve(rootDir, 'profile/index.html'),
        alerts: resolve(rootDir, 'alerts/index.html'),
        portfolio: resolve(rootDir, 'portfolio/index.html'),
      },
    },
  },
})
