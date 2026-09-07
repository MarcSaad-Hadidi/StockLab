import assert from 'node:assert/strict'
import test from 'node:test'

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
