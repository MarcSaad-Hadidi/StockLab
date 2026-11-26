import assert from 'node:assert/strict'
import { test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import type { StockQuote } from '../src/api/marketDataApi.ts'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
Object.assign(globalThis, { React })
const { i18n } = await import('../src/i18n/i18n.ts')
const { StockDataPanel } = await import('../src/market/StockDataPanel.tsx')
const quote: StockQuote = { symbol: 'AAPL', name: 'Apple Inc.', exchange: 'NASDAQ', price: 140.25, priceDecimal: '140.25',
  change: -3, changePercent: -2.5, currency: 'USD', volume: 1234, asOfUtc: '2026-11-18T15:30:00Z',
  receivedAtUtc: '2026-11-18T17:00:00Z', open: 140, high: 141, low: 139, previousClose: 143.25,
  averageVolume: null, isMarketOpen: false, fiftyTwoWeek: null }

for (const language of ['en', 'fr']) {
  for (const source of ['Mock', 'TwelveData', undefined] as const) {
    test(`Key Metrics retains the actual ${source ?? 'unknown'} quote source and dates in ${language}`, async () => {
      await i18n.changeLanguage(language)
      const html = renderToStaticMarkup(React.createElement(StockDataPanel, { tab: 'keyMetrics', quote: { ...quote, source } }))
      const page = new JSDOM(html).window.document
      const provenance = page.querySelector('[data-market-provenance]')
      assert.ok(provenance, 'quote provenance must be explicit beside the displayed metrics')
      const text = provenance.textContent!
      assert.ok(text.includes(source === 'Mock' ? i18n.t('marketProvenance.mock')
        : source === 'TwelveData' ? 'Twelve Data' : i18n.t('marketProvenance.unknownSource')))
      if (source !== 'TwelveData') assert.doesNotMatch(page.body.textContent!, /Twelve Data/)
      assert.deepEqual([...provenance.querySelectorAll('time')].map(time => time.dateTime),
        [quote.asOfUtc, quote.receivedAtUtc])
      assert.match(page.body.textContent!, language === 'en' ? /140\.00/ : /140,00/)
      assert.match(page.body.textContent!, /1[,.\s\u202f]?234/)
    })
  }
  test(`a missing quote has unavailable provenance instead of a fictitious Twelve Data attribution in ${language}`, async () => {
    await i18n.changeLanguage(language)
    const page = new JSDOM(renderToStaticMarkup(React.createElement(StockDataPanel, { tab: 'keyMetrics' }))).window.document
    assert.doesNotMatch(page.body.textContent!, /Twelve Data/)
    assert.ok(page.body.textContent!.includes(i18n.t('marketProvenance.unknownSource')))
    assert.ok(page.body.textContent!.includes(i18n.t('marketProvenance.unknownObservation')))
    assert.equal(page.querySelector('time'), null)
  })
  test(`company ratios retain their separate Alpha Vantage source only after loading in ${language}`, async () => {
    await i18n.changeLanguage(language)
    const fundamentals = { symbol: 'AAPL', marketCap: 123456, peRatio: 12, epsTtm: null, dividendYield: null,
      beta: null, analystTargetPrice: null, analystRatings: null }
    const page = new JSDOM(renderToStaticMarkup(React.createElement(StockDataPanel,
      { tab: 'keyMetrics', quote: { ...quote, source: 'Mock' }, fundamentals }))).window.document
    const sources = [...page.querySelectorAll('[data-market-provenance]')]
    assert.equal(sources.length, 2)
    assert.ok(sources[0].textContent!.includes(i18n.t('marketProvenance.mock')))
    assert.match(sources[1].textContent!, /Alpha Vantage/)
    assert.doesNotMatch(sources[0].textContent!, /Alpha Vantage/)
    assert.doesNotMatch(page.body.textContent!, /Twelve Data/)
  })
}
