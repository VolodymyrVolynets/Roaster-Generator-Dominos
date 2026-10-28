export function countAvailabilityDays(days = []) {
  return days.filter((day) => day.startTime || day.finishTime).length
}

function durationHours(startTime, finishTime) {
  const minutes = (time) => {
    const [hour, minute] = time.split(':').map(Number)
    return hour * 60 + minute
  }
  return ((minutes(finishTime) - minutes(startTime) + 1440) % 1440) / 60
}

function toMinutes(time) {
  const [hour, minute] = time.split(':').map(Number)
  return hour * 60 + minute
}

// Mirrors the server rule: start no earlier than opening, finish no later than closing (closing may be after midnight).
function isWithinShopHours(day, field, value) {
  if (!day.openingTime || !day.closingTime || !value) return true
  const opening = toMinutes(day.openingTime)
  const closing = toMinutes(day.closingTime)
  const closingOnTimeline = closing <= opening ? closing + 1440 : closing
  const startTime = field === 'startTime' ? value : day.startTime
  const finishTime = field === 'finishTime' ? value : day.finishTime

  if (startTime && toMinutes(startTime) < opening) return field !== 'startTime'
  if (!finishTime) return true

  const anchor = startTime ? toMinutes(startTime) : opening
  let finish = toMinutes(finishTime)
  if (finish <= anchor) finish += 1440
  return finish <= closingOnTimeline
}

export function isAvailabilityTimeAllowed(day, field, value, maximumHours) {
  if (!isWithinShopHours(day, field, value)) return false
  if (maximumHours == null) return true
  const updated = { ...day, [field]: value }
  if (!updated.startTime || !updated.finishTime) return true
  const duration = durationHours(updated.startTime, updated.finishTime)
  return duration > 0 && duration <= maximumHours
}

export function getAvailabilityLimitErrors(schedule) {
  if (!schedule) return []
  const errors = []
  const { days = [], maximumAvailabilityDaysPerWeek: maximumDays, maximumAvailabilityHoursPerDay: maximumHours } = schedule
  if (maximumDays != null && countAvailabilityDays(days) > maximumDays) {
    errors.push(`Choose at most ${maximumDays} days for this week. Reset another day to add a different one.`)
  }
  if (maximumHours != null) {
    for (const day of days) {
      if (day.startTime && day.finishTime && durationHours(day.startTime, day.finishTime) > maximumHours) {
        errors.push(`${day.dayOfWeek || day.date}: availability cannot exceed ${maximumHours} hours, including overnight hours.`)
      }
    }
  }
  return errors
}

export function getAvailabilityDemandError(schedule) {
  if (schedule?.availabilitySaveBlockedReason) return schedule.availabilitySaveBlockedReason
  const { requiredBusyDates = [], days = [] } = schedule || {}
  if (requiredBusyDates.length === 0 || days.some((day) =>
    requiredBusyDates.includes(day.date) && day.startTime && day.finishTime &&
    durationHours(day.startTime, day.finishTime) > 0)) return ''

  const names = requiredBusyDates.map((date) => {
    const day = days.find((item) => item.date === date)
    return day?.dayOfWeek ? `${day.dayOfWeek} (${date})` : date
  }).join(' or ')
  return `Include availability on at least one of this week's busiest days: ${names}. An administrator can make exceptions.`
}
