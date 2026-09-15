export const initialApplicationEventVersions = Object.freeze({
  connection: 0,
  auth: 0,
  employees: 0,
  availability: 0,
  demand: 0,
  demandDrivers: 0,
  demandInside: 0,
  roster: 0,
  rosterDrivers: 0,
  rosterInside: 0,
  sickLeave: 0,
  holiday: 0,
  settings: 0,
})

function increment(current, ...keys) {
  const next = { ...current }
  for (const key of keys) next[key] = (next[key] || 0) + 1
  return next
}

export function applyApplicationChangedEvent(current, event, employeeId) {
  switch (event?.type) {
    case 'availabilityChanged':
      return increment(current, 'availability')
    case 'demandChanged':
      return increment(current, 'demand', event.rosterKind === 'inside' ? 'demandInside' : 'demandDrivers')
    case 'rosterChanged':
      return increment(current, 'roster', event.rosterKind === 'inside' ? 'rosterInside' : 'rosterDrivers')
    case 'sickLeaveChanged':
      return increment(current, 'sickLeave', ...(event.rosterKind ? ['availability'] : []))
    case 'holidayChanged':
      return increment(current, 'holiday')
    case 'employeeChanged':
      return increment(current, 'employees', 'availability',
        ...(employeeId && String(event.entityId) === String(employeeId) ? ['auth'] : []))
    case 'rosterSettingsChanged':
      return increment(current, 'settings')
    default:
      return current
  }
}

export function reconnectApplicationEvents(current) {
  return Object.fromEntries(Object.keys(initialApplicationEventVersions)
    .map((key) => [key, (current[key] || 0) + 1]))
}
