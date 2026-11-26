import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'

registerHooks({ load(url, context, next) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return next(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/market?symbol=AAPL', pretendToBeVisual: true })
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
const { FinancialLineChart } = await import('../src/components/charts/FinancialLineChart.tsx')
const values = Array.from({ length: 1200 }, (_, index) => 100 + index)
const labels = values.map((_, index) => `Observation ${index}`)
const props = { values, labels, min: 90, max: 1400, ariaLabel: 'AAPL historical prices',
  formatValue: (value: number) => `$${value}`, showLatestValue: true,
  stockCursor: { tickIndexes: [0, 300, 600, 900, 1199], keyboardInstructions: 'Use arrow keys' } }
async function mount() {
  const container = document.createElement('div')
  document.body.append(container)
  const root = createRoot(container)
  const render = (key: string) => act(async () => root.render(React.createElement(FinancialLineChart, { ...props, key })))
  await render('1D')
  const hit = () => container.querySelector<SVGRectElement>('.stock-chart-cursor')!
  const hair = () => container.querySelector('.financial-chart-crosshair')
  const price = () => container.querySelector('.financial-chart-value')?.textContent
  const tip = () => container.querySelector('.financial-chart-tooltip')?.getAttribute('transform')
  const dispatch = (type: string, fraction: number, y: number, pointerType = 'mouse') => {
    const target = hit()
    const [left, top, width, height] = ['x', 'y', 'width', 'height'].map(name => Number(target.getAttribute(name)))
    target.getBoundingClientRect = () => new dom.window.DOMRect(left + 50, top + 70, width, height)
    const event = new dom.window.MouseEvent(type, { bubbles: true, clientX: left + 50 + fraction * width, clientY: top + 70 + y * height })
    Object.assign(event, { pointerType, pointerId: 1 })
    target.dispatchEvent(event)
  }
  const frame = () => new Promise<void>(resolve => window.requestAnimationFrame(() => resolve()))
  return { container, render, hit, hair, price, tip, dispatch, frame,
    async close() { await act(async () => root.unmount()); container.remove() } }
}
test('dense stock chart uses one hit area; crosshair follows the cursor while price snaps to the nearest bar', async () => {
  const view = await mount()
  try {
    assert.equal(view.container.querySelectorAll('.financial-chart-hit').length, 1)
    assert.equal(view.hit().getAttribute('aria-valuemax'), '1200')
    const left = Number(view.hit().getAttribute('x'))
    const width = Number(view.hit().getAttribute('width'))
    await act(async () => { view.dispatch('pointermove', .2, .25); view.dispatch('pointermove', .25, .5); await view.frame() })
    assert.ok(Math.abs(Number(view.hair()?.getAttribute('x1')) - (left + width * .25)) < 1)
    assert.equal(view.price(), '$400')
    assert.equal(view.container.querySelector('.financial-chart-date')?.textContent, 'Observation 300')
    const before = view.tip()
    await act(async () => { view.dispatch('pointermove', .25, .8); await view.frame() })
    assert.notEqual(view.tip(), before, 'tooltip moves vertically even between observations')
    await act(async () => view.dispatch('pointerout', 0, 0))
    assert.equal(view.hair(), null)
    assert.equal(view.price(), '$1299')
  } finally { await view.close() }
})
test('keyboard and touch select actual observations; cancel or range switch clears the temporary cursor', async () => {
  const view = await mount()
  const key = (value: string) => act(async () => view.hit().dispatchEvent(new dom.window.KeyboardEvent('keydown', { key: value, bubbles: true })))
  try {
    await key('Home')
    assert.equal(view.hit().getAttribute('aria-valuenow'), '1')
    await key('ArrowRight')
    assert.equal(view.hit().getAttribute('aria-valuetext'), 'Observation 1: $101')
    await key('End')
    assert.equal(view.price(), '$1299')
    await key('Escape')
    assert.equal(view.hair(), null)
    await act(async () => { view.dispatch('pointerdown', .7, .5, 'touch'); await view.frame() })
    assert.ok(view.hair())
    await act(async () => view.dispatch('pointercancel', .7, .5, 'touch'))
    assert.equal(view.hair(), null)
    await act(async () => {
      view.dispatch('pointerdown', .7, .5, 'touch')
      view.dispatch('pointermove', .5, .5, 'touch')
      view.dispatch('pointerup', .5, .5, 'touch')
    })
    assert.equal(view.price(), '$700')
    await view.render('3M')
    assert.equal(view.hair(), null, 'new range clears a pinned selection at the same series length')
  } finally { await view.close() }
})
