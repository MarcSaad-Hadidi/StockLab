export const marketRoute = '/market.html'

export function stockDetailsRoute(symbol: string): string {
  return `${marketRoute}?symbol=${encodeURIComponent(symbol.toLowerCase())}`
}
