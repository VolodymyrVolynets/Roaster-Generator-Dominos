export const dateFormatter = new Intl.DateTimeFormat(undefined, {
  day: 'numeric',
  month: 'short',
})

export const hours = Array.from({ length: 24 }, (_, index) => String(index).padStart(2, '0'))

export const minWeekOffset = 1

export const maxWeekOffset = 3

export function parseDate(dateValue) {
  const [year, month, day] = dateValue.split('-').map(Number)
  return new Date(year, month - 1, day)
}

export function getShiftDuration(startTime, finishTime) {
  if (!startTime || !finishTime) {
    return null
  }

  const startHour = Number(startTime.split(':')[0])
  const finishHour = Number(finishTime.split(':')[0])
  const duration = finishHour > startHour
    ? finishHour - startHour
    : 24 - startHour + finishHour

  if (duration === 24) {
    return null
  }

  return `${duration} ${duration === 1 ? 'hour' : 'hours'}`
}

const holidayDateFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'short',
})

export function formatHolidayDate(value) {
  return value ? holidayDateFormatter.format(new Date(value)) : '—'
}

export function formatDateOnly(value) {
  return value ? dateFormatter.format(parseDate(value)) : '—'
}

export const personalRosterDateFormatter = new Intl.DateTimeFormat(undefined, {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
})
