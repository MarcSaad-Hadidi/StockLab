export const marketRoute = '/market'

export function stockDetailsRoute(symbol: string): string {
  return `${marketRoute}?symbol=${encodeURIComponent(symbol.toLowerCase())}`
}
