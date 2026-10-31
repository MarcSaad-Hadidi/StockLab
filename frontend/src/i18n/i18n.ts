import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './locales/en.json' with { type: 'json' }
import fr from './locales/fr.json' with { type: 'json' }
import { defaultLanguage, fallbackLanguage, isSupportedLanguage, supportedLanguages, type Language } from './languages'
export { defaultLanguage, fallbackLanguage, isSupportedLanguage, supportedLanguages, type Language } from './languages'

export const languageStorageKey = 'stocklab-language'

export const translationResources = {
  en: { translation: en },
  fr: { translation: fr },
} as const

type LanguageStorage = Pick<Storage, 'getItem' | 'setItem'>

function browserStorage(): LanguageStorage | undefined {
  if (typeof window === 'undefined') return undefined
  try {
    return window.localStorage
  } catch {
    return undefined
  }
}

function readStoredLanguage(storage: LanguageStorage | undefined): string | null {
  try {
    return storage?.getItem(languageStorageKey) ?? null
  } catch {
    return null
  }
}

export function getStoredLanguage(storage: LanguageStorage | undefined = browserStorage()): Language {
  const stored = readStoredLanguage(storage)
  return isSupportedLanguage(stored) ? stored : defaultLanguage
}

export function persistLanguage(language: Language, storage: LanguageStorage | undefined = browserStorage()) {
  try {
    storage?.setItem(languageStorageKey, language)
  } catch {
    // A blocked or full storage must not prevent the UI from changing language.
  }
}

export async function setLanguage(language: Language, storage: LanguageStorage | undefined = browserStorage()) {
  persistLanguage(language, storage)
  await i18n.changeLanguage(language)
}

i18n.use(initReactI18next).init({
  resources: translationResources,
  lng: getStoredLanguage(),
  fallbackLng: fallbackLanguage,
  supportedLngs: supportedLanguages,
  interpolation: { escapeValue: false },
  initAsync: false,
})

function syncDocumentLanguage(language: string) {
  if (typeof document !== 'undefined') document.documentElement.lang = language
}

syncDocumentLanguage(i18n.language)
i18n.on('languageChanged', syncDocumentLanguage)

// A saved preference also updates pages that are already open in another tab.
if (typeof window !== 'undefined') window.addEventListener('storage', event => {
  if (event.key === languageStorageKey && isSupportedLanguage(event.newValue))
    void i18n.changeLanguage(event.newValue)
})

export { i18n }
