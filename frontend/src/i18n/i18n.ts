import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './locales/en.json' with { type: 'json' }
import fr from './locales/fr.json' with { type: 'json' }

export const supportedLanguages = ['en', 'fr'] as const
export type Language = (typeof supportedLanguages)[number]
export const defaultLanguage: Language = 'en'
export const fallbackLanguage: Language = 'en'
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

export function isSupportedLanguage(value: string | null | undefined): value is Language {
  return value === 'en' || value === 'fr'
}

export function getStoredLanguage(storage: LanguageStorage | undefined = browserStorage()): Language {
  const stored = storage?.getItem(languageStorageKey)
  return isSupportedLanguage(stored) ? stored : defaultLanguage
}

export function persistLanguage(language: Language, storage: LanguageStorage | undefined = browserStorage()) {
  storage?.setItem(languageStorageKey, language)
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

export { i18n }
