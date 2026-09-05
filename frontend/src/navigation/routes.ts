export const pages = {
  dashboard: { path: '/dashboard', entry: 'dashboard.html' },
  market: { path: '/market', entry: 'market.html' },
  portfolio: { path: '/portfolio', entry: 'portfolio/index.html' },
  transactions: { path: '/transactions', entry: 'transactions/index.html' },
  watchlist: { path: '/watchlist', entry: 'watchlist/index.html' },
  alerts: { path: '/alerts', entry: 'alerts/index.html' },
  'ai-trader': { path: '/ai-trader', entry: 'ai-trader/index.html' },
  profile: { path: '/profile', entry: 'profile/index.html' },
  login: { path: '/login', entry: 'login/index.html' },
  register: { path: '/register', entry: 'register/index.html' },
  'not-found': { path: '/not-found', entry: 'not-found/index.html' },
} as const

export type PageId = keyof typeof pages

// Labels and legacy navigation IDs share the same canonical destination.
export function routeFor(label: string): string {
  const id = label.toLowerCase().replaceAll(' ', '-')
  if (id === 'settings') return pages.profile.path
  if (id === 'logout') return pages.login.path
  return Object.hasOwn(pages, id) ? pages[id as PageId].path : pages['not-found'].path
}

export function pageForPath(pathname: string): PageId | undefined {
  const path = pathname.replace(/\/$/, '')
  return (Object.keys(pages) as PageId[]).find(id =>
    pages[id].path === path || `/${pages[id].entry}` === path)
}

export function isCurrentPage(label: string, pathname: string): boolean {
  const page = pageForPath(pathname)
  return page !== undefined && routeFor(label) === pages[page].path
}

// Used by both Vite dev and preview. Query strings (including stock details) survive.
export function documentUrl(url: string): string {
  const parsed = new URL(url, 'http://localhost')
  const page = pageForPath(parsed.pathname)
  if (page) return `/${pages[page].entry}${parsed.search}`
  if (parsed.pathname === '/' || parsed.pathname === '/index.html') return url
  if (['/@', '/src/', '/node_modules/', '/assets/'].some(prefix => parsed.pathname.startsWith(prefix))) return url
  // Leave static files to Vite, including missing assets which should not receive HTML.
  if (parsed.pathname.split('/').at(-1)?.includes('.')) return url
  return `/${pages['not-found'].entry}${parsed.search}`
}
