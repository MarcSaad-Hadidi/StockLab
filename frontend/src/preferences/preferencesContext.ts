import { createContext, useContext } from 'react'
import type { PreferenceKey, UserPreferences } from '../api/preferencesApi'

export const preferencesChangedKey = 'stocklab-preferences-changed'

export type PreferencesState = {
  values: UserPreferences | null
  status: 'loading' | 'idle' | 'saving' | 'saved' | 'loadError' | 'saveError' | 'conflict'
  save: (key: PreferenceKey, enabled: boolean) => void
  retry: () => void
}

export const PreferencesContext = createContext<PreferencesState | null>(null)
export const usePreferences = () => useContext(PreferencesContext)
