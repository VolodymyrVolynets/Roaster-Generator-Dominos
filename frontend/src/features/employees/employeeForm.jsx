export const emptyEmployeeForm = {
  employeeNumber: '',
  firstName: '',
  lastName: '',
  phoneNumber: '',
  payrollNumber: '',
  hourlyRate: 14.5,
  maximumWeeklyHours: 45,
  roles: ['Driver'],
  driverType: 'Car',
  isOwn: true,
}

export const employeeFieldLabels = {
  employeeNumber: 'Employee number',
  firstName: 'First name',
  lastName: 'Last name',
  phoneNumber: 'Phone number',
  payrollNumber: 'Payroll number',
  hourlyRate: 'Hourly pay rate',
  maximumWeeklyHours: 'Maximum weekly hours',
  roles: 'Roles',
  driverType: 'Driver type',
  identity: 'Employee login',
}

export function getEmployeeFieldErrors(saveState, field) {
  return saveState?.status === 'error' ? saveState.fieldErrors?.[field] || [] : []
}

export function EmployeeFieldError({ field, messages }) {
  if (!messages?.length) {
    return null
  }

  return (
    <p id={`admin-employee-${field}-error`} className="employee-field-error">
      {messages.join(' ')}
    </p>
  )
}

export function getEmployeeErrorSummary(error) {
  const fieldMessages = new Set(Object.values(error?.fieldErrors || {}).flat())
  const summary = String(error?.message || '')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line && !fieldMessages.has(line))
  return summary.join('\n') || 'The employee could not be saved.'
}
