import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act } from 'react'
import { createAiTraderApi } from '../src/api/aiTraderApi.ts'
import { saveAuthSession, clearAuthSession } from '../src/auth/authStorage.ts'
import { bodies, performance, model, backtest, decision, trade, rejection, secondId } from './fixtures/aiTrader.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && /\.(js|cjs)$/.test(url) && context.format !== 'module') return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/ai-trader' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { default: AITraderPage } = await import('../src/ai-trader/AITraderPage.tsx')
beforeEach(async () => { window.localStorage.clear(); await i18n.changeLanguage('en'); saveAuthSession({ accessToken: 'test-token', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z', user: { id: 'a', displayName: 'Tester', email: 'a@example.test' } }) })
const settle = async () => { await act(async () => { await new Promise(resolve => setTimeout(resolve, 20)) }) }
async function mount(overrides: Record<string, unknown> = {}) {
  const paths: string[] = []
  const api = createAiTraderApi('', async input => {
    const path = String(input).split('/api/ai-trader/')[1].split('?')[0]; paths.push(path)
    const body = overrides[path] ?? (path.startsWith('trades/') ? trade : path.startsWith('decisions/') ? decision : bodies[path])
    if (body instanceof Response) return body.clone()
    return new Response(JSON.stringify(body))
  })
  const container = document.createElement('div'); document.body.append(container)
  const root = createRoot(container)
  await act(async () => root.render(React.createElement(AITraderPage, { api })))
  await settle()
  const click = async (text: string) => {
    const button = [...container.querySelectorAll('button')].find(item => item.textContent?.trim() === text)
    assert.ok(button, `Missing button: ${text}`)
    await act(async () => button.click()); await settle()
  }
  return { container, paths, click, close: async () => { await act(async () => root.unmount()); container.remove() } }
}
test('Overview renders only real API metrics and separates unavailable catalogs from empty histories', async () => {
  const view = await mount({ 'performance/history': [], models: new Response('{}', { status: 503 }), trades: [] })
  try {
    const cards = view.container.querySelector('.metric-grid')!.textContent!
    assert.match(cards, /\$100,000/); assert.match(cards, /\$108,000/); assert.match(cards, /\+\$8,000/); assert.match(cards, /\+8.00%/)
    assert.match(cards, /65.0%/); assert.match(cards, /20.00%/)
    assert.match(view.container.textContent!, /Model catalog unavailable/)
    assert.match(view.container.textContent!, /No trades yet/)
    assert.match(view.container.textContent!, /No historical performance data yet/)
    assert.doesNotMatch(view.container.textContent!, /Sharpe|Profit Factor|Healthy|Next Retrain|Target Price|Stop Loss|Complete.*History/)
    await view.click('Positions')
    const rows = view.container.querySelector('tbody')!.textContent!
    assert.match(rows, /TEST/); assert.match(rows, /0.12345678/); assert.match(rows, /\+10.00%/)
    assert.match(rows, /\$100/); assert.match(rows, /\$110/)
  } finally { await view.close() }
})
test('all seven tabs show recorded trades, outcomes, model families and selected backtest evidence', async () => {
  const statuses = ['EXECUTED', 'REJECTED', 'HOLD', 'UNRESOLVED']
  const decisions = statuses.map((executionStatus, index) => ({ ...decision, decisionId: `${index + 3}1111111-1111-4111-8111-111111111111`, executionStatus, signal: index === 2 ? 'HOLD' : index === 1 ? 'SELL' : 'BUY' }))
  const view = await mount({ 'performance/history': [], decisions, models: [model, { ...model, modelName: 'random_forest', modelType: 'RandomForest', modelVersion: 'v000004' }, { ...model, modelVersion: 'v000001', status: 'Archived' }], backtests: [backtest, { ...backtest, modelVersion: 'v000002', evaluationSource: 'model_promotion', finalValue: '120000', totalReturn: .20 }], 'rejected-decisions': [rejection] })
  try {
    await view.click('Trades')
    assert.match(view.container.querySelector('tbody')!.textContent!, /0.12345678/)
    assert.match(view.container.querySelector('tbody')!.textContent!, /100.1234/)
    assert.match(view.container.querySelector('tbody')!.textContent!, /—/)
    assert.doesNotMatch(view.container.querySelector('table')!.textContent!, /Entry \/ Exit|P&L \(%\)/)
    await view.click('Decisions')
    for (const label of ['Executed', 'Rejected', 'Hold', 'Unresolved', '72.0%', 'v000002', 'Low confidence']) assert.match(view.container.textContent!, new RegExp(label))
    await view.click('Performance')
    for (const label of ['Average Loss', 'Best Trade', 'Worst Trade', 'Historical coverage']) assert.match(view.container.textContent!, new RegExp(label))
    await view.click('Model')
    for (const label of ['logistic', 'random_forest', 'v000004', 'v000001', 'Archived', '72.0%', '71.0%', '73.0%', '70.0%']) assert.match(view.container.textContent!, new RegExp(label))
    await view.click('Backtests')
    assert.match(view.container.textContent!, /Version unavailable/)
    assert.match(view.container.textContent!, /\+12.00%/)
    const select = view.container.querySelector('select[data-backtests]') as HTMLSelectElement
    assert.ok(select)
    await act(async () => { select.value = '1'; select.dispatchEvent(new window.Event('change', { bubbles: true })) })
    assert.match(view.container.textContent!, /\+20.00%/)
    assert.ok([...view.container.querySelectorAll('button')].filter(button => /Backtest/.test(button.textContent ?? '') && button.textContent?.includes('Run')).every(button => button.disabled))
    assert.ok(view.paths.filter(path => path === 'performance').length === 1)
  } finally { await view.close() }
})
test('null and legacy metrics remain unavailable, genuine zero remains zero, logout clears the page in FR and EN', async () => {
  const view = await mount({ 'performance/history': [], performance: { ...performance, winRate: null, maxDrawdown: null, averageWin: null, averageLoss: null, bestTrade: null, worstTrade: null, tradeStatisticsComplete: false, unvaluedLegacySellCount: 1, totalReturn: 0 } })
  try {
    assert.match(view.container.querySelector('.metric-grid')!.textContent!, /\+0.00%/)
    assert.match(view.container.textContent!, /Historical trade statistics incomplete/)
    await act(async () => i18n.changeLanguage('fr'))
    assert.match(view.container.textContent!, /Statistiques historiques des transactions incomplètes/)
    await act(async () => clearAuthSession())
    assert.doesNotMatch(view.container.textContent!, /108[\s\u202f\u00a0,]*000|TEST|v000002/)
    assert.match(view.container.textContent!, /session.*expiré/i)
  } finally { await view.close() }
})
test('detail controls request immutable trade and decision IDs and show original model versions', async () => {
  const view = await mount({ 'performance/history': [] })
  try {
    await view.click('Trades')
    const detail = view.container.querySelector(`button[data-trade-id="${secondId}"]`) as HTMLButtonElement
    assert.ok(detail)
    await act(async () => detail.click()); await settle()
    assert.ok(view.paths.includes(`trades/${secondId}`))
    assert.match(view.container.querySelector('[data-details]')!.textContent!, /v000002/)
    const decisionButton = view.container.querySelector('[data-details] button[data-decision-id]') as HTMLButtonElement
    assert.ok(decisionButton)
    await act(async () => decisionButton.click()); await settle()
    assert.ok(view.paths.some(path => path.startsWith('decisions/')))
  } finally { await view.close() }
})
test('empty positions remain a table and a rejected detail session clears cached portfolio data', async () => {
  const view = await mount({ 'performance/history': [], positions: [], [`trades/${secondId}`]: new Response('{}', { status: 401 }) })
  try {
    assert.match(view.container.querySelector('.positions-panel table')!.textContent!, /No open positions/)
    await view.click('Trades')
    const button = view.container.querySelector('button[data-trade-id]') as HTMLButtonElement
    await act(async () => button.click()); await settle()
    await view.click('Overview')
    assert.doesNotMatch(view.container.textContent!, /108,000|TEST|v000002/)
    assert.match(view.container.textContent!, /session has expired/)
  } finally { await view.close() }
})
test('a list refresh returning 401 also removes already loaded details', async () => {
  const overrides: Record<string, unknown> = { 'performance/history': [] }
  const view = await mount(overrides)
  try {
    await view.click('Trades')
    await act(async () => (view.container.querySelector('button[data-trade-id]') as HTMLButtonElement).click()); await settle()
    assert.match(view.container.querySelector('[data-details]')!.textContent!, /v000002/)
    overrides.trades = new Response('{}', { status: 401 })
    await view.click('Refresh')
    assert.ok(view.container.querySelector('[data-details]') === null, '401 must remove already loaded detail data')
    assert.doesNotMatch(view.container.textContent!, /TEST|v000002/)
  } finally { await view.close() }
})
test('official rejection codes render in English and French with actual rejection timestamps', async () => {
  const reasons = [
    ['InvalidDecision', 'Invalid decision', 'Décision invalide'], ['InvalidPrice', 'Invalid price', 'Prix invalide'],
    ['HoldSignal', 'Hold signal', 'Signal de conservation'], ['LowConfidence', 'Low confidence', 'Confiance insuffisante'],
    ['CurrencyMismatch', 'Currency mismatch', 'Devise incompatible'], ['MaxPositionsReached', 'Maximum positions reached', 'Nombre maximal de positions atteint'],
    ['MaxSymbolExposureReached', 'Maximum symbol exposure reached', 'Exposition maximale au symbole atteinte'], ['InsufficientCash', 'Insufficient cash', 'Liquidités insuffisantes'],
    ['NoPositionToSell', 'No position to sell', 'Aucune position à vendre'], ['TradeTooSmall', 'Trade too small', 'Transaction trop petite'],
    ['FutureCode', 'Unknown rejection reason', 'Motif de rejet inconnu'],
  ]
  const view = await mount({ 'performance/history': [], 'rejected-decisions': reasons.map(([rejectionReason], index) => ({ ...rejection, decisionId: `${String(index + 1).padStart(8, '0')}-1111-4111-8111-111111111111`, rejectionReason })) })
  try {
    await view.click('Decisions')
    for (const [, english] of reasons) assert.ok(view.container.textContent!.includes(english))
    assert.match(view.container.textContent!, /Oct 20, 2026/)
    await act(async () => i18n.changeLanguage('fr'))
    for (const [, , french] of reasons) assert.ok(view.container.textContent!.includes(french))
    assert.match(view.container.textContent!, /20 oct. 2026/)
  } finally { await view.close() }
})
