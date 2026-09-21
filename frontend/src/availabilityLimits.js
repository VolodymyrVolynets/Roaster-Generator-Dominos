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

export function isAvailabilityTimeAllowed(day, field, value, maximumHours) {
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
