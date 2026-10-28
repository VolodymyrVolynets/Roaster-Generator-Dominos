import { useCallback, useEffect, useRef, useState } from 'react'
import { WeekSelector } from '../../components/SelectionControls'
import { AppShell } from '../../components/AppShell'
import { usePresence } from '../../hooks/usePresence'
import { compareEmployees, driverRosterRoleGroups, employeeHasRole, employeeRoleGroups, getEmployeeRoleGroup, getRosterRoleGroup } from '../../lib/employeeGroups'
import { getWeekOffsetFromDate } from '../../lib/weekSelection'
import { AbsentPanel } from '../absence/AbsentPanel'
import { AvailabilityHeatmap } from '../availability/DriverAvailabilityHeatmap'
import { RosterGenerationPanel, SavedRosterPanel } from '../roster/RosterAdmin'
import { StoreSettingsPanel } from '../settings/StoreSettingsPanel'
import { fetchJson } from '../../api/client'
import { ErrorPopup } from '../../components/ErrorPopup'
import { ApproximateHoursPreview, ScheduleEditor } from '../availability/ScheduleEditor'
import { DemandManager } from '../demand/DemandManager'
import { EmployeeFieldError, employeeFieldLabels, getEmployeeFieldErrors } from '../employees/employeeForm'
import { AdminHolidayPanel, HolidayPanel } from '../leave/HolidayPanels'
import { AdminSickLeavePanel } from '../leave/SickLeavePanels'
import { dateFormatter, parseDate } from '../../lib/format'

export function AdminConsole({
  authState,
  errorPopup,
  employees,
  employeesState,
  selectedEmployeeId,
  setSelectedEmployeeId,
  weekOffset,
  setWeekOffset,
  scheduleState,
  schedule,
  saveState,
  saveSchedule,
  updateDay,
  resetDay,
  employeeForm,
  updateEmployeeForm,
  isCreatingEmployee,
  setIsCreatingEmployee,
  employeeEditorOpen,
  setEmployeeEditorOpen,
  employeeSaveState,
  saveEmployee,
  changeEmployeeStatus,
  resetEmployeePassword,
  removeEmployee,
  availability,
  setErrorPopup,
  logout,
  onTabChange,
  realtimeVersions = {},
}) {
  const [activeTab, setActiveTab] = useState('employees')
  const [workArea, setWorkArea] = useState('outside')
  const [weekSelectionLocks, setWeekSelectionLocks] = useState({})
  const [employeeQuery, setEmployeeQuery] = useState('')
  const [employeeStatusFilter, setEmployeeStatusFilter] = useState('all')
  const employeeEditorRef = useRef(null)
  const employeeSheet = usePresence(employeeEditorOpen, 200)

  const updateWeekSelectionLock = useCallback((source, locked) => {
    setWeekSelectionLocks((current) => {
      if (Boolean(current[source]) === locked) return current
      const next = { ...current }
      if (locked) next[source] = true
      else delete next[source]
      return next
    })
  }, [])

  useEffect(() => {
    if (!employeeEditorOpen) return undefined
    const previousFocus = document.activeElement
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    employeeEditorRef.current?.focus()
    const closeOnEscape = (event) => {
      if (event.key === 'Escape') {
        setIsCreatingEmployee(false)
        setEmployeeEditorOpen(false)
      }
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => {
      window.removeEventListener('keydown', closeOnEscape)
      document.body.style.overflow = previousOverflow
      previousFocus?.focus?.()
    }
  }, [employeeEditorOpen, setEmployeeEditorOpen, setIsCreatingEmployee])

  function switchTab(tab) {
    setActiveTab(tab)
    onTabChange()
  }

  function openEmployee(employeeId) {
    setSelectedEmployeeId(String(employeeId))
    setIsCreatingEmployee(false)
    setEmployeeEditorOpen(true)
  }

  function addEmployee() {
    setSelectedEmployeeId('')
    setIsCreatingEmployee(true)
    setEmployeeEditorOpen(true)
    updateEmployeeForm('employeeNumber', '')
    updateEmployeeForm('firstName', '')
    updateEmployeeForm('lastName', '')
    updateEmployeeForm('phoneNumber', '')
    updateEmployeeForm('payrollNumber', '')
    updateEmployeeForm('hourlyRate', 14.5)
    updateEmployeeForm('maximumWeeklyHours', 45)
    updateEmployeeForm('roles', ['Driver'])
    updateEmployeeForm('driverType', 'Car')
    updateEmployeeForm('isOwn', true)
  }

  function closeEmployeeEditor() {
    setIsCreatingEmployee(false)
    setEmployeeEditorOpen(false)
  }

  function selectScheduleEmployee(event) {
    setSelectedEmployeeId(event.target.value)
  }

  const selectedEmployee = employees.find(
    (employee) => String(employee.id) === selectedEmployeeId,
  )
  const activeEmployeeCount = employees.filter((employee) => employee.isActive).length
  const inactiveEmployeeCount = employees.length - activeEmployeeCount
  const normalizedEmployeeQuery = employeeQuery.trim().toLowerCase()
  const visibleEmployees = employees.filter((employee) => {
    if (employeeStatusFilter === 'active' && !employee.isActive) return false
    if (employeeStatusFilter === 'inactive' && employee.isActive) return false
    if (!normalizedEmployeeQuery) return true
    return [
      `${employee.firstName} ${employee.lastName}`,
      employee.employeeNumber,
      employee.payrollNumber,
      employee.phoneNumber,
    ].some((value) => String(value || '').toLowerCase().includes(normalizedEmployeeQuery))
  })
  const groupedEmployees = employeeRoleGroups
    .map((group) => ({
      ...group,
      employees: visibleEmployees
        .filter((employee) => getEmployeeRoleGroup(employee).id === group.id)
        .sort(compareEmployees),
    }))
    .filter((group) => group.employees.length > 0)
  const employeesById = new Map(employees.map((employee) => [String(employee.id), employee]))
  const groupedAvailability = driverRosterRoleGroups
    .map((group) => ({
      ...group,
      employees: (availability?.employees || [])
        .filter((employeeSchedule) => {
          const employee = employeeSchedule.roles?.length ? employeeSchedule : employeesById.get(String(employeeSchedule.employeeId)) || { roles: ['Driver'] }
          return getRosterRoleGroup(employee)?.id === group.id
        })
        .sort((first, second) => first.employeeName.localeCompare(second.employeeName, undefined, { sensitivity: 'base' })),
    }))
  const employeesWithoutAvailability = (availability?.employees || [])
    .filter((employeeSchedule) => !employeeSchedule.days.some((day) => day.startTime && day.finishTime))
  const employeesWithApproximateHours = (availability?.employees || [])
    .filter((employeeSchedule) => employeeSchedule.approximateHours != null)
  const totalApproximateHours = employeesWithApproximateHours.length > 0
    ? employeesWithApproximateHours.reduce((total, employeeSchedule) => total + Number(employeeSchedule.approximateHours), 0)
    : null
  const totalApproximateCapacityHours = employeesWithApproximateHours.length > 0
    ? employeesWithApproximateHours.reduce((total, employeeSchedule) => total + Number(employeeSchedule.approximateCapacityHours ?? 0), 0)
    : null
  const roleDetails = {
    Driver: { label: 'Driver', description: 'Delivers orders and is included in driver rosters.' },
    InStore: { label: 'In-store', description: 'Works inside the store and is rostered on the inside roster.' },
    Manager: { label: 'Manager', description: 'Opens the admin console to manage staff, demand and rosters.' },
    Admin: { label: 'Administrator', description: 'Full access, including store settings and roster generation.' },
  }
  const editableRoles = authState.user.isAdmin
    ? ['Driver', 'InStore', 'Manager', 'Admin']
    : ['Driver', 'InStore', 'Manager']
  const selectedIsAdmin = selectedEmployee?.roles?.includes('Admin')
  const selectedIsOwnAdmin = selectedIsAdmin && selectedEmployee.id === authState.user.employeeId
  const protectedAdminActions = selectedIsAdmin && (!authState.user.isAdmin || selectedIsOwnAdmin)
  const employeeNumberErrors = getEmployeeFieldErrors(employeeSaveState, 'employeeNumber')
  const firstNameErrors = getEmployeeFieldErrors(employeeSaveState, 'firstName')
  const lastNameErrors = getEmployeeFieldErrors(employeeSaveState, 'lastName')
  const phoneNumberErrors = getEmployeeFieldErrors(employeeSaveState, 'phoneNumber')
  const payrollNumberErrors = getEmployeeFieldErrors(employeeSaveState, 'payrollNumber')
  const roleErrors = getEmployeeFieldErrors(employeeSaveState, 'roles')
  const hourlyRateErrors = getEmployeeFieldErrors(employeeSaveState, 'hourlyRate')
  const maximumWeeklyHoursErrors = getEmployeeFieldErrors(employeeSaveState, 'maximumWeeklyHours')
  const driverTypeErrors = getEmployeeFieldErrors(employeeSaveState, 'driverType')
  const selectedDemandRealtimeKey = workArea === 'inside'
    ? realtimeVersions.demandInside
    : realtimeVersions.demandDrivers
  const planningRealtimeKey = (selectedDemandRealtimeKey || 0) +
    (realtimeVersions.availability || 0) + (realtimeVersions.employees || 0) +
    (realtimeVersions.sickLeave || 0) + (realtimeVersions.settings || 0) +
    (workArea === 'inside' ? 0 : (realtimeVersions.rosterDrivers || 0))
  const selectedRosterRealtimeKey = workArea === 'inside'
    ? (realtimeVersions.rosterInside || 0) + (realtimeVersions.demandInside || 0)
    : (realtimeVersions.rosterDrivers || 0) + (realtimeVersions.demandDrivers || 0)

  const isSystemAdmin = authState.user.isAdmin
  const weekScopedTabs = new Set(['roster', 'employee-availability', 'my-availability', 'demand', 'generate-roster', 'saved-rosters'])
  const navGroups = [
    {
      label: 'People',
      items: [
        { id: 'employees', label: 'Employees', icon: 'people' },
        { id: 'holiday', label: 'Holiday', icon: 'sun' },
        { id: 'sick-leave', label: 'Sick leave', icon: 'cross' },
        { id: 'absent', label: 'Absent', icon: 'absent' },
      ],
    },
    {
      label: 'Availability',
      items: [
        { id: 'roster', label: 'Availability roster', icon: 'grid' },
        isSystemAdmin && { id: 'employee-availability', label: 'Employee availability', icon: 'clock' },
        !isSystemAdmin && authState.user.employeeId && { id: 'my-availability', label: 'My availability', icon: 'person' },
      ].filter(Boolean),
    },
    {
      label: 'Planning',
      items: [
        { id: 'demand', label: 'Demand', icon: 'chart' },
        isSystemAdmin && { id: 'generate-roster', label: 'Generate roster', icon: 'sparkles' },
        { id: 'saved-rosters', label: 'Saved rosters', icon: 'folder' },
      ].filter(Boolean),
    },
    isSystemAdmin && {
      label: 'Store',
      items: [{ id: 'store-settings', label: 'Store settings', icon: 'gear' }],
    },
  ].filter(Boolean)

  return (
    <>
      <ErrorPopup message={errorPopup} onClose={() => setErrorPopup('')} />
      <AppShell
        consoleLabel="Admin console"
        roleLabel={isSystemAdmin ? 'Administrator' : 'Manager'}
        userName={authState.user.employeeName || authState.user.username}
        navGroups={navGroups}
        activeId={activeTab}
        onNavigate={switchTab}
        onSignOut={logout}
        toolbar={weekScopedTabs.has(activeTab) && (
          <WeekSelector
            weekOffset={weekOffset}
            onChange={setWeekOffset}
            allowAllWeeks
            disabled={Object.keys(weekSelectionLocks).length > 0}
          />
        )}
      >

        {activeTab === 'employees' && (
          <section className="admin-tools">
            <div className="section-heading">
              <div>
                <span className="eyebrow">Administration</span>
                <h2>Employees</h2>
                <p className="employee-active-count" aria-live="polite">
                  <strong>{employeesState.status === 'success' ? activeEmployeeCount : '—'}</strong>{' '}
                  active {activeEmployeeCount === 1 ? 'employee' : 'employees'}
                </p>
              </div>
              <button type="button" onClick={addEmployee}>Add employee</button>
            </div>

            <div className="directory-toolbar">
              <label className="search-field">
                <span className="visually-hidden">Search employees</span>
                <input
                  type="search"
                  value={employeeQuery}
                  onChange={(event) => setEmployeeQuery(event.target.value)}
                  placeholder="Search by name, number or phone"
                  autoComplete="off"
                />
              </label>
              <div className="segmented-control" role="group" aria-label="Filter by status">
                {[
                  ['all', `All ${employees.length}`],
                  ['active', `Active ${activeEmployeeCount}`],
                  ['inactive', `Inactive ${inactiveEmployeeCount}`],
                ].map(([value, label]) => (
                  <button
                    type="button"
                    key={value}
                    className={employeeStatusFilter === value ? 'active' : ''}
                    aria-pressed={employeeStatusFilter === value}
                    onClick={() => setEmployeeStatusFilter(value)}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>

            {employeesState.status === 'loading' && (
              <p className="message info-message">Loading employees…</p>
            )}
            {employeesState.status === 'error' && (
              <p className="message error-message">Unable to load employees.</p>
            )}

            <div className="employee-groups">
              {groupedEmployees.map((group) => (
                <section className={`employee-group employee-group-${group.id}`} key={group.id}>
                  <div className="employee-group-heading">
                    <div>
                      <h3 className="employee-group-label">{group.label}</h3>
                      <p>{group.description}</p>
                    </div>
                    <span className="employee-group-count">
                      {group.employees.length} {group.employees.length === 1 ? 'employee' : 'employees'}
                    </span>
                  </div>

                  <div className="employee-card-grid">
                    {group.employees.map((employee) => (
                      <button
                        type="button"
                        className={`employee-card ${employee.isActive ? '' : 'inactive'}`}
                        key={employee.id}
                        onClick={() => openEmployee(employee.id)}
                        aria-label={`Edit ${employee.firstName} ${employee.lastName}`}
                      >
                        <span className="employee-card-top">
                          <span className="employee-avatar" aria-hidden="true">
                            {`${employee.firstName?.[0] || ''}${employee.lastName?.[0] || ''}`.toUpperCase()}
                          </span>
                          <span className="employee-card-name">
                            <strong>{employee.firstName} {employee.lastName}</strong>
                            <span>#{employee.employeeNumber} · {(employee.roles || []).join(', ') || 'No role'}</span>
                          </span>
                          <span className={`status-pill ${employee.isActive ? 'is-active' : 'is-inactive'}`}>
                            {employee.isActive ? 'Active' : 'Inactive'}
                          </span>
                        </span>
                        <span className="employee-card-facts">
                          <span><small>Pay</small>€{Number(employee.hourlyRate ?? 14.5).toFixed(2)}/h</span>
                          <span><small>Max</small>{employee.maximumWeeklyHours ?? 45}h/wk</span>
                          {employeeHasRole(employee, 'Driver') && (
                            <span><small>Vehicle</small>{employee.driverType === 'EBike' ? 'E-bike' : employee.driverType || 'Car'}{employee.isOwn !== false ? '' : ' (company)'}</span>
                          )}
                          <span><small>Phone</small>{employee.phoneNumber || '—'}</span>
                        </span>
                        <span className="employee-card-meta">
                          <span>Payroll {employee.payrollNumber || 'not set'}</span>
                          <span className={employee.hasChangedPassword ? '' : 'needs-attention'}>
                            {employee.hasChangedPassword == null ? 'No login' : employee.hasChangedPassword ? 'Password set' : 'Initial password'}
                          </span>
                        </span>
                      </button>
                    ))}
                  </div>
                </section>
              ))}
            </div>

            {employees.length === 0 && employeesState.status === 'success' && (
              <p className="message info-message">No employees are available yet.</p>
            )}
            {employees.length > 0 && visibleEmployees.length === 0 && (
              <div className="empty-state">
                <strong>No matching employees</strong>
                <span>Try a different name or number, or change the status filter.</span>
                <button type="button" className="secondary-button" onClick={() => { setEmployeeQuery(''); setEmployeeStatusFilter('all') }}>
                  Clear search
                </button>
              </div>
            )}

            {employeeSheet.mounted && (
              <div
                className={`sheet-backdrop${employeeSheet.closing ? ' is-closing' : ''}`}
                onMouseDown={(event) => {
                  if (event.target === event.currentTarget) closeEmployeeEditor()
                }}
              >
              <section
                ref={employeeEditorRef}
                className="employee-editor-card sheet"
                role="dialog"
                aria-modal="true"
                aria-labelledby="employee-editor-title"
                tabIndex={-1}
              >
                <div className="section-heading">
                  <div>
                    <span className="eyebrow">Employee card</span>
                    <h2 id="employee-editor-title">
                      {isCreatingEmployee ? 'Add employee' : `${selectedEmployee?.firstName || ''} ${selectedEmployee?.lastName || ''}`.trim() || 'Edit employee'}
                    </h2>
                  </div>
                  <button type="button" className="secondary-button" onClick={closeEmployeeEditor}>
                    Close
                  </button>
                </div>

                <p className="employee-editor-help">
                  {isCreatingEmployee
                    ? 'Create a profile with the details used for scheduling, availability, and payroll.'
                    : 'Update this profile here. Changes are applied to future roster generation and availability.'}
                </p>

                {employeeSaveState.status === 'error' && (
                  <div className="employee-save-alert" role="alert" aria-live="assertive">
                    <strong>Employee could not be saved</strong>
                    <p>{employeeSaveState.message}</p>
                    {Object.entries(employeeSaveState.fieldErrors || {}).map(([field, messages]) => (
                      <div className="employee-save-alert-detail" key={field}>
                        <strong>{employeeFieldLabels[field] || field || 'General'}</strong>
                        <span>{messages.join(' ')}</span>
                      </div>
                    ))}
                  </div>
                )}

                <form className="employee-form" onSubmit={saveEmployee}>
                  <div className="employee-form-field">
                    <label htmlFor="admin-employee-number">Employee number</label>
                    <input
                      id="admin-employee-number"
                      value={employeeForm.employeeNumber}
                      onChange={(event) => updateEmployeeForm('employeeNumber', event.target.value)}
                      aria-invalid={employeeNumberErrors.length > 0}
                      aria-describedby={employeeNumberErrors.length > 0 ? 'admin-employee-employeeNumber-error' : undefined}
                      required
                    />
                    <EmployeeFieldError field="employeeNumber" messages={employeeNumberErrors} />
                  </div>
                  <div className="employee-form-field">
                    <label htmlFor="admin-employee-first-name">First name</label>
                    <input
                      id="admin-employee-first-name"
                      value={employeeForm.firstName}
                      onChange={(event) => updateEmployeeForm('firstName', event.target.value)}
                      aria-invalid={firstNameErrors.length > 0}
                      aria-describedby={firstNameErrors.length > 0 ? 'admin-employee-firstName-error' : undefined}
                      required
                    />
                    <EmployeeFieldError field="firstName" messages={firstNameErrors} />
                  </div>
                  <div className="employee-form-field">
                    <label htmlFor="admin-employee-last-name">Last name</label>
                    <input
                      id="admin-employee-last-name"
                      value={employeeForm.lastName}
                      onChange={(event) => updateEmployeeForm('lastName', event.target.value)}
                      aria-invalid={lastNameErrors.length > 0}
                      aria-describedby={lastNameErrors.length > 0 ? 'admin-employee-lastName-error' : undefined}
                      required
                    />
                    <EmployeeFieldError field="lastName" messages={lastNameErrors} />
                  </div>
                  <div className="employee-form-field">
                    <label htmlFor="admin-employee-phone">Phone number</label>
                    <input
                      id="admin-employee-phone"
                      value={employeeForm.phoneNumber}
                      onChange={(event) => updateEmployeeForm('phoneNumber', event.target.value)}
                      aria-invalid={phoneNumberErrors.length > 0}
                      aria-describedby={phoneNumberErrors.length > 0 ? 'admin-employee-phoneNumber-error' : undefined}
                    />
                    <EmployeeFieldError field="phoneNumber" messages={phoneNumberErrors} />
                  </div>
                  <div className="employee-form-field">
                    <label htmlFor="admin-employee-payroll-number">Payroll number</label>
                    <input
                      id="admin-employee-payroll-number"
                      value={employeeForm.payrollNumber}
                      onChange={(event) => updateEmployeeForm('payrollNumber', event.target.value)}
                      aria-invalid={payrollNumberErrors.length > 0}
                      aria-describedby={payrollNumberErrors.length > 0 ? 'admin-employee-payrollNumber-error' : undefined}
                    />
                    <EmployeeFieldError field="payrollNumber" messages={payrollNumberErrors} />
                  </div>

                  <div className="employee-form-field">
                    <label htmlFor="admin-employee-hourly-rate">Hourly pay rate (€)</label>
                    <input id="admin-employee-hourly-rate" type="number" min="0" max="10000" step="0.01" required
                      value={employeeForm.hourlyRate ?? 14.5}
                      onChange={(event) => updateEmployeeForm('hourlyRate', event.target.value === '' ? '' : Number(event.target.value))}
                      aria-invalid={hourlyRateErrors.length > 0}
                      aria-describedby={`admin-employee-hourly-rate-help${hourlyRateErrors.length > 0 ? ' admin-employee-hourlyRate-error' : ''}`} />
                    <EmployeeFieldError field="hourlyRate" messages={hourlyRateErrors} />
                    <small id="admin-employee-hourly-rate-help">
                      Default €14.50. Used with actual saved shifts to calculate labour.
                      {' '}{employeeForm.roles?.includes('Manager')
                        ? 'Managers receive this same rate on Sunday.'
                        : 'Hours worked on Sunday receive an extra 25%.'}
                    </small>
                  </div>

                  <div className="employee-form-field">
                    <label htmlFor="admin-employee-maximum-weekly-hours">Maximum weekly hours</label>
                    <input id="admin-employee-maximum-weekly-hours" type="number" min="0" max="168" step="1" required
                      value={employeeForm.maximumWeeklyHours ?? 45}
                      onChange={(event) => updateEmployeeForm('maximumWeeklyHours', event.target.value === '' ? '' : Number(event.target.value))}
                      aria-invalid={maximumWeeklyHoursErrors.length > 0}
                      aria-describedby={`admin-employee-maximum-weekly-hours-help${maximumWeeklyHoursErrors.length > 0 ? ' admin-employee-maximumWeeklyHours-error' : ''}`} />
                    <EmployeeFieldError field="maximumWeeklyHours" messages={maximumWeeklyHoursErrors} />
                    <small id="admin-employee-maximum-weekly-hours-help">
                      Default 45 hours. Caps automatic approximate hours for this employee; demand coverage can still require more scheduled hours.
                    </small>
                  </div>

                  <fieldset
                    className="employee-role-fieldset"
                    aria-invalid={roleErrors.length > 0}
                    aria-describedby={roleErrors.length > 0 ? 'admin-employee-roles-error' : undefined}
                  >
                    <legend>Roles</legend>
                    <p className="employee-role-hint">Choose at least one. An employee can have several.</p>
                    <div className="employee-role-options">
                      {editableRoles.map((role) => {
                        const details = roleDetails[role] || { label: role, description: '' }
                        const checked = employeeForm.roles?.includes(role) || false
                        return (
                          <label
                            className={`employee-role-option${checked ? ' is-checked' : ''}`}
                            htmlFor={`admin-employee-role-${role}`}
                            key={role}
                          >
                            <input
                              id={`admin-employee-role-${role}`}
                              type="checkbox"
                              checked={checked}
                              disabled={(role === 'Admin' && selectedIsOwnAdmin) || (selectedIsAdmin && !authState.user.isAdmin)}
                              aria-describedby={`admin-employee-role-${role}-description`}
                              onChange={(event) => {
                                const currentRoles = employeeForm.roles || []
                                const nextRoles = event.target.checked
                                  ? [...new Set([...currentRoles, role])]
                                  : currentRoles.filter((item) => item !== role)
                                updateEmployeeForm('roles', nextRoles)
                              }}
                            />
                            <span className="employee-role-copy">
                              <strong>{details.label}</strong>
                              <small id={`admin-employee-role-${role}-description`}>{details.description}</small>
                            </span>
                          </label>
                        )
                      })}
                    </div>
                    <EmployeeFieldError field="roles" messages={roleErrors} />
                  </fieldset>

                  {employeeForm.roles?.includes('Driver') && (
                    <>
                      <div className="employee-form-field">
                        <label htmlFor="admin-employee-driver-type">Driver type</label>
                        <select
                          id="admin-employee-driver-type"
                          value={employeeForm.driverType}
                          onChange={(event) => updateEmployeeForm('driverType', event.target.value)}
                          aria-invalid={driverTypeErrors.length > 0}
                          aria-describedby={driverTypeErrors.length > 0 ? 'admin-employee-driverType-error' : undefined}
                        >
                          <option value="Car">Car</option>
                          <option value="Moped">Moped</option>
                          <option value="EBike">E-bike</option>
                        </select>
                        <EmployeeFieldError field="driverType" messages={driverTypeErrors} />
                      </div>
                      <div className="employee-form-field">
                        <label htmlFor="admin-employee-vehicle-ownership">Vehicle ownership</label>
                        <select id="admin-employee-vehicle-ownership"
                          value={employeeForm.isOwn !== false ? 'own' : 'company'}
                          onChange={(event) => updateEmployeeForm('isOwn', event.target.value === 'own')}>
                          <option value="own">Own vehicle</option>
                          <option value="company">Company vehicle</option>
                        </select>
                        <small>Company vehicles are shared. Overlapping shifts cannot exceed the saved count for this driver's vehicle type.</small>
                      </div>
                    </>
                  )}

                  <div className="employee-form-actions">
                    {!isCreatingEmployee && selectedEmployee && authState.user.isAdmin && (
                      <button type="button" className="secondary-button" onClick={resetEmployeePassword}
                        disabled={employeeSaveState.status === 'saving'}>Reset password</button>
                    )}
                    <button type="submit" disabled={employeeSaveState.status === 'saving' || (selectedIsAdmin && !authState.user.isAdmin)}>
                      {employeeSaveState.status === 'saving' ? 'Saving…' : 'Save employee'}
                    </button>
                    {!isCreatingEmployee && selectedEmployee && (
                      <button
                        type="button"
                        className="secondary-button"
                        onClick={() => changeEmployeeStatus(selectedEmployee.isActive ? 'deactivate' : 'reactivate')}
                        disabled={protectedAdminActions}
                      >
                        {selectedEmployee.isActive ? 'Deactivate' : 'Reactivate'}
                      </button>
                    )}
                    {!isCreatingEmployee && selectedEmployee && (
                      <button type="button" className="danger-button" onClick={removeEmployee} disabled={protectedAdminActions}>
                        Remove employee
                      </button>
                    )}
                  </div>
                  {employeeSaveState.message && (
                    <p className={`save-message ${employeeSaveState.status}`} role={employeeSaveState.status === 'error' ? 'alert' : undefined}>
                      {employeeSaveState.message}
                    </p>
                  )}
                </form>
              </section>
              </div>
            )}
          </section>
        )}

        {activeTab === 'holiday' && (
          authState.user.isAdmin
            ? <AdminHolidayPanel setErrorPopup={setErrorPopup} realtimeKey={realtimeVersions.holiday} />
            : <>
              <AdminHolidayPanel setErrorPopup={setErrorPopup} canExport={false} realtimeKey={realtimeVersions.holiday} />
              <HolidayPanel setErrorPopup={setErrorPopup} realtimeKey={realtimeVersions.holiday} />
            </>
        )}

        {activeTab === 'sick-leave' && <AdminSickLeavePanel setErrorPopup={setErrorPopup} includeOwnRequest={!authState.user.isAdmin && Boolean(authState.user.employeeId)} realtimeKey={realtimeVersions.sickLeave} />}

        {activeTab === 'absent' && <AbsentPanel management admin={authState.user.isAdmin} fetchJson={fetchJson} realtimeKey={realtimeVersions.absent + realtimeVersions.employees + realtimeVersions.rosterDrivers} />}

        {activeTab === 'store-settings' && authState.user.isAdmin && <StoreSettingsPanel fetchJson={fetchJson} />}

        {activeTab === 'roster' && (
          <section className="availability-section admin-tools">
            <div className="section-heading">
              <div>
                <span className="eyebrow">Admin overview</span>
                <h2>Driver availability roster</h2>
              </div>
            </div>

            {availability && (
              <>
                <div className="week-range availability-week-range">
                  {dateFormatter.format(parseDate(availability.weekStart))} –{' '}
                  {dateFormatter.format(parseDate(availability.weekEnd))}
                </div>
                <ApproximateHoursPreview
                  approximateHours={totalApproximateHours}
                  capacityHours={totalApproximateCapacityHours}
                  label="Estimated driver hours"
                  detail={employeesWithApproximateHours.length > 0
                    ? `Automatic allocation across ${employeesWithApproximateHours.length} active ${employeesWithApproximateHours.length === 1 ? 'driver' : 'drivers'}. The generator uses these individual targets when balancing the roster.`
                    : 'Complete outside demand and driver availability to calculate the hours the roster will try to allocate.'}
                />
                <AvailabilityHeatmap heatmap={availability.heatmap} weekStart={availability.weekStart} />
                {employeesWithoutAvailability.length > 0 && (
                  <div className="availability-missing-summary" role="status">
                    <strong>
                      {employeesWithoutAvailability.length}{' '}
                      {employeesWithoutAvailability.length === 1 ? 'driver has' : 'drivers have'} no availability entered
                    </strong>
                    <span>Highlighted rows need attention before roster generation.</span>
                  </div>
                )}
                <div className="roster-role-groups availability-role-groups">
                  {groupedAvailability.map((group) => (
                    <section className={`roster-role-group roster-role-group-${group.id}`} key={group.id}>
                      <div className="roster-role-group-heading">
                        <div>
                          <h3>{group.label}</h3>
                          <p>{group.description}</p>
                        </div>
                        <span className="roster-role-group-count">
                          {group.employees.length} {group.employees.length === 1 ? 'employee' : 'employees'}
                        </span>
                      </div>
                      {group.employees.length > 0 ? (
                        <div className="availability-table" role="table">
                          <div className="availability-row availability-header" role="row">
                            <strong>Employee</strong>
                            {group.employees[0].days.map((day) => (
                              <span key={day.date}>{day.dayOfWeek.slice(0, 3)}</span>
                            ))}
                          </div>
                          {group.employees.map((employeeSchedule) => {
                            const hasAvailability = employeeSchedule.days.some((day) => day.startTime && day.finishTime)

                            return (
                              <div
                                className={`availability-row ${hasAvailability ? '' : 'availability-row-missing'}`}
                                role="row"
                                key={employeeSchedule.employeeId}
                              >
                                <strong className="availability-employee-name">
                                  <span>{employeeSchedule.employeeName}</span>
                                  {!hasAvailability && (
                                    <span className="availability-missing-badge">No availability entered</span>
                                  )}
                                  <span className={`availability-approximate-badge ${employeeSchedule.approximateHours == null ? 'pending' : ''}`}>
                                    {employeeSchedule.approximateHours == null
                                      ? 'Approx. hours pending'
                                      : `Approx. ${Number(employeeSchedule.approximateHours).toFixed(1)}h`}
                                  </span>
                                </strong>
                                {employeeSchedule.days.map((day) => (
                                  <span key={day.date}>
                                    {day.startTime && day.finishTime
                                      ? `${day.startTime}–${day.finishTime}`
                                      : 'Off'}
                                  </span>
                                ))}
                              </div>
                            )
                          })}
                        </div>
                      ) : (
                        <p className="roster-role-group-empty">No active employees in this group for the selected week.</p>
                      )}
                    </section>
                  ))}
                </div>
                {availability.employees.length === 0 && (
                  <p className="message info-message">No active employees.</p>
                )}
              </>
            )}
          </section>
        )}

        {activeTab === 'my-availability' && !authState.user.isAdmin && authState.user.employeeId && (
          <section className="admin-tools">
            <div className="section-heading">
              <div>
                <span className="eyebrow">Personal schedule</span>
                <h2>My availability</h2>
                <p>Set the hours you are available to work for the selected week.</p>
              </div>
            </div>
            {scheduleState.status === 'loading' && (
              <p className="message info-message">Loading availability…</p>
            )}
            {scheduleState.status === 'error' && (
              <p className="message error-message">{scheduleState.message}</p>
            )}
            <ScheduleEditor
              schedule={schedule}
              saveState={saveState}
              saveSchedule={saveSchedule}
              updateDay={updateDay}
              resetDay={resetDay}
            />
          </section>
        )}

        {activeTab === 'employee-availability' && authState.user.isAdmin && (
          <section className="admin-tools">
            <div className="section-heading">
              <div>
                <span className="eyebrow">Administration</span>
                <h2>Employee availability</h2>
              </div>
            </div>

            <div className="employee-picker">
              <label htmlFor="schedule-employee">Employee</label>
              <select
                id="schedule-employee"
                value={selectedEmployeeId}
                onChange={selectScheduleEmployee}
                disabled={employeesState.status !== 'success' || employees.length === 0}
              >
                <option value="">Select an employee</option>
                {employees.map((employee) => (
                  <option key={employee.id} value={employee.id}>
                    {employee.firstName} {employee.lastName}{employee.isActive ? '' : ' (inactive)'}
                  </option>
                ))}
              </select>
            </div>

            {scheduleState.status === 'loading' && (
              <p className="message info-message">Loading week…</p>
            )}
            {scheduleState.status === 'error' && (
              <p className="message error-message">Unable to load employee availability.</p>
            )}

            <ScheduleEditor
              schedule={schedule}
              saveState={saveState}
              saveSchedule={saveSchedule}
              updateDay={updateDay}
              resetDay={resetDay}
              eyebrow="Schedule for"
              lockedMessage="This week is outside the three-week editing window and is read-only."
              estimateLabel={schedule ? `${schedule.employeeName}'s estimated roster hours` : undefined}
            />
          </section>
        )}

        {activeTab === 'demand' && <DemandManager
          setErrorPopup={setErrorPopup}
          employees={employees}
          canEdit={authState.user.isAdmin}
          weekOffset={weekOffset}
          demandKind={workArea}
          onDemandKindChange={setWorkArea}
          onWeekSelectionLockChange={updateWeekSelectionLock}
          realtimeKey={planningRealtimeKey}
        />}

        {authState.user.isAdmin && (
          <RosterGenerationPanel
            fetchJson={fetchJson}
            setErrorPopup={setErrorPopup}
            isVisible={activeTab === 'generate-roster'}
            onWeekSelectionLockChange={updateWeekSelectionLock}
            realtimeKey={planningRealtimeKey}
            weekOffset={weekOffset}
            onShowSaved={(weekStart) => {
              setWeekOffset(getWeekOffsetFromDate(weekStart))
              setWorkArea('outside')
              switchTab('saved-rosters')
            }}
          />
        )}
        {activeTab === 'saved-rosters' && <SavedRosterPanel
          fetchJson={fetchJson}
          setErrorPopup={setErrorPopup}
          canEdit={authState.user.isAdmin}
          weekOffset={weekOffset}
          workArea={workArea}
          setWorkArea={setWorkArea}
          onWeekSelectionLockChange={updateWeekSelectionLock}
          realtimeKey={selectedRosterRealtimeKey + (realtimeVersions.availability || 0) +
            (realtimeVersions.employees || 0) + (realtimeVersions.sickLeave || 0) +
            (realtimeVersions.settings || 0)}
        />}
      </AppShell>
    </>
  )
}
