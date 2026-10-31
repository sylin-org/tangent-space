// A reader's theme: System, Light (Paper) or Dark (Ink), chosen per browser, for a visitor and a participant alike.
// The tokens answer data-theme on <html>; System leaves it off so the device decides.

export type Theme = 'system' | 'light' | 'dark'

export const themes: readonly Theme[] = ['system', 'light', 'dark']

export const storageKey = 'tangent:appearance'

function applyTheme(theme: Theme): void {
  const root = document.documentElement
  if (theme === 'system') delete root.dataset.theme
  else root.dataset.theme = theme
}

// Without storage the choice still applies to the page; it is simply not remembered.
export function savedTheme(): Theme {
  let saved: string | null = null
  try {
    saved = localStorage.getItem(storageKey)
  } catch {}
  return themes.includes(saved as Theme) ? (saved as Theme) : 'system'
}

export function restoreTheme(): Theme {
  const theme = savedTheme()
  applyTheme(theme)
  return theme
}

export function selectTheme(theme: Theme): void {
  applyTheme(theme)
  try {
    localStorage.setItem(storageKey, theme)
  } catch {}
}
