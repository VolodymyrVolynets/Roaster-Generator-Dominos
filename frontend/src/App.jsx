import { Suspense, lazy, useEffect, useState } from 'react'
import { PasswordChangePrompt } from './features/auth/PasswordChangePrompt'
import { useApplicationEvents } from './hooks/useApplicationEvents'
import { getAvailabilityEmployeeId } from './lib/availabilityAccess'
import { getAvailabilityDemandError, getAvailabilityLimitErrors } from './lib/availabilityLimits'
import { fetchJson } from './api/client'
import { ErrorPopup } from './components/ErrorPopup'
import { LoginView } from './features/auth/LoginView'
import { emptyEmployeeForm, getEmployeeErrorSummary } from './features/employees/employeeForm'
import { minWeekOffset } from './lib/format'
import { confirmAction } from './components/ConfirmDialog'

// Admins and employees load only their own part of the app.
const AdminConsole = lazy(() => import('./features/admin/AdminConsole').then((module) => ({ default: module.AdminConsole })))
const loadWorkspace = () => import('./features/workspace/EmployeeWorkspace')
const DriverWorkspace = lazy(() => loadWorkspace().then((module) => ({ default: module.DriverWorkspace })))
const InStoreWorkspace = lazy(() => loadWorkspace().then((module) => ({ default: module.InStoreWorkspace })))
const EmployeeWorkspace = lazy(() => loadWorkspace().then((module) => ({ default: module.EmployeeWorkspace })))

function LoadingScreen({ label }) {
  return (
    <main className="page-shell centered-page splash" aria-busy="true">
      <span className="shell-brand-mark" aria-hidden="true">RG</span>
      <p role="status">{label}</p>
    </main>
  )
}

function App() {
  return (
    <Suspense fallback={<LoadingScreen label="Loading…" />}>
      <AppContent />
    </Suspense>
  )
}

function AppContent() {
  const [authState, setAuthState] = useState({ status: 'loading', user: null, message: '' })
  const [employees, setEmployees] = useState([])
  const [selectedEmployeeId, setSelectedEmployeeId] = useState('')
  const [employeesState, setEmployeesState] = useState({ status: 'loading' })
  const [scheduleState, setScheduleState] = useState({ status: 'idle' })
  const [schedule, setSchedule] = useState(null)
  const [saveState, setSaveState] = useState({ status: 'idle' })
  const [weekOffset, setWeekOffset] = useState(minWeekOffset)
  const [errorPopup, setErrorPopup] = useState('')
  const [employeeForm, setEmployeeForm] = useState(emptyEmployeeForm)
  const [isCreatingEmployee, setIsCreatingEmployee] = useState(false)
  const [employeeEditorOpen, setEmployeeEditorOpen] = useState(false)
  const [employeeSaveState, setEmployeeSaveState] = useState({ status: 'idle' })
  const [availability, setAvailability] = useState(null)
  const [dataRefreshKey, setDataRefreshKey] = useState(0)

  const isAuthenticated = authState.status === 'authenticated'
  const isSystemAdmin = isAuthenticated && authState.user.isAdmin
  const isManager = isAuthenticated && authState.user.roles?.includes('Manager')
  const isAdmin = isSystemAdmin || isManager
  const realtimeVersions = useApplicationEvents(isAuthenticated, authState.user?.employeeId)
  const scheduleRealtimeKey = realtimeVersions.availability + realtimeVersions.demandDrivers +
    realtimeVersions.sickLeave + realtimeVersions.employees + realtimeVersions.settings + realtimeVersions.rosterDrivers
  const availabilityEmployeeId = getAvailabilityEmployeeId(
    isAuthenticated ? authState.user : null,
    selectedEmployeeId,
  )

  useEffect(() => {
    function handleAuthExpired(event) {
      void fetchJson('/api/auth/logout', { method: 'POST' }).catch(() => {})

      setAuthState({
        status: 'anonymous',
        user: null,
        message: 'Your session has expired. Please sign in again.',
      })
      setEmployees([])
      setEmployeesState({ status: 'idle' })
      setSelectedEmployeeId('')
      setSchedule(null)
      setScheduleState({ status: 'idle' })
      setAvailability(null)
      setEmployeeEditorOpen(false)
      setIsCreatingEmployee(false)

      if (event.detail?.showMessage) {
        setErrorPopup('Your session has expired. Please sign in again.')
      }
    }

    window.addEventListener('auth-expired', handleAuthExpired)
    return () => window.removeEventListener('auth-expired', handleAuthExpired)
  }, [])

  useEffect(() => {
    fetchJson('/api/auth/me')
      .then((user) => setAuthState({ status: 'authenticated', user, message: '' }))
      .catch(() => setAuthState({ status: 'anonymous', user: null, message: '' }))
  }, [realtimeVersions.auth])

  useEffect(() => {
    if (!isAuthenticated) {
      return
    }

    setEmployeesState({ status: 'loading' })
    const endpoint = isAdmin ? '/api/admin/employees' : '/api/employees'

    fetchJson(endpoint)
      .then((payload) => {
        setEmployees(payload)
        const ownEmployeeId = authState.user.employeeId
        // Administrators manage other people's availability, so start them on the first active driver.
        const firstActiveDriver = payload.find((employee) => employee.isActive && employee.roles?.includes('Driver'))
        const defaultEmployee = authState.user.isAdmin && firstActiveDriver
          ? firstActiveDriver
          : ownEmployeeId
            ? payload.find((employee) => employee.id === ownEmployeeId)
            : payload.find((employee) => employee.isActive)
        setSelectedEmployeeId(defaultEmployee ? String(defaultEmployee.id) : '')
        setEmployeesState({ status: 'success' })
      })
      .catch((error) => {
        setEmployeesState({ status: 'error', message: error.message })
        setErrorPopup(error.message)
      })
  }, [authState, isAuthenticated, isAdmin, dataRefreshKey, realtimeVersions.employees])

  useEffect(() => {
    if (!availabilityEmployeeId) {
      setSchedule(null)
      setScheduleState({ status: 'idle' })
      return
    }

    setScheduleState({ status: 'loading' })
    setSchedule(null)
    setSaveState({ status: 'idle' })
    setErrorPopup('')

    let cancelled = false
    fetchJson(`/api/employees/${availabilityEmployeeId}/schedule?weekOffset=${weekOffset}`)
      .then((payload) => {
        if (cancelled) return
        setSchedule(payload)
        setScheduleState({ status: 'success' })
      })
      .catch((error) => {
        if (cancelled) return
        setScheduleState({ status: 'error', message: error.message })
        setErrorPopup(error.message)
      })
    return () => { cancelled = true }
  }, [availabilityEmployeeId, weekOffset, dataRefreshKey, scheduleRealtimeKey])

  useEffect(() => {
    if (!isAdmin) {
      setAvailability(null)
      return
    }

    fetchJson(`/api/admin/availability?weekOffset=${weekOffset}`)
      .then(setAvailability)
      .catch((error) => setErrorPopup(error.message))
  }, [isAdmin, weekOffset, dataRefreshKey, scheduleRealtimeKey])

  useEffect(() => {
    const employee = employees.find((item) => String(item.id) === selectedEmployeeId)

    if (employee && !isCreatingEmployee) {
      setEmployeeForm({
        employeeNumber: employee.employeeNumber,
        firstName: employee.firstName,
        lastName: employee.lastName,
        phoneNumber: employee.phoneNumber,
        payrollNumber: employee.payrollNumber || '',
        hourlyRate: employee.hourlyRate ?? 14.5,
        maximumWeeklyHours: employee.maximumWeeklyHours ?? 45,
        roles: employee.roles?.length ? employee.roles : ['Driver'],
        driverType: employee.driverType || 'Car',
        isOwn: employee.isOwn ?? true,
      })
    }
  }, [employees, selectedEmployeeId, isCreatingEmployee])

  function updateDay(date, field, value) {
    setSchedule((current) => ({
      ...current,
      days: current.days.map((day) =>
        day.date === date ? { ...day, [field]: value || null } : day,
      ),
    }))
    setSaveState({ status: 'idle' })
  }

  function resetDay(date) {
    setSchedule((current) => ({
      ...current,
      days: current.days.map((day) =>
        day.date === date ? { ...day, startTime: null, finishTime: null } : day,
      ),
    }))
    setSaveState({ status: 'idle' })
  }

  async function login(username, password) {
    setAuthState((current) => ({ ...current, status: 'logging-in', message: '' }))

    try {
      await fetchJson('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      })
      const user = await fetchJson('/api/auth/me')
      setAuthState({ status: 'authenticated', user, message: '' })
      setWeekOffset(minWeekOffset)
      setErrorPopup('')
    } catch (error) {
      // The sign-in form shows this message inline; a second toast would repeat it.
      setAuthState({ status: 'anonymous', user: null, message: error.message })
    }
  }

  async function logout() {
    try {
      await fetchJson('/api/auth/logout', { method: 'POST' })
    } catch (error) {
      setErrorPopup(error.message)
    }

    setAuthState({ status: 'anonymous', user: null, message: '' })
    setEmployees([])
    setSelectedEmployeeId('')
    setSchedule(null)
  }

  function updateEmployeeForm(field, value) {
    setEmployeeForm((current) => ({ ...current, [field]: value }))
    setEmployeeSaveState({ status: 'idle' })
  }

  async function saveEmployee(event) {
    event.preventDefault()
    if (!employeeForm.roles?.length) {
      const message = 'Select at least one employee role before saving.'
      setEmployeeSaveState({
        status: 'error',
        message,
        fieldErrors: { roles: ['At least one employee role is required.'] },
      })
      setErrorPopup(message)
      return
    }

    setEmployeeSaveState({ status: 'saving', message: 'Saving employee…', fieldErrors: {} })

    const endpoint = isCreatingEmployee
      ? '/api/admin/employees'
      : `/api/admin/employees/${selectedEmployeeId}`

    try {
      const savedEmployee = await fetchJson(endpoint, {
        method: isCreatingEmployee ? 'POST' : 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(employeeForm),
      })

      setEmployees((current) => {
        const withoutSaved = current.filter((employee) => employee.id !== savedEmployee.id)
        return [...withoutSaved, savedEmployee].sort((left, right) =>
          `${left.lastName} ${left.firstName}`.localeCompare(`${right.lastName} ${right.firstName}`),
        )
      })
      setSelectedEmployeeId(String(savedEmployee.id))
      setIsCreatingEmployee(false)
      setEmployeeEditorOpen(true)
      setEmployeeSaveState({ status: 'success', message: 'Employee saved.', fieldErrors: {} })
    } catch (error) {
      const message = getEmployeeErrorSummary(error)
      setEmployeeSaveState({
        status: 'error',
        message,
        fieldErrors: error.fieldErrors || {},
      })
      setErrorPopup(error.message || message)
    }
  }

  async function changeEmployeeStatus(action) {
    if (!selectedEmployeeId) {
      return
    }

    try {
      const employee = await fetchJson(
        `/api/admin/employees/${selectedEmployeeId}/${action}`,
        { method: 'POST' },
      )
      setEmployees((current) =>
        current.map((item) => (item.id === employee.id ? employee : item)),
      )
      setEmployeeSaveState({
        status: 'success',
        message: action === 'deactivate' ? 'Employee deactivated.' : 'Employee reactivated.',
      })
    } catch (error) {
      setEmployeeSaveState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function removeEmployee() {
    if (!selectedEmployeeId || !(await confirmAction({
      title: 'Remove employee?',
      message: 'Their login is deleted. Roster, availability, sick leave, holiday requests and absent forms are kept.',
      confirmLabel: 'Remove employee',
      destructive: true,
    }))) {
      return
    }

    try {
      await fetchJson(`/api/admin/employees/${selectedEmployeeId}`, { method: 'DELETE' })
      setEmployees((current) => current.filter((employee) => String(employee.id) !== selectedEmployeeId))
      setSelectedEmployeeId('')
      setEmployeeForm(emptyEmployeeForm)
      setIsCreatingEmployee(false)
      setEmployeeEditorOpen(false)
      setEmployeeSaveState({ status: 'success', message: 'Employee removed. Their login was deleted and history was kept.' })
    } catch (error) {
      setEmployeeSaveState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function resetEmployeePassword() {
    if (!isSystemAdmin || !selectedEmployeeId || employeeSaveState.status === 'saving' ||
        !(await confirmAction({
          title: 'Reset password?',
          message: 'The password goes back to the initial password. Existing sessions end, and they must choose a new password after signing in.',
          confirmLabel: 'Reset password',
          destructive: true,
        }))) return
    setEmployeeSaveState({ status: 'saving', message: '' })
    try {
      const employee = await fetchJson(`/api/admin/employees/${selectedEmployeeId}/reset-password`, { method: 'POST' })
      setEmployees((current) => current.map((item) => item.id === employee.id ? employee : item))
      setEmployeeSaveState({ status: 'success', message: 'Password reset. The employee must change their initial password after signing in.' })
      if (employee.id === authState.user.employeeId) await logout()
    } catch (error) {
      setEmployeeSaveState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function saveSchedule(event) {
    event.preventDefault()
    if (!availabilityEmployeeId || schedule?.employeeId !== availabilityEmployeeId ||
        scheduleState.status !== 'success' || schedule?.canEdit === false || saveState.status === 'saving') {
      return
    }
    const limitMessage = [getAvailabilityDemandError(schedule), ...getAvailabilityLimitErrors(schedule)]
      .filter(Boolean).join(' ')
    if (limitMessage) {
      setSaveState({ status: 'error', message: limitMessage })
      setErrorPopup(limitMessage)
      return
    }
    setSaveState({ status: 'saving' })
    setErrorPopup('')

    try {
      const payload = await fetchJson(
        `/api/employees/${availabilityEmployeeId}/schedule`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            weekOffset,
            days: schedule.days.map((day) => ({
              date: day.date,
              startTime: day.startTime || null,
              finishTime: day.finishTime || null,
            })),
          }),
        },
      )

      setSchedule(payload)
      setSaveState({ status: 'success', message: 'Availability saved.' })
    } catch (error) {
      setSaveState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  if (authState.status === 'loading') {
    return <LoadingScreen label="Checking sign-in…" />
  }

  if (!isAuthenticated) {
    return (
      <>
        <ErrorPopup message={errorPopup} onClose={() => setErrorPopup('')} />
        <LoginView
          onLogin={login}
          error={authState.message}
          isSubmitting={authState.status === 'logging-in'}
        />
      </>
    )
  }

  if (!authState.user.hasChangedPassword) {
    return <PasswordChangePrompt fetchJson={fetchJson} onSignOut={logout} onChanged={() =>
      setAuthState((current) => ({ ...current, user: { ...current.user, hasChangedPassword: true } }))
    } />
  }

  if (isAdmin) {
    return (
      <AdminConsole
        authState={authState}
        errorPopup={errorPopup}
        employees={employees}
        employeesState={employeesState}
        selectedEmployeeId={selectedEmployeeId}
        setSelectedEmployeeId={setSelectedEmployeeId}
        weekOffset={weekOffset}
        setWeekOffset={setWeekOffset}
        scheduleState={scheduleState}
        schedule={schedule}
        saveState={saveState}
        saveSchedule={saveSchedule}
        updateDay={updateDay}
        resetDay={resetDay}
        employeeForm={employeeForm}
        updateEmployeeForm={updateEmployeeForm}
        isCreatingEmployee={isCreatingEmployee}
        setIsCreatingEmployee={setIsCreatingEmployee}
        employeeEditorOpen={employeeEditorOpen}
        setEmployeeEditorOpen={setEmployeeEditorOpen}
        employeeSaveState={employeeSaveState}
        saveEmployee={saveEmployee}
        changeEmployeeStatus={changeEmployeeStatus}
        resetEmployeePassword={resetEmployeePassword}
        removeEmployee={removeEmployee}
        availability={availability}
        setErrorPopup={setErrorPopup}
        logout={logout}
        onTabChange={() => setDataRefreshKey((current) => current + 1)}
        realtimeVersions={realtimeVersions}
      />
    )
  }

  const employee = employees.find((item) => String(item.id) === selectedEmployeeId)
  const employeeRoles = authState.user.roles || []
  const employeeWorkspaceProps = {
    authState,
    employee,
    errorPopup,
    employeesState,
    scheduleState,
    schedule,
    saveState,
    weekOffset,
    setWeekOffset,
    saveSchedule,
    updateDay,
    resetDay,
    setErrorPopup,
    logout,
    realtimeVersions,
  }

  if (employeeRoles.includes('Driver')) {
    return <DriverWorkspace {...employeeWorkspaceProps} />
  }

  if (employeeRoles.includes('InStore')) {
    return <InStoreWorkspace {...employeeWorkspaceProps} />
  }

  return (
    <EmployeeWorkspace
      {...employeeWorkspaceProps}
      variant="employee"
      title="Employee workspace"
      lead="Set the hours you are available to work for the selected week."
    >
      <p className="message info-message">Your shared employee information is available to your manager.</p>
    </EmployeeWorkspace>
  )
}

export default App
