import assert from 'node:assert/strict'
import test from 'node:test'
import { readFileSync, readdirSync } from 'node:fs'
import ts from 'typescript'
import { formatCurrency, formatDate, formatNumber, formatPercent, formatTime } from '../src/i18n/formatters.ts'
import { performanceSeries } from '../src/dashboard/dashboardData.ts'
import { chartRanges } from '../src/market/stockDetailsData.ts'

import {
  defaultLanguage,
  fallbackLanguage,
  getStoredLanguage,
  i18n,
  isSupportedLanguage,
  languageStorageKey,
  persistLanguage,
  setLanguage,
  supportedLanguages,
  translationResources,
} from '../src/i18n/i18n.ts'

type MemoryStorage = {
  value: string | null
  getItem: (key: string) => string | null
  setItem: (key: string, value: string) => void
}

function createStorage(value: string | null = null): MemoryStorage {
  const storage: MemoryStorage = {
    value,
    getItem: () => storage.value,
    setItem: (_key, nextValue) => { storage.value = nextValue },
  }
  return storage
}

test('uses English as the default and fallback language', () => {
  assert.equal(defaultLanguage, 'en')
  assert.equal(fallbackLanguage, 'en')
  assert.deepEqual(supportedLanguages, ['en', 'fr'])
  assert.equal(getStoredLanguage(createStorage()), 'en')
  assert.equal(i18n.language, 'en')
})

test('accepts only supported languages and falls back from invalid storage values', () => {
  assert.equal(isSupportedLanguage('en'), true)
  assert.equal(isSupportedLanguage('fr'), true)
  assert.equal(isSupportedLanguage('de'), false)
  assert.equal(getStoredLanguage(createStorage('de')), 'en')
})

test('persists and applies an English/French language change', async () => {
  const storage = createStorage()

  await setLanguage('fr', storage)
  assert.equal(storage.value, 'fr')
  assert.equal(i18n.language, 'fr')
  assert.equal(i18n.t('dashboard.greeting.afternoon', { name: 'Ghaith' }), 'Bon après-midi, Ghaith')

  await setLanguage('en', storage)
  assert.equal(storage.value, 'en')
  assert.equal(i18n.language, 'en')
  assert.equal(i18n.t('dashboard.greeting.afternoon', { name: 'Ghaith' }), 'Good afternoon, Ghaith')
})

test('stores the preference under the shared localStorage key', () => {
  const storage = createStorage()
  persistLanguage('fr', storage)
  assert.equal(storage.value, 'fr')
  assert.equal(languageStorageKey, 'stocklab-language')
})

test('keeps the same translation keys in English and French', () => {
  const flatten = (value: unknown, prefix = ''): string[] => {
    if (!value || typeof value !== 'object') return prefix ? [prefix] : []
    return Object.entries(value).flatMap(([key, nested]) => flatten(nested, prefix ? `${prefix}.${key}` : key))
  }

  assert.deepEqual(flatten(translationResources.en.translation).sort(), flatten(translationResources.fr.translation).sort())
})

test('every literal UI and data translation key resolves in both languages without fallback', () => {
  const namespaces = Object.keys(translationResources.en.translation)
  const visitDirectory = (directory: URL) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const file = new URL(entry.name + (entry.isDirectory() ? '/' : ''), directory)
      if (entry.isDirectory()) { visitDirectory(file); continue }
      if (!entry.name.endsWith('.tsx') && !entry.name.endsWith('Data.ts')) continue
      const source = ts.createSourceFile(file.pathname, readFileSync(file, 'utf8'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX)
      const checkKey = (key: string) => {
        for (const lng of supportedLanguages) {
          assert.equal(i18n.exists(key, { lng, fallbackLng: false }), true, `${file.pathname}: missing ${lng} translation for ${key}`)
          assert.notEqual(i18n.t(key, { lng, fallbackLng: false }), key)
        }
      }
      const visit = (node: ts.Node) => {
        if (ts.isCallExpression(node) && node.expression.getText(source) === 't' && node.arguments[0] && ts.isStringLiteral(node.arguments[0])) checkKey(node.arguments[0].text)
        if (ts.isJsxAttribute(node) && node.name.getText(source) === 'i18nKey' && node.initializer && ts.isStringLiteral(node.initializer)) checkKey(node.initializer.text)
        if (ts.isStringLiteral(node) && namespaces.some(namespace => node.text.startsWith(`${namespace}.`))) checkKey(node.text)
        ts.forEachChild(node, visit)
      }
      visit(source)
    }
  }
  visitDirectory(new URL('../src/', import.meta.url))
})

test('translations preserve interpolation variables and resolve dynamic messages', () => {
  const variables = (text: string) => [...text.matchAll(/{{\s*([^},]+)(?:,[^}]*)?\s*}}/g)].map(match => match[1].trim()).sort()
  const visit = (value: unknown, prefix = '') => {
    if (typeof value !== 'string') {
      for (const [key, child] of Object.entries(value as Record<string, unknown>)) visit(child, prefix ? `${prefix}.${key}` : key)
      return
    }
    const french = i18n.getResource('fr', 'translation', prefix)
    assert.equal(typeof french, 'string', prefix)
    assert.deepEqual(variables(french), variables(value), prefix)
  }
  visit(translationResources.en.translation)
  for (const lng of supportedLanguages) {
    const results = i18n.t('market.showingResults', { lng, start: 1, end: 12, count: 42 })
    assert.match(results, /1.*12.*42/)
    assert.doesNotMatch(results, /{{|market\.showingResults/)
    assert.match(i18n.t('dashboard.shares', { lng, count: 3 }), /3/)
    assert.match(i18n.t('dashboard.greeting.morning', { lng, name: 'Sam' }), /Sam/)
  }
})

test('dynamic chart ranges and data labels have translations in both languages', () => {
  for (const lng of supportedLanguages) {
    for (const range of new Set([...Object.keys(performanceSeries), ...chartRanges])) {
      assert.equal(i18n.exists(`common.timeRanges.${range}`, { lng, fallbackLng: false }), true, `${lng}: ${range}`)
    }
    for (const series of Object.values(performanceSeries)) {
      for (const label of series.labels.filter(label => /[A-Za-z]/.test(label))) {
        assert.equal(i18n.exists(`dashboard.chartLabels.${label}`, { lng, fallbackLng: false }), true, `${lng}: ${label}`)
      }
    }
  }
})

test('formats the same financial values and dates for each locale', () => {
  assert.equal(formatNumber(1234.5, 'en'), '1,234.50')
  assert.equal(formatNumber(1234.5, 'fr'), '1\u202f234,50')
  assert.equal(formatCurrency(1234.5, 'en'), '$1,234.50')
  assert.match(formatCurrency(1234.5, 'fr'), /1\u202f234,50/)
  assert.equal(formatPercent(12.5, 'en', 1), '12.5%')
  assert.equal(formatPercent(12.5, 'fr', 1), '12,5\u00a0%')
  assert.equal(formatDate('2026-06-01', 'en'), 'Jun 1, 2026')
  assert.equal(formatDate('2026-06-01', 'fr'), '1 juin 2026')
  assert.equal(formatTime('15:18', 'en'), '3:18 PM')
  assert.equal(formatTime('15:18', 'fr'), '15:18')
})
