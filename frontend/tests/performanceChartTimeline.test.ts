import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { portfolioApi, type PortfolioPerformanceHistory } from '../src/api/portfolioApi.ts'
import { saveAuthSession } from '../src/auth/authStorage.ts'
import { formatTradeAmount } from '../src/i18n/formatters.ts'

registerHooks({ load(url, context, next) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return next(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/portfolio', pretendToBeVisual: true })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document,
  HTMLElement: dom.window.HTMLElement, SVGElement: dom.window.SVGElement, Element: dom.window.Element,
  IS_REACT_ACT_ENVIRONMENT: true })
// JSDOM has no layout engine. Supply a fixed viewport while retaining the real
// MUI scales, generated line path, overlay and React event handlers.
const computedStyle = window.getComputedStyle.bind(window)
window.getComputedStyle = (element, pseudo) => new Proxy(computedStyle(element, pseudo), {
  get(target, property, receiver) {
    if (property === 'width') return '800px'
    if (property === 'height') return '320px'
    return Reflect.get(target, property, receiver)
  },
})
const { createRoot } = await import('react-dom/client')
const { PersonalPerformanceChart } = await import('../src/components/charts/PersonalPerformanceChart.tsx')
const { PerformanceLineChart } = await import('../src/components/charts/PerformanceLineChart.tsx')
const { i18n } = await import('../src/i18n/i18n.ts')
const dates = ['2026-10-31T12:00:00Z', '2026-10-31T13:00:00Z', '2026-11-03T13:00:00Z']
const history: PortfolioPerformanceHistory = {
  range: '1W', currency: 'CAD', fromUtc: '2026-10-28T14:00:00Z', toUtc: '2026-11-04T14:00:00Z',
  sampleCount: 3, resolution: 'observation', firstRecordedAtUtc: dates[0], lastRecordedAtUtc: dates[2],
  points: dates.map((recordedAtUtc, index) => ({ recordedAtUtc,
    totalValue: ['99999.999999999999', '100000.000000000001', '100100.5'][index],
    source: 'Cash', quoteAsOfUtc: null })),
}
beforeEach(async () => {
  window.localStorage.clear()
  await i18n.changeLanguage('en')
  saveAuthSession({ accessToken: 'timeline-owner', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z',
    user: { id: 'timeline-owner', displayName: 'Owner', email: 'timeline@example.test' } })
})
async function mount(element: React.ReactElement) {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(element))
  return { container, async close() { await act(async () => root.unmount()); container.remove() } }
}
function coordinates(container: HTMLElement) {
  const path = container.querySelector('.MuiLineChart-line')?.getAttribute('d')
  assert.ok(path, 'The real MUI line must be rendered')
  return [...path.matchAll(/[ML](-?[\d.]+),(-?[\d.]+)/g)].map(match => Number(match[1]))
}
function closeTo(actual: number, expected: number, tolerance = 0.01) {
  assert.ok(Math.abs(actual - expected) < tolerance, `${actual} must be close to ${expected}`)
}
for (const language of ['en', 'fr']) {
  test(`recorded history preserves one-hour versus three-day gaps and matching interactions in ${language}`, async t => {
    await i18n.changeLanguage(language)
    t.mock.method(portfolioApi, 'getPerformanceHistory', async () => history)
    const view = await mount(React.createElement(PersonalPerformanceChart, { range: '1W', ariaLabel: 'Recorded timeline' }))
    try {
      const x = coordinates(view.container)
      assert.equal(x.length, 3)
      closeTo((x[2] - x[1]) / (x[1] - x[0]), 72, 0.05)
      const points = view.container.querySelectorAll<SVGGElement>('.financial-chart-point')
      assert.equal(points.length, 3)
      for (let index = 0; index < points.length; index++) {
        await act(async () => points[index].dispatchEvent(new dom.window.FocusEvent('focusin', { bubbles: true })))
        closeTo(Number(points[index].querySelector('circle')?.getAttribute('cx')), x[index])
        closeTo(Number(view.container.querySelector('.financial-chart-crosshair')?.getAttribute('x1')), x[index])
        assert.ok(view.container.querySelector('.financial-chart-value')?.textContent?.includes(
          formatTradeAmount(history.points[index].totalValue, language, 2, 'CAD')))
        assert.match(points[index].getAttribute('aria-label') ?? '', /UTC/)
      }
      const middleHit = points[1].querySelector('rect')!
      closeTo(Number(middleHit.getAttribute('x')), (x[0] + x[1]) / 2)
      closeTo(Number(middleHit.getAttribute('width')), (x[2] - x[0]) / 2)
      await act(async () => points[1].dispatchEvent(new dom.window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true })))
      assert.equal(points[1].getAttribute('aria-pressed'), 'true')
      await act(async () => points[1].dispatchEvent(new dom.window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true })))
      assert.equal(points[1].getAttribute('aria-pressed'), 'false')
    } finally { await view.close() }
  })
}
const legacyProps = { values: [100, 110, 130], labels: ['First', 'Second', 'Third'],
  ariaLabel: 'Legacy timeline', formatValue: (value: number) => `$${value}`, unavailableMessage: 'personalHistory.empty', curve: 'linear' as const }
test('shared charts without timestamps retain their existing equally spaced categories', async () => {
  const view = await mount(React.createElement(PerformanceLineChart, legacyProps))
  try {
    const x = coordinates(view.container)
    assert.equal(x.length, 3)
    closeTo((x[2] - x[1]) / (x[1] - x[0]), 1)
    assert.equal(view.container.querySelector('.financial-chart-point')?.getAttribute('aria-label'), 'First: $100')
  } finally { await view.close() }
})
for (const timestamps of [[Date.parse(dates[0])], [NaN, Date.parse(dates[1]), Date.parse(dates[2])], dates.map(Date.parse).reverse()]) {
  test(`unusable timestamps cannot silently revert to equally spaced observations: ${timestamps.join(',')}`, async () => {
    const view = await mount(React.createElement(PerformanceLineChart, { ...legacyProps, timestamps }))
    try {
      assert.equal(view.container.querySelector('.financial-chart-point') === null, true)
      assert.match(view.container.textContent ?? '', /No observation.*recorded/i)
    } finally { await view.close() }
  })
}
