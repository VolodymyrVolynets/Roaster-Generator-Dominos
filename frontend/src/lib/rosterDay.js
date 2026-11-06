/**
 * Who works on one roster day, sorted by start (shifts starting after midnight last),
 * then start time, then name; plus how many employees are off.
 */
export function getRosterDay(employees, date) {
  const working = employees
    .flatMap((employee) => employee.shifts
      .filter((shift) => shift.date === date)
      .map((shift) => ({ employee, shift })))
    .sort((first, second) =>
      ((first.shift.startDayOffset || 0) - (second.shift.startDayOffset || 0)) ||
      first.shift.startTime.localeCompare(second.shift.startTime) ||
      first.employee.employeeName.localeCompare(second.employee.employeeName))
  const offCount = employees.filter((employee) => !employee.shifts.some((shift) => shift.date === date)).length
  return { working, offCount }
}
