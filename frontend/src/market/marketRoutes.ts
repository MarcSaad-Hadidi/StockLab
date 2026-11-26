import { routeFor } from '../navigation/routes.ts'

export const marketRoute = routeFor('market')

export function stockDetailsRoute(symbol: string, instrumentType?: string | null): string {
  const params = new URLSearchParams({ symbol: symbol.toLowerCase() })
  if (instrumentType) params.set('instrumentType', instrumentType)
  return `${marketRoute}?${params}`
}
