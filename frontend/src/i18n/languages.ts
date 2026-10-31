export const supportedLanguages = ['en', 'fr'] as const
export type Language = typeof supportedLanguages[number]
export const defaultLanguage: Language = 'en'
export const fallbackLanguage: Language = 'en'
export function isSupportedLanguage(value: unknown): value is Language {
  return value === 'en' || value === 'fr'
}
