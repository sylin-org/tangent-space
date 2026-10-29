// Appearance: System, Light (Paper) or Dark (Ink), chosen per browser, and the Tangent Space accent.
// The tokens answer data-theme and data-accent on <html>; System leaves data-theme off so the device decides.

export type Theme = 'system' | 'light' | 'dark'
export type Accent = 'gold' | 'jade' | 'iris' | 'copper'

export const themes: readonly Theme[] = ['system', 'light', 'dark']
export const accents: readonly Accent[] = ['gold', 'jade', 'iris', 'copper']

const storageKey = 'tangent:appearance'

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

// The server renders the Tangent Space's accent; this applies another one live, as a settings preview does.
export function applyAccent(accent: Accent): void {
  if (accents.includes(accent)) document.documentElement.dataset.accent = accent
}
