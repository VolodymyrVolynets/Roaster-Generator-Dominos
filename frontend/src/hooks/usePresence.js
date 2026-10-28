import { useEffect, useState } from 'react'

/**
 * Keeps an element mounted for its exit animation.
 * Returns `mounted` (render it) and `closing` (apply the exit class).
 * `exitMs` must match the CSS exit animation duration.
 */
export function usePresence(isOpen, exitMs = 180) {
  const [mounted, setMounted] = useState(isOpen)
  const [closing, setClosing] = useState(false)

  useEffect(() => {
    if (isOpen) {
      setMounted(true)
      setClosing(false)
      return undefined
    }
    if (!mounted) return undefined
    setClosing(true)
    const timer = window.setTimeout(() => {
      setMounted(false)
      setClosing(false)
    }, exitMs)
    return () => window.clearTimeout(timer)
    // `mounted` is intentionally read only when `isOpen` changes.
  }, [isOpen, exitMs])

  return { mounted, closing }
}
