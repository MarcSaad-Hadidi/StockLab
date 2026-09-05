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

function watchlistRoute(): Plugin {
  const normalizeRoute = (url: string | undefined) => {
    if (!url) return url
    const parsedUrl = new URL(url, 'http://localhost')
    if (parsedUrl.pathname !== '/watchlist' && parsedUrl.pathname !== '/watchlist/') return url
    return `/watchlist/${parsedUrl.search}`
  }

  return {
    name: 'stocklab-watchlist-route',
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

function aiTraderRoute(): Plugin {
  const normalizeRoute = (url: string | undefined) => {
    if (!url) return url
    const parsedUrl = new URL(url, 'http://localhost')
    if (parsedUrl.pathname !== '/ai-trader' && parsedUrl.pathname !== '/ai-trader/') return url
    return `/ai-trader/${parsedUrl.search}`
  }

  return {
    name: 'stocklab-ai-trader-route',
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

function notFoundRoute(): Plugin {
  const routePaths = new Set(['/register', '/login', '/profile', '/alerts', '/portfolio', '/watchlist', '/ai-trader', '/not-found'])
  const documentPaths = new Set(['/', '/market.html', '/dashboard.html'])
  const assetPrefixes = ['/@', '/src/', '/node_modules/', '/assets/']

  const rewriteRequest = (request: { url?: string }) => {
    if (!request.url) return
    const parsedUrl = new URL(request.url, 'http://localhost')
    const pathname = parsedUrl.pathname
    const routePath = pathname.endsWith('/') ? pathname.slice(0, -1) : pathname

    if (routePath === '/not-found') {
      request.url = `/not-found/${parsedUrl.search}`
      return
    }

    const isAsset = assetPrefixes.some((prefix) => pathname.startsWith(prefix))
    const isFile = pathname.includes('.')
    if (!routePaths.has(routePath) && !documentPaths.has(pathname) && !isAsset && !isFile) {
      request.url = '/not-found/'
    }
  }

  return {
    name: 'stocklab-not-found-route',
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

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), registerRoute(), loginRoute(), profileRoute(), alertsRoute(), portfolioRoute(), watchlistRoute(), aiTraderRoute(), notFoundRoute()],
  build: {
    rollupOptions: {
      input: {
        main: resolve(rootDir, 'index.html'),
        dashboard: resolve(rootDir, 'dashboard.html'),
        market: resolve(rootDir, 'market.html'),
        register: resolve(rootDir, 'register/index.html'),
        login: resolve(rootDir, 'login/index.html'),
        profile: resolve(rootDir, 'profile/index.html'),
        alerts: resolve(rootDir, 'alerts/index.html'),
        portfolio: resolve(rootDir, 'portfolio/index.html'),
        watchlist: resolve(rootDir, 'watchlist/index.html'),
        aiTrader: resolve(rootDir, 'ai-trader/index.html'),
        notFound: resolve(rootDir, 'not-found/index.html'),
      },
    },
  },
})
