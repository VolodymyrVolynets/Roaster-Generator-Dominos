export const employeeRoleGroups = [
  {
    id: 'drivers',
    label: 'Drivers',
    roles: ['Driver'],
    description: 'Delivery staff included in roster generation.',
  },
  {
    id: 'instore',
    label: 'In-store',
    roles: ['InStore'],
    description: 'Employees assigned to in-store work.',
  },
  {
    id: 'managers',
    label: 'Managers & admins',
    roles: ['Manager', 'Admin'],
    description: 'Employees with management or administration access.',
  },
  {
    id: 'other',
    label: 'Other employees',
    roles: [],
    description: 'Employees without one of the groups above.',
  },
]

export const rosterRoleGroups = [
  {
    id: 'drivers',
    label: 'Drivers',
    roles: ['Driver'],
    description: 'Delivery drivers and their shifts.',
  },
]

export function employeeHasRole(employee, role) {
  return (employee?.roles || []).some((item) => String(item).toLowerCase() === role.toLowerCase())
}

export function getEmployeeRoleGroup(employee) {
  return employeeRoleGroups.find((group) =>
    group.roles.some((role) => employeeHasRole(employee, role)),
  ) || employeeRoleGroups[employeeRoleGroups.length - 1]
}

export function getRosterRoleGroup(employee) {
  if (employeeHasRole(employee, 'InStore') || employeeHasRole(employee, 'Manager')) return null
  // Older driver rosters have no role snapshot.
  return !employee?.roles?.length || employeeHasRole(employee, 'Driver') ? rosterRoleGroups[0] : null
}

export function driverRosterOnly(roster) {
  if (!roster || (roster.rosterKind && roster.rosterKind !== 'drivers')) return null
  return { ...roster, employees: (roster.employees || []).filter((employee) => getRosterRoleGroup(employee)?.id === 'drivers') }
}

export function compareEmployees(first, second) {
  if (first.isActive !== second.isActive) {
    return first.isActive ? -1 : 1
  }

  return `${first.lastName || ''} ${first.firstName || first.employeeName || ''}`.localeCompare(
    `${second.lastName || ''} ${second.firstName || second.employeeName || ''}`,
    undefined,
    { sensitivity: 'base' },
  )
}

export function compareRosterEmployees(first, second) {
  return String(first.employeeName || '').localeCompare(
    String(second.employeeName || ''),
    undefined,
    { sensitivity: 'base' },
  )
}
