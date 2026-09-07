import { useTranslation } from 'react-i18next'
import { isSupportedLanguage, setLanguage, supportedLanguages, type Language } from '../i18n/i18n'
import './LanguageSelector.css'

type LanguageSelectorProps = {
  className?: string
}

const languageLabels: Record<Language, string> = {
  en: 'English',
  fr: 'Français',
}

export function LanguageSelector({ className = '' }: LanguageSelectorProps) {
  const { i18n, t } = useTranslation()
  const currentLanguage = isSupportedLanguage(i18n.resolvedLanguage) ? i18n.resolvedLanguage : 'en'

  return (
    <div aria-label={t('common.languageLabel')} className={`language-switch ${className}`.trim()} role="group">
      {supportedLanguages.map((language) => (
        <button
          aria-pressed={currentLanguage === language}
          key={language}
          onClick={() => { void setLanguage(language) }}
          title={languageLabels[language]}
          type="button"
        >
          {language.toUpperCase()}
        </button>
      ))}
    </div>
  )
}
