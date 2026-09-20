import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { test } from 'node:test'

const pageSource = fileURLToPath(new URL('../src/ai-trader/AITraderPage.tsx', import.meta.url))
const stylesheet = fileURLToPath(new URL('../src/ai-trader/ai-trader.css', import.meta.url))

test('AI Trader metric cards contain only text content and their change value', async () => {
  const [tsx, css] = await Promise.all([readFile(pageSource, 'utf8'), readFile(stylesheet, 'utf8')])

  assert.match(
    tsx,
    /function MetricCard\(\{ label, value, change, tone, trend = 'neutral' \}: \{ label: string; value: string; change\?: string; tone: string; trend\?: TrendTone \}\)/,
  )
  assert.doesNotMatch(tsx, /change\.startsWith\('-'\)/)
  assert.doesNotMatch(tsx, /<span className="metric-icon">/)
  assert.doesNotMatch(tsx, /<MetricCard[^>]*\bicon=/)
  assert.doesNotMatch(css, /\.metric-(?:icon|blue \.metric-icon|green \.metric-icon|purple \.metric-icon|orange \.metric-icon|red \.metric-icon)\b/)

  for (const name of ['bell', 'briefcase', 'clock', 'edit', 'grid', 'mail', 'menu', 'pause', 'search', 'sparkles', 'star', 'user', 'x']) {
    assert.doesNotMatch(tsx, new RegExp(`^\\s*(?:\\| '${name}'|${name}:)`, 'm'), `${name} should not remain in AI Trader's icon renderer`)
  }
})
