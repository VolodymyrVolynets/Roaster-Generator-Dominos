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

export const driverRosterRoleGroups = [
  {
    id: 'drivers',
    label: 'Drivers',
    roles: ['Driver'],
    description: 'Delivery drivers and their shifts.',
  },
]

export const insideRosterRoleGroups = [
  {
    id: 'managers',
    label: 'Managers',
    roles: ['Manager'],
    description: 'Managers who can supervise inside employees.',
  },
  {
    id: 'instore',
    label: 'In-store',
    roles: ['InStore'],
    description: 'Employees assigned to inside shop work.',
  },
]

export const rosterRoleGroups = [...insideRosterRoleGroups, ...driverRosterRoleGroups]

export function employeeHasRole(employee, role) {
  return (employee?.roles || []).some((item) => String(item).toLowerCase() === role.toLowerCase())
}

export function getEmployeeRoleGroup(employee) {
  return employeeRoleGroups.find((group) =>
    group.roles.some((role) => employeeHasRole(employee, role)),
  ) || employeeRoleGroups[employeeRoleGroups.length - 1]
}

export function getRosterRoleGroup(employee) {
  if (employeeHasRole(employee, 'Manager')) return insideRosterRoleGroups[0]
  if (employeeHasRole(employee, 'InStore')) return insideRosterRoleGroups[1]
  // Older driver rosters have no role snapshot.
  return !employee?.roles?.length || employeeHasRole(employee, 'Driver') ? driverRosterRoleGroups[0] : null
}

export function employeeMatchesRosterKind(employee, rosterKind) {
  const group = getRosterRoleGroup(employee)
  return rosterKind === 'inside' ? group?.id === 'managers' || group?.id === 'instore' : group?.id === 'drivers'
}

export function rosterForKind(roster, rosterKind) {
  if (!roster || (roster.rosterKind && roster.rosterKind !== rosterKind)) return null
  return { ...roster, employees: (roster.employees || []).filter((employee) => employeeMatchesRosterKind(employee, rosterKind)) }
}

export const driverRosterOnly = (roster) => rosterForKind(roster, 'drivers')

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
