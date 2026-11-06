const weekMilliseconds = 7 * 24 * 60 * 60 * 1000

export function getMonday(date) {
  const monday = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()))
  const daysSinceMonday = (monday.getUTCDay() + 6) % 7
  monday.setUTCDate(monday.getUTCDate() - daysSinceMonday)
  return monday
}

export function formatDateInput(date) {
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}-${String(date.getUTCDate()).padStart(2, '0')}`
}

export function parseDateInput(value) {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(Date.UTC(year, month - 1, day))
}

export function getWeekStartValue(weekOffset, today = new Date()) {
  const monday = getMonday(today)
  monday.setUTCDate(monday.getUTCDate() + Number(weekOffset) * 7)
  return formatDateInput(monday)
}

export function getWeekOffsetFromDate(value, today = new Date()) {
  const selectedMonday = getMonday(parseDateInput(value))
  const currentMonday = getMonday(today)
  return Math.round((selectedMonday.getTime() - currentMonday.getTime()) / weekMilliseconds)
}

export function getCalendarMonthStart(weekOffset) {
  const selectedWeek = parseDateInput(getWeekStartValue(weekOffset))
  return new Date(Date.UTC(selectedWeek.getUTCFullYear(), selectedWeek.getUTCMonth(), 1))
}

export function getCalendarDays(monthStart) {
  const firstDay = new Date(Date.UTC(monthStart.getUTCFullYear(), monthStart.getUTCMonth(), 1))
  const gridStart = getMonday(firstDay)

  return Array.from({ length: 42 }, (_, index) => {
    const day = new Date(gridStart)
    day.setUTCDate(day.getUTCDate() + index)
    return day
  })
}

export function getRelativeWeekLabel(weekOffset) {
  const offset = Number(weekOffset)
  if (offset === 0) return 'Current week'
  if (offset === 1) return 'Next week'
  if (offset === 2) return 'Week after next'
  if (offset === 3) return 'Three weeks ahead'
  if (offset < 0) return `${Math.abs(offset)} ${Math.abs(offset) === 1 ? 'week' : 'weeks'} ago`
  return `${offset} weeks ahead`
}
