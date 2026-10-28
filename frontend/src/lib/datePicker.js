import { formatDateInput, parseDateInput } from './weekSelection.js'

// Date helpers for the calendar picker. Values are YYYY-MM-DD strings, like <input type="date">.

export function todayValue(now = new Date()) {
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

export function addDays(value, days) {
  const date = parseDateInput(value)
  date.setUTCDate(date.getUTCDate() + days)
  return formatDateInput(date)
}

export function monthStartOf(value) {
  const date = parseDateInput(value)
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1))
}

export function isDateAllowed(value, min, max) {
  return (!min || value >= min) && (!max || value <= max)
}

/** The date the calendar should open on: the value, else today when allowed, else the nearest limit. */
export function initialPickerDate(value, min, max, today = todayValue()) {
  if (value) return value
  if (isDateAllowed(today, min, max)) return today
  return min || max || today
}
