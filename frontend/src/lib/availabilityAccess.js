export function getAvailabilityEmployeeId(user, selectedEmployeeId) {
  if (!user) return ''
  return String((user.isAdmin ? selectedEmployeeId : user.employeeId) || '')
}
