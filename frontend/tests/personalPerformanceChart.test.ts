import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { portfolioApi, PortfolioApiError, type PortfolioPerformanceHistory } from '../src/api/portfolioApi.ts'
import { saveAuthSession } from '../src/auth/authStorage.ts'
import { formatTradeAmount } from '../src/i18n/formatters.ts'

registerHooks({ load(url, context, next) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.endsWith('/FinancialLineChart.tsx')) return { format: 'module', shortCircuit: true,
    source: `import React from 'react'; export function FinancialLineChart(p) { return React.createElement('div', { 'data-history-plot': true }, p.values.map((v, i) => React.createElement('button', { key: i, 'aria-label': p.pointLabel?.(i) }, p.formatPointValue?.(i) ?? p.formatValue(v)))) }` }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return next(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/portfolio' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document,
  HTMLElement: dom.window.HTMLElement, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { PersonalPerformanceChart } = await import('../src/components/charts/PersonalPerformanceChart.tsx')
const { i18n } = await import('../src/i18n/i18n.ts')
const history: PortfolioPerformanceHistory = {
  range: '1M', currency: 'CAD', fromUtc: '2026-10-01T12:00:00Z', toUtc: '2026-11-01T12:00:00Z',
  sampleCount: 2, resolution: 'observation', points: [
    { recordedAtUtc: '2026-10-31T12:00:00Z', totalValue: '99999.999999999999', source: 'Mock', quoteAsOfUtc: '2026-10-30T20:00:00Z' },
    { recordedAtUtc: '2026-11-01T12:00:00Z', totalValue: '10000.000000000001', source: 'Mock', quoteAsOfUtc: '2026-10-30T20:00:00Z' },
  ],
  firstRecordedAtUtc: '2026-10-31T12:00:00Z', lastRecordedAtUtc: '2026-11-01T12:00:00Z',
}
beforeEach(async () => {
  window.localStorage.clear(); await i18n.changeLanguage('en')
  saveAuthSession({ accessToken: 'chart-owner', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z',
    user: { id: 'chart-owner', displayName: 'Owner', email: 'chart@example.com' } })
})
async function mount() {
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(PersonalPerformanceChart, { range: '1M', ariaLabel: 'Recorded history' })))
  return { container, async close() { await act(async () => root.unmount()); container.remove() } }
}

for (const language of ['en', 'fr']) {
  test(`history chart displays original exact amounts, actual dates and demo provenance in ${language}`, async t => {
    await i18n.changeLanguage(language)
    t.mock.method(portfolioApi, 'getPerformanceHistory', async () => history)
    const view = await mount()
    try {
      assert.equal(view.container.querySelectorAll('[data-history-plot] button').length, 2)
      for (const point of history.points)
        assert.ok(view.container.textContent?.includes(formatTradeAmount(point.totalValue, language, 2, 'CAD')))
      assert.match(view.container.textContent ?? '', /UTC/)
      assert.match(view.container.textContent ?? '', /Mock|demo|démo|simul/i)
      assert.equal(view.container.querySelectorAll('[data-market-provenance]').length, 1)
    } finally { await view.close() }
  })
}
for (const count of [0, 1]) {
  test(`${count} recorded observations have an honest insufficient-history state`, async t => {
    t.mock.method(portfolioApi, 'getPerformanceHistory', async () => ({ ...history, sampleCount: count, points: history.points.slice(0, count),
      firstRecordedAtUtc: count ? history.points[0].recordedAtUtc : null, lastRecordedAtUtc: count ? history.points[count - 1].recordedAtUtc : null }))
    const view = await mount()
    try {
      assert.equal(view.container.querySelector('[data-history-plot]'), null)
      assert.match(view.container.textContent ?? '', count === 0 ? /No.*recorded/i : /one.*recorded/i)
      if (count === 1) assert.ok(view.container.textContent?.includes(formatTradeAmount(history.points[0].totalValue, 'en', 2, 'CAD')))
    } finally { await view.close() }
  })
}
test('history failures offer a real read retry without producing an empty successful graph', async t => {
  let calls = 0
  t.mock.method(portfolioApi, 'getPerformanceHistory', async () => {
    if (++calls === 1) throw new PortfolioApiError(503, 'server_error', 'unavailable')
    return history
  })
  const view = await mount()
  try {
    assert.ok(view.container.querySelector('[role="alert"]'))
    const retry = view.container.querySelector<HTMLButtonElement>('button')!
    await act(async () => retry.click())
    assert.equal(calls, 2); assert.ok(view.container.querySelector('[data-history-plot]'))
  } finally { await view.close() }
})

test('downsampled coverage states the full recorded span rather than only bucket winners', async t => {
  t.mock.method(portfolioApi, 'getPerformanceHistory', async () => ({ ...history, sampleCount: 1000,
    resolution: 'day', firstRecordedAtUtc: '2026-10-01T12:00:00Z' }))
  const view = await mount()
  try {
    assert.match(view.container.querySelector('.personal-history-footer')!.textContent ?? '', /Oct 1, 2026/)
    assert.match(view.container.textContent ?? '', /1,?000 recorded observations/)
    assert.match(view.container.textContent ?? '', /2 actual points/)
  } finally { await view.close() }
})
