import { routeFor } from '../navigation/routes.ts'

export const marketRoute = routeFor('market')

export function stockDetailsRoute(symbol: string): string {
  return `${marketRoute}?symbol=${encodeURIComponent(symbol.toLowerCase())}`
}
