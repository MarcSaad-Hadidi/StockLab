export type TrendTone = 'positive' | 'negative' | 'neutral'

export type TrendIcon = 'trending-up' | 'trending-down'

export function getTrendTone(value: number | null | undefined): TrendTone {
  if (value == null || !Number.isFinite(value) || value === 0) return 'neutral'
  return value > 0 ? 'positive' : 'negative'
}

export function getTrendIcon(tone: TrendTone): TrendIcon | null {
  if (tone === 'positive') return 'trending-up'
  if (tone === 'negative') return 'trending-down'
  return null
}

export function getTrendClass(tone: TrendTone) {
  return `metric-change-${tone}`
}
