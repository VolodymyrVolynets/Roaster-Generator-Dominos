import { useEffect, useState } from 'react'

const storageKey = 'roaster-generator-theme'
const themes = ['system', 'light', 'dark']

function readStoredTheme() {
  try {
    const value = window.localStorage.getItem(storageKey)
    return themes.includes(value) ? value : 'system'
  } catch {
    return 'system'
  }
}

export function applyTheme(theme) {
  const root = document.documentElement
  if (theme === 'system') root.removeAttribute('data-theme')
  else root.setAttribute('data-theme', theme)
}

export function useTheme() {
  const [theme, setTheme] = useState(readStoredTheme)

  useEffect(() => {
    applyTheme(theme)
    try {
      window.localStorage.setItem(storageKey, theme)
    } catch {
      // Storage can be unavailable in private windows; the theme still applies for this visit.
    }
  }, [theme])

  const cycleTheme = () => setTheme((current) => themes[(themes.indexOf(current) + 1) % themes.length])
  return { theme, cycleTheme }
}
