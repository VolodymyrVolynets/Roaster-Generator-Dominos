import { useEffect, useState } from 'react'
import { RosterGenerationPanel, SavedRosterPanel } from './RosterAdmin'

const dateFormatter = new Intl.DateTimeFormat(undefined, {
  day: 'numeric',
  month: 'short',
})

const hours = Array.from({ length: 24 }, (_, index) => String(index).padStart(2, '0'))
const minWeekOffset = 1
const maxWeekOffset = 3
const weekLabels = ['Current week', 'Next week', 'Week after next', 'Three weeks ahead']
const emptyEmployeeForm = {
  employeeNumber: '',
  firstName: '',
  lastName: '',
  phoneNumber: '',
  targetHours: 20,
  canWorkAlone: true,
}

function parseDate(dateValue) {
  const [year, month, day] = dateValue.split('-').map(Number)
  return new Date(year, month - 1, day)
}

function getNextMondayValue() {
  const date = new Date()
  const day = date.getDay()
  const daysUntilNextMonday = day === 0 ? 1 : 8 - day
  date.setDate(date.getDate() + daysUntilNextMonday)
  return date.toISOString().slice(0, 10)
}

function getShiftDuration(startTime, finishTime) {
  if (!startTime || !finishTime) {
    return null
  }

  const startHour = Number(startTime.split(':')[0])
  const finishHour = Number(finishTime.split(':')[0])
  const duration = finishHour > startHour
    ? finishHour - startHour
    : 24 - startHour + finishHour

  if (duration === 24) {
    return null
  }

  return `${duration} ${duration === 1 ? 'hour' : 'hours'}`
}

async function fetchJson(url, options) {
  const response = await fetch(url, { ...options, credentials: 'include' })
  const body = await response.text()
  let payload = null
  const requestPath = url.split('?')[0]

  try {
    payload = body ? JSON.parse(body) : null
  } catch {
    payload = null
  }

  if (!response.ok) {
    const validationMessages = payload?.errors
      ? Object.values(payload.errors).flatMap((messages) =>
          Array.isArray(messages) ? messages : [messages],
        )
      : []
    const messages = [payload?.message, payload?.detail, payload?.title, ...validationMessages]
      .filter(Boolean)
      .filter((message, index, allMessages) => allMessages.indexOf(message) === index)

    if (response.status === 401 &&
        requestPath !== '/api/auth/login' &&
        requestPath !== '/api/auth/logout') {
      window.dispatchEvent(new CustomEvent('auth-expired', {
        detail: { showMessage: requestPath !== '/api/auth/me' },
      }))
    }

    const message = response.status === 401 && requestPath !== '/api/auth/login'
      ? 'Your session has expired. Please sign in again.'
      : messages.join('\n') || body || 'The API request failed.'
    const error = new Error(message)
    error.status = response.status
    throw error
  }

  return payload
}

function ErrorPopup({ message, onClose }) {
  if (!message) {
    return null
  }

  return (
    <div className="error-popup" role="alert" aria-live="assertive">
      <div className="error-popup-header">
        <strong>Something went wrong</strong>
        <button
          type="button"
          className="error-popup-close"
          aria-label="Close error message"
          onClick={onClose}
        >
          ×
        </button>
      </div>
      <p>{message}</p>
    </div>
  )
}

function LoginView({ onLogin, error, isSubmitting }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')

  function submit(event) {
    event.preventDefault()
    onLogin(username, password)
  }

  return (
    <main className="page-shell centered-page">
      <section className="app-card login-card">
        <header className="page-header">
          <span className="eyebrow">Roaster Generator</span>
          <h1>Sign in</h1>
          <p className="lead">Use your employee number or administrator account.</p>
        </header>

        <form className="login-form" onSubmit={submit}>
          <label htmlFor="login-username">Username</label>
          <input
            id="login-username"
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            autoComplete="username"
            required
          />
          <label htmlFor="login-password">Password</label>
          <input
            id="login-password"
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
            required
          />
          {error && <p className="message error-message">{error}</p>}
          <button type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </section>
    </main>
  )
}

function DemandManager({ setErrorPopup }) {
  const [plans, setPlans] = useState([])
  const [selectedPlanId, setSelectedPlanId] = useState('')
  const [plan, setPlan] = useState(null)
  const [selectedDemandDayPosition, setSelectedDemandDayPosition] = useState(0)
  const [name, setName] = useState('Weekly demand')
  const [weekStart, setWeekStart] = useState(getNextMondayValue())
  const [pasteContent, setPasteContent] = useState('')
  const [status, setStatus] = useState({ status: 'idle', message: '' })

  useEffect(() => {
    fetchJson('/api/admin/demand')
      .then((payload) => {
        setPlans(payload)
        if (payload.length > 0) {
          setSelectedPlanId(String(payload[0].id))
        }
      })
      .catch((error) => setErrorPopup(error.message))
  }, [setErrorPopup])

  useEffect(() => {
    if (!selectedPlanId) {
      setPlan(null)
      return
    }

    fetchJson(`/api/admin/demand/${selectedPlanId}`)
      .then((payload) => {
        setPlan(payload)
        setSelectedDemandDayPosition(payload.columns[0]?.position ?? 0)
        setName(payload.name)
        setWeekStart(payload.weekStart)
        setStatus({ status: 'idle', message: '' })
      })
      .catch((error) => setErrorPopup(error.message))
  }, [selectedPlanId, setErrorPopup])

  function updateDemandValue(hour, position, rawValue) {
    const demand = rawValue === '' ? null : Number(rawValue)
    setPlan((current) => ({
      ...current,
      rows: current.rows.map((row) => {
        if (row.hour !== hour) {
          return row
        }

        const existing = row.values.find((item) => item.position === position)
        const nextValue = {
          position,
          deliveries: existing?.deliveries ?? null,
          demand,
        }

        return {
          ...row,
          values: [
            ...row.values.filter((item) => item.position !== position),
            nextValue,
          ].sort((left, right) => left.position - right.position),
        }
      }),
    }))
    setStatus({ status: 'idle', message: '' })
  }

  async function importPaste() {
    setStatus({ status: 'saving', message: '' })

    try {
      const payload = await fetchJson('/api/admin/demand/paste', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, weekStart, content: pasteContent }),
      })
      setPlan(payload)
      setSelectedDemandDayPosition(payload.columns[0]?.position ?? 0)
      setName(payload.name)
      setWeekStart(payload.weekStart)
      setSelectedPlanId(String(payload.id))
      setPasteContent('')
      setStatus({ status: 'success', message: 'Demand imported.' })
      await refreshPlans()
    } catch (error) {
      setStatus({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function importFile(event) {
    const file = event.target.files?.[0]
    event.target.value = ''

    if (!file) {
      return
    }

    setStatus({ status: 'saving', message: '' })
    const formData = new FormData()
    formData.append('file', file)
    formData.append('name', name)
    formData.append('weekStart', weekStart)

    try {
      const payload = await fetchJson('/api/admin/demand/upload', {
        method: 'POST',
        body: formData,
      })
      setPlan(payload)
      setSelectedDemandDayPosition(payload.columns[0]?.position ?? 0)
      setName(payload.name)
      setWeekStart(payload.weekStart)
      setSelectedPlanId(String(payload.id))
      setStatus({ status: 'success', message: 'Excel demand imported.' })
      await refreshPlans()
    } catch (error) {
      setStatus({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function refreshPlans() {
    const payload = await fetchJson('/api/admin/demand')
    setPlans(payload)
  }

  async function savePlan(event) {
    event.preventDefault()
    setStatus({ status: 'saving', message: '' })

    try {
      const payload = await fetchJson(`/api/admin/demand/${selectedPlanId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name,
          weekStart,
          columns: plan.columns.map((column) => ({
            position: column.position,
            label: column.label,
          })),
          rows: plan.rows.map((row) => ({
            hour: row.hour,
            values: row.values.map((value) => ({
              position: value.position,
              demand: value.demand,
            })),
          })),
        }),
      })
      setPlan(payload)
      setStatus({ status: 'success', message: 'Demand changes saved.' })
      await refreshPlans()
    } catch (error) {
      setStatus({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function deletePlan() {
    if (!selectedPlanId || !window.confirm('Delete this demand plan?')) {
      return
    }

    try {
      await fetchJson(`/api/admin/demand/${selectedPlanId}`, { method: 'DELETE' })
      const remainingPlans = plans.filter((item) => String(item.id) !== selectedPlanId)
      setPlans(remainingPlans)
      setSelectedPlanId(remainingPlans[0] ? String(remainingPlans[0].id) : '')
      setPlan(null)
      setStatus({ status: 'success', message: 'Demand plan deleted.' })
    } catch (error) {
      setErrorPopup(error.message)
    }
  }

  const selectedDemandColumn = plan?.columns.find(
    (column) => column.position === selectedDemandDayPosition,
  )
  const weeklyTotalHours = plan?.columns.reduce(
    (total, column) => total + (column.totalHours ?? 0),
    0,
  ) ?? 0

  return (
    <section className="admin-tools demand-tools">
      <div className="section-heading">
        <div>
          <span className="eyebrow">Administration</span>
          <h2>Demand input</h2>
        </div>
      </div>

      <p className="demand-help">
        Import the single weekly demand template as an Excel/CSV/table paste. Each pair of non-empty columns is a weekday: deliveries are imported
        and read-only, while demand is calculated from deliveries and can be edited below. Enter 0 when no drivers
        are needed; every open hour needs an explicit demand value before generation. Hours are shown
        as 06–23, followed by next-day 00–05. Importing new data replaces the existing template.
      </p>

      <label className="demand-paste-label">
        Paste demand table
        <textarea
          value={pasteContent}
          onChange={(event) => setPasteContent(event.target.value)}
          placeholder="Paste rows from Excel, CSV, TSV, or a Markdown table…"
          rows={6}
        />
      </label>

      <div className="demand-actions">
        <button type="button" onClick={importPaste} disabled={!pasteContent || status.status === 'saving'}>
          {status.status === 'saving' ? 'Importing…' : 'Import pasted table'}
        </button>
        <label className="secondary-button file-button">
          Import Excel
          <input type="file" accept=".xlsx,.xlsm" onChange={importFile} />
        </label>
      </div>

      {status.message && (
        <p className={`save-message ${status.status}`}>{status.message}</p>
      )}

      {plan && (
        <form onSubmit={savePlan}>
          <p className="demand-total-summary">
            Weekly total driver-hours: <strong>{weeklyTotalHours}</strong>
          </p>

          <div className="demand-mobile-controls">
            <label>
              Day
              <select
                value={selectedDemandDayPosition}
                onChange={(event) => setSelectedDemandDayPosition(Number(event.target.value))}
              >
                {plan.columns.map((column) => (
                  <option key={column.position} value={column.position}>
                    {column.label}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="demand-table-wrapper demand-desktop-view">
            <table className="demand-table demand-desktop-table">
              <thead>
                <tr>
                  <th rowSpan="2">Hour</th>
                  {plan.columns.map((column) => (
                    <th key={column.position} colSpan="2">
                      <span className="demand-day-label">{column.label}</span>
                      <small className="demand-day-hours">
                        {column.totalHours ?? 0} driver-hours
                      </small>
                    </th>
                  ))}
                </tr>
                <tr>
                  {plan.columns.flatMap((column) => [
                    <th key={`${column.position}-deliveries`}>Deliveries</th>,
                    <th key={`${column.position}-demand`}>Demand</th>,
                  ])}
                </tr>
              </thead>
              <tbody>
                {plan.rows.map((row) => (
                  <tr key={row.hour}>
                    <th>{String(row.hour).padStart(2, '0')}</th>
                    {plan.columns.flatMap((column) => {
                      const value = row.values.find((item) => item.position === column.position) || {}
                      return [
                        <td key={`${row.hour}-${column.position}-deliveries`} className="demand-readonly">
                          {value.deliveries ?? '—'}
                        </td>,
                        <td key={`${row.hour}-${column.position}-demand`}>
                          <input
                            type="number"
                            min="0"
                            step="1"
                            value={value.demand ?? ''}
                            onChange={(event) => updateDemandValue(
                              row.hour,
                              column.position,
                              event.target.value,
                            )}
                          />
                        </td>,
                      ]
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="demand-mobile-view">
            <div className="demand-table-wrapper">
              <table className="demand-table demand-mobile-table">
                <thead>
                  <tr>
                    <th colSpan="3">
                      <span className="demand-day-label">{selectedDemandColumn?.label ?? 'Day'}</span>
                      <small className="demand-day-hours">
                        {selectedDemandColumn?.totalHours ?? 0} driver-hours
                      </small>
                    </th>
                  </tr>
                  <tr>
                    <th>Hour</th>
                    <th>Deliveries</th>
                    <th>Demand</th>
                  </tr>
                </thead>
                <tbody>
                  {plan.rows.map((row) => {
                    const value = row.values.find(
                      (item) => item.position === selectedDemandDayPosition,
                    ) || {}

                    return (
                      <tr key={row.hour}>
                        <th>{String(row.hour).padStart(2, '0')}</th>
                        <td className="demand-readonly">{value.deliveries ?? '—'}</td>
                        <td>
                          <input
                            type="number"
                            min="0"
                            step="1"
                            value={value.demand ?? ''}
                            onChange={(event) => updateDemandValue(
                              row.hour,
                              selectedDemandDayPosition,
                              event.target.value,
                            )}
                          />
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </div>

          <div className="demand-actions">
            <button type="submit" disabled={status.status === 'saving'}>Save demand changes</button>
            <button type="button" className="secondary-button" onClick={deletePlan}>Delete plan</button>
          </div>
        </form>
      )}
    </section>
  )
}

function WeekSelector({ weekOffset, onChange, disabled = false }) {
  return (
    <label className="week-selector">
      Week
      <select
        value={weekOffset}
        onChange={(event) => onChange(Number(event.target.value))}
        disabled={disabled}
      >
        <option value={1}>Next week</option>
        <option value={2}>Week after next</option>
        <option value={3}>Three weeks ahead</option>
      </select>
    </label>
  )
}

function AdminConsole({
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
  removeEmployee,
  availability,
  setErrorPopup,
  logout,
  onTabChange,
}) {
  const [activeTab, setActiveTab] = useState('employees')
  const [savedWeekStart, setSavedWeekStart] = useState(null)

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
    updateEmployeeForm('targetHours', 20)
    updateEmployeeForm('canWorkAlone', true)
  }

  function closeEmployeeEditor() {
    setIsCreatingEmployee(false)
    setEmployeeEditorOpen(false)
  }

  function selectScheduleEmployee(event) {
    setSelectedEmployeeId(event.target.value)
    setWeekOffset(minWeekOffset)
  }

  const selectedEmployee = employees.find(
    (employee) => String(employee.id) === selectedEmployeeId,
  )
  const activeEmployeeCount = employees.filter((employee) => employee.isActive).length

  return (
    <>
      <ErrorPopup message={errorPopup} onClose={() => setErrorPopup('')} />
      <main className="page-shell">
        <section className="app-card">
          <header className="page-header">
            <span className="eyebrow">Roaster Generator</span>
            <h1>Admin console</h1>
            <p className="lead">Manage availability, configure demand, and generate balanced rosters.</p>
          </header>

          <div className="app-toolbar">
            <span className="role-badge">Admin</span>
            <button type="button" className="secondary-button" onClick={logout}>Sign out</button>
          </div>

          <nav className="admin-tabs" aria-label="Administrator sections">
            <button
              type="button"
              className={activeTab === 'employees' ? 'admin-tab active' : 'admin-tab'}
              onClick={() => switchTab('employees')}
            >
              Employees
            </button>
            <button
              type="button"
              className={activeTab === 'roster' ? 'admin-tab active' : 'admin-tab'}
              onClick={() => switchTab('roster')}
            >
              Availability roster
            </button>
            <button
              type="button"
              className={activeTab === 'employee-availability' ? 'admin-tab active' : 'admin-tab'}
              onClick={() => switchTab('employee-availability')}
            >
              Employee availability
            </button>
            <button
              type="button"
              className={activeTab === 'demand' ? 'admin-tab active' : 'admin-tab'}
              onClick={() => switchTab('demand')}
            >
              Demand
            </button>
            <button
              type="button"
              className={activeTab === 'generate-roster' ? 'admin-tab active' : 'admin-tab'}
              onClick={() => switchTab('generate-roster')}
            >
              Generate roster
            </button>
            <button
              type="button"
              className={activeTab === 'saved-rosters' ? 'admin-tab active' : 'admin-tab'}
              onClick={() => { setSavedWeekStart(null); switchTab('saved-rosters') }}
            >
              Saved rosters
            </button>
          </nav>

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

              {employeesState.status === 'loading' && (
                <p className="message info-message">Loading employees…</p>
              )}
              {employeesState.status === 'error' && (
                <p className="message error-message">Unable to load employees.</p>
              )}

              <div className="employee-card-grid">
                {employees.map((employee) => (
                  <button
                    type="button"
                    className={`employee-card ${employee.isActive ? '' : 'inactive'}`}
                    key={employee.id}
                    onClick={() => openEmployee(employee.id)}
                  >
                    <strong>{employee.firstName} {employee.lastName}</strong>
                    <span>Employee number: {employee.employeeNumber}</span>
                    <span>Phone: {employee.phoneNumber || 'Not set'}</span>
                    <span>Target hours: {employee.targetHours}</span>
                    <span>Can work alone: {employee.canWorkAlone ? 'Yes' : 'No'}</span>
                    <span className="employee-card-status">
                      {employee.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </button>
                ))}
              </div>

              {employees.length === 0 && employeesState.status === 'success' && (
                <p className="message info-message">No employees are available yet.</p>
              )}

              {employeeEditorOpen && (
                <section className="employee-editor-card">
                  <div className="section-heading">
                    <div>
                      <span className="eyebrow">Employee card</span>
                      <h2>{isCreatingEmployee ? 'Add employee' : 'Edit employee'}</h2>
                    </div>
                    <button type="button" className="secondary-button" onClick={closeEmployeeEditor}>
                      Close
                    </button>
                  </div>

                  <form className="employee-form" onSubmit={saveEmployee}>
                    <label htmlFor="admin-employee-number">Employee number</label>
                    <input
                      id="admin-employee-number"
                      value={employeeForm.employeeNumber}
                      onChange={(event) => updateEmployeeForm('employeeNumber', event.target.value)}
                      required
                    />
                    <label htmlFor="admin-employee-first-name">First name</label>
                    <input
                      id="admin-employee-first-name"
                      value={employeeForm.firstName}
                      onChange={(event) => updateEmployeeForm('firstName', event.target.value)}
                      required
                    />
                    <label htmlFor="admin-employee-last-name">Last name</label>
                    <input
                      id="admin-employee-last-name"
                      value={employeeForm.lastName}
                      onChange={(event) => updateEmployeeForm('lastName', event.target.value)}
                      required
                    />
                    <label htmlFor="admin-employee-phone">Phone number</label>
                    <input
                      id="admin-employee-phone"
                      value={employeeForm.phoneNumber}
                      onChange={(event) => updateEmployeeForm('phoneNumber', event.target.value)}
                    />
                    <label htmlFor="admin-employee-target-hours">Target hours per week</label>
                    <input
                      id="admin-employee-target-hours"
                      type="number"
                      min="3"
                      max="168"
                      step="1"
                      value={employeeForm.targetHours}
                      onChange={(event) => updateEmployeeForm('targetHours', Number(event.target.value))}
                      required
                    />
                    <label className="checkbox-label" htmlFor="admin-employee-can-work-alone">
                      <input
                        id="admin-employee-can-work-alone"
                        type="checkbox"
                        checked={employeeForm.canWorkAlone}
                        onChange={(event) => updateEmployeeForm('canWorkAlone', event.target.checked)}
                      />
                      Can work alone
                    </label>

                    <div className="employee-form-actions">
                      <button type="submit" disabled={employeeSaveState.status === 'saving'}>
                        {employeeSaveState.status === 'saving' ? 'Saving…' : 'Save employee'}
                      </button>
                      {!isCreatingEmployee && selectedEmployee && (
                        <button
                          type="button"
                          className="secondary-button"
                          onClick={() => changeEmployeeStatus(selectedEmployee.isActive ? 'deactivate' : 'reactivate')}
                        >
                          {selectedEmployee.isActive ? 'Deactivate' : 'Reactivate'}
                        </button>
                      )}
                      {!isCreatingEmployee && selectedEmployee && (
                        <button type="button" className="danger-button" onClick={removeEmployee}>
                          Remove completely
                        </button>
                      )}
                    </div>
                    {employeeSaveState.message && (
                      <p className={`save-message ${employeeSaveState.status}`}>
                        {employeeSaveState.message}
                      </p>
                    )}
                  </form>
                </section>
              )}
            </section>
          )}

          {activeTab === 'roster' && (
            <section className="availability-section admin-tools">
              <div className="section-heading">
                <div>
                  <span className="eyebrow">Admin overview</span>
                  <h2>Full availability roster</h2>
                </div>
                <WeekSelector weekOffset={weekOffset} onChange={setWeekOffset} />
              </div>

              {availability && (
                <>
                  <div className="week-range availability-week-range">
                    {dateFormatter.format(parseDate(availability.weekStart))} –{' '}
                    {dateFormatter.format(parseDate(availability.weekEnd))}
                  </div>
                  <div className="availability-table" role="table">
                    <div className="availability-row availability-header" role="row">
                      <strong>Employee</strong>
                      {availability.employees[0]?.days.map((day) => (
                        <span key={day.date}>{day.dayOfWeek.slice(0, 3)}</span>
                      ))}
                    </div>
                    {availability.employees.map((employeeSchedule) => (
                      <div className="availability-row" role="row" key={employeeSchedule.employeeId}>
                        <strong>{employeeSchedule.employeeName}</strong>
                        {employeeSchedule.days.map((day) => (
                          <span key={day.date}>
                            {day.startTime && day.finishTime
                              ? `${day.startTime}–${day.finishTime}`
                              : 'Off'}
                          </span>
                        ))}
                      </div>
                    ))}
                  </div>
                  {availability.employees.length === 0 && (
                    <p className="message info-message">No active employees.</p>
                  )}
                </>
              )}
            </section>
          )}

          {activeTab === 'employee-availability' && (
            <section className="admin-tools">
              <div className="section-heading">
                <div>
                  <span className="eyebrow">Administration</span>
                  <h2>Employee availability</h2>
                </div>
                <WeekSelector weekOffset={weekOffset} onChange={setWeekOffset} />
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

              {schedule && (
                <form className="schedule-form" onSubmit={saveSchedule}>
                  <div className="schedule-heading">
                    <div>
                      <span className="eyebrow">Schedule for</span>
                      <h2>{schedule.employeeName}</h2>
                    </div>
                    <span className="week-range">
                      {dateFormatter.format(parseDate(schedule.weekStart))} –{' '}
                      {dateFormatter.format(parseDate(schedule.weekEnd))}
                    </span>
                  </div>

                  <div className="schedule-table" role="table" aria-label="Employee schedule">
                    <div className="schedule-row schedule-header" role="row">
                      <span role="columnheader">Day</span>
                      <span role="columnheader">Start time</span>
                      <span role="columnheader">Finish time</span>
                      <span role="columnheader">Reset</span>
                    </div>
                    {schedule.days.map((day) => (
                      <div className="schedule-row" role="row" key={day.date}>
                        <div className="day-cell" role="cell">
                          <strong>{day.dayOfWeek}</strong>
                          <span>{dateFormatter.format(parseDate(day.date))}</span>
                          {getShiftDuration(day.startTime, day.finishTime) && (
                            <span className="shift-duration">
                              {getShiftDuration(day.startTime, day.finishTime)}
                            </span>
                          )}
                        </div>
                        <div role="cell">
                          <TimeSelector
                            id={`${day.date}-admin-start`}
                            label={`${day.dayOfWeek} start time`}
                            value={day.startTime || ''}
                            onChange={(value) => updateDay(day.date, 'startTime', value)}
                          />
                        </div>
                        <div role="cell">
                          <TimeSelector
                            id={`${day.date}-admin-finish`}
                            label={`${day.dayOfWeek} finish time`}
                            value={day.finishTime || ''}
                            onChange={(value) => updateDay(day.date, 'finishTime', value)}
                          />
                        </div>
                        <div role="cell">
                          <button
                            type="button"
                            className="reset-button"
                            onClick={() => resetDay(day.date)}
                            disabled={!day.startTime && !day.finishTime}
                          >
                            Reset
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                  <div className="form-footer">
                    <span className={`save-message ${saveState.status}`} aria-live="polite">
                      {saveState.message || 'Leave both fields empty for a day off.'}
                    </span>
                    <button type="submit" disabled={saveState.status === 'saving'}>
                      {saveState.status === 'saving' ? 'Saving…' : 'Save availability'}
                    </button>
                  </div>
                </form>
              )}
            </section>
          )}

          {activeTab === 'demand' && <DemandManager setErrorPopup={setErrorPopup} />}

          <RosterGenerationPanel
            fetchJson={fetchJson}
            setErrorPopup={setErrorPopup}
            isVisible={activeTab === 'generate-roster'}
            onShowSaved={(weekStart) => { setSavedWeekStart(weekStart); switchTab('saved-rosters') }}
          />
          {activeTab === 'saved-rosters' && <SavedRosterPanel
            key={savedWeekStart || 'upcoming'}
            fetchJson={fetchJson}
            setErrorPopup={setErrorPopup}
            initialWeekStart={savedWeekStart}
          />}
        </section>
      </main>
    </>
  )
}

function TimeSelector({ id, label, value, onChange }) {
  const selectedHour = value ? value.split(':')[0] : ''

  return (
    <div className="time-selector" id={id} aria-label={label}>
      <select
        aria-label={`${label} (24-hour format)`}
        value={selectedHour}
        onChange={(event) =>
          onChange(event.target.value ? `${event.target.value}:00` : '')
        }
      >
        <option value="">HH</option>
        {hours.map((hour) => (
          <option key={hour} value={hour}>
            {hour}
          </option>
        ))}
      </select>
    </div>
  )
}

function App() {
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
  const isAdmin = isAuthenticated && authState.user.isAdmin

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
  }, [])

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
        const defaultEmployee = ownEmployeeId
          ? payload.find((employee) => employee.id === ownEmployeeId)
          : payload.find((employee) => employee.isActive)
        setSelectedEmployeeId(defaultEmployee ? String(defaultEmployee.id) : '')
        setEmployeesState({ status: 'success' })
      })
      .catch((error) => {
        setEmployeesState({ status: 'error', message: error.message })
        setErrorPopup(error.message)
      })
  }, [authState, isAuthenticated, isAdmin, dataRefreshKey])

  useEffect(() => {
    if (!isAuthenticated || !selectedEmployeeId) {
      setSchedule(null)
      setScheduleState({ status: 'idle' })
      return
    }

    setScheduleState({ status: 'loading' })
    setSchedule(null)
    setSaveState({ status: 'idle' })
    setErrorPopup('')

    fetchJson(`/api/employees/${selectedEmployeeId}/schedule?weekOffset=${weekOffset}`)
      .then((payload) => {
        setSchedule(payload)
        setScheduleState({ status: 'success' })
      })
      .catch((error) => {
        setScheduleState({ status: 'error', message: error.message })
        setErrorPopup(error.message)
      })
  }, [isAuthenticated, selectedEmployeeId, weekOffset, dataRefreshKey])

  useEffect(() => {
    if (!isAdmin) {
      setAvailability(null)
      return
    }

    fetchJson(`/api/admin/availability?weekOffset=${weekOffset}`)
      .then(setAvailability)
      .catch((error) => setErrorPopup(error.message))
  }, [isAdmin, weekOffset, dataRefreshKey])

  useEffect(() => {
    const employee = employees.find((item) => String(item.id) === selectedEmployeeId)

    if (employee && !isCreatingEmployee) {
      setEmployeeForm({
        employeeNumber: employee.employeeNumber,
        firstName: employee.firstName,
        lastName: employee.lastName,
        phoneNumber: employee.phoneNumber,
        targetHours: employee.targetHours,
        canWorkAlone: employee.canWorkAlone,
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

  function changeWeek(direction) {
    setWeekOffset((current) =>
      Math.min(maxWeekOffset, Math.max(minWeekOffset, current + direction)),
    )
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
      setAuthState({ status: 'anonymous', user: null, message: error.message })
      setErrorPopup(error.message)
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
    setEmployeeSaveState({ status: 'saving' })

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
      setEmployeeSaveState({ status: 'success', message: 'Employee saved.' })
    } catch (error) {
      setEmployeeSaveState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
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
    if (!selectedEmployeeId || !window.confirm('Remove this employee and their login completely?')) {
      return
    }

    try {
      await fetchJson(`/api/admin/employees/${selectedEmployeeId}`, { method: 'DELETE' })
      setEmployees((current) => current.filter((employee) => String(employee.id) !== selectedEmployeeId))
      setSelectedEmployeeId('')
      setEmployeeForm(emptyEmployeeForm)
      setIsCreatingEmployee(false)
      setEmployeeEditorOpen(false)
      setEmployeeSaveState({ status: 'success', message: 'Employee removed completely.' })
    } catch (error) {
      setEmployeeSaveState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function saveSchedule(event) {
    event.preventDefault()
    setSaveState({ status: 'saving' })
    setErrorPopup('')

    try {
      const payload = await fetchJson(
        `/api/employees/${selectedEmployeeId}/schedule`,
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
      setSaveState({ status: 'success', message: 'Schedule saved.' })
    } catch (error) {
      setSaveState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  if (authState.status === 'loading') {
    return <main className="page-shell centered-page"><p className="message info-message">Checking sign-in…</p></main>
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
        removeEmployee={removeEmployee}
        availability={availability}
        setErrorPopup={setErrorPopup}
        logout={logout}
        onTabChange={() => setDataRefreshKey((current) => current + 1)}
      />
    )
  }

  return (
    <>
      <ErrorPopup message={errorPopup} onClose={() => setErrorPopup('')} />
      <main className="page-shell">
      <section className="app-card">
        <header className="page-header">
          <span className="eyebrow">Roaster Generator</span>
          <h1>Weekly shifts</h1>
          <p className="lead">
            {isAdmin
              ? 'Manage employees and review or edit schedules.'
              : 'Set your start and finish times for the available weeks.'}
          </p>
        </header>

        <div className="app-toolbar">
          <span className="role-badge">{isAdmin ? 'Admin' : 'Employee'}</span>
          <button type="button" className="secondary-button" onClick={logout}>Sign out</button>
        </div>

        {isAdmin ? (
          <div className="employee-picker">
            <label htmlFor="employee">Employee</label>
            <select
              id="employee"
              value={selectedEmployeeId}
              onChange={(event) => {
                setSelectedEmployeeId(event.target.value)
                setWeekOffset(minWeekOffset)
                setIsCreatingEmployee(false)
              }}
              disabled={employeesState.status !== 'success' || employees.length === 0}
            >
              <option value="">
                {employeesState.status === 'loading' ? 'Loading employees…' : 'Select an employee'}
              </option>
              {employees.map((employee) => (
                <option key={employee.id} value={employee.id}>
                  {employee.firstName} {employee.lastName}{employee.isActive ? '' : ' (inactive)'}
                </option>
              ))}
            </select>
          </div>
        ) : (
          <div className="current-employee">
            <span className="eyebrow">Signed in as</span>
            <strong>{authState.user.employeeName || authState.user.username}</strong>
          </div>
        )}

        {employeesState.status === 'error' && (
          <p className="message error-message">{employeesState.message}</p>
        )}

        {employeesState.status === 'success' && employees.length === 0 && (
          <p className="message info-message">No employees are available yet.</p>
        )}

        {scheduleState.status === 'loading' && (
          <p className="message info-message">Loading week…</p>
        )}

        {scheduleState.status === 'error' && (
          <p className="message error-message">{scheduleState.message}</p>
        )}

        {isAdmin && (
          <section className="admin-tools">
            <div className="section-heading">
              <div>
                <span className="eyebrow">Administration</span>
                <h2>{isCreatingEmployee ? 'Add employee' : 'Edit employee'}</h2>
              </div>
              <button
                type="button"
                className="secondary-button"
                onClick={() => {
                  setIsCreatingEmployee((current) => !current)
                  setEmployeeForm(emptyEmployeeForm)
                  setEmployeeSaveState({ status: 'idle' })
                }}
              >
                {isCreatingEmployee ? 'Cancel' : 'Add employee'}
              </button>
            </div>

            {(isCreatingEmployee || selectedEmployeeId) && (
              <form className="employee-form" onSubmit={saveEmployee}>
                <label htmlFor="employee-number">Employee number</label>
                <input
                  id="employee-number"
                  value={employeeForm.employeeNumber}
                  onChange={(event) => updateEmployeeForm('employeeNumber', event.target.value)}
                  required
                />
                <label htmlFor="employee-first-name">First name</label>
                <input
                  id="employee-first-name"
                  value={employeeForm.firstName}
                  onChange={(event) => updateEmployeeForm('firstName', event.target.value)}
                  required
                />
                <label htmlFor="employee-last-name">Last name</label>
                <input
                  id="employee-last-name"
                  value={employeeForm.lastName}
                  onChange={(event) => updateEmployeeForm('lastName', event.target.value)}
                  required
                />
                <label htmlFor="employee-phone">Phone number</label>
                <input
                  id="employee-phone"
                  value={employeeForm.phoneNumber}
                  onChange={(event) => updateEmployeeForm('phoneNumber', event.target.value)}
                />
                <label htmlFor="employee-target-hours">Target hours per week</label>
                <input
                  id="employee-target-hours"
                  type="number"
                  min="3"
                  max="168"
                  step="1"
                  value={employeeForm.targetHours}
                  onChange={(event) => updateEmployeeForm('targetHours', Number(event.target.value))}
                  required
                />
                <div className="employee-form-actions">
                  <button type="submit" disabled={employeeSaveState.status === 'saving'}>
                    {employeeSaveState.status === 'saving' ? 'Saving…' : 'Save employee'}
                  </button>
                  {!isCreatingEmployee && (
                    <button
                      type="button"
                      className="secondary-button"
                      onClick={() => changeEmployeeStatus(
                        employees.find((employee) => String(employee.id) === selectedEmployeeId)?.isActive
                          ? 'deactivate'
                          : 'reactivate',
                      )}
                    >
                      {employees.find((employee) => String(employee.id) === selectedEmployeeId)?.isActive
                        ? 'Deactivate'
                        : 'Reactivate'}
                    </button>
                  )}
                </div>
                {employeeSaveState.message && (
                  <p className={`save-message ${employeeSaveState.status}`}>
                    {employeeSaveState.message}
                  </p>
                )}
              </form>
            )}
          </section>
        )}

        {isAdmin && <DemandManager setErrorPopup={setErrorPopup} />}

        {isAdmin && availability && (
          <section className="availability-section">
            <div className="section-heading">
              <div>
                <span className="eyebrow">Admin overview</span>
                <h2>All employee availability</h2>
              </div>
              <span className="week-range">
                {dateFormatter.format(parseDate(availability.weekStart))} –{' '}
                {dateFormatter.format(parseDate(availability.weekEnd))}
              </span>
            </div>
            <div className="availability-table" role="table">
              <div className="availability-row availability-header" role="row">
                <strong>Employee</strong>
                {availability.employees[0]?.days.map((day) => (
                  <span key={day.date}>{day.dayOfWeek.slice(0, 3)}</span>
                ))}
              </div>
              {availability.employees.map((employeeSchedule) => (
                <div className="availability-row" role="row" key={employeeSchedule.employeeId}>
                  <strong>{employeeSchedule.employeeName}</strong>
                  {employeeSchedule.days.map((day) => (
                    <span key={day.date}>
                      {day.startTime && day.finishTime
                        ? `${day.startTime}–${day.finishTime}`
                        : 'Off'}
                    </span>
                  ))}
                </div>
              ))}
              {availability.employees.length === 0 && (
                <p className="message info-message">No active employees.</p>
              )}
            </div>
          </section>
        )}

        {schedule && (
          <form className="schedule-form" onSubmit={saveSchedule}>
            <div className="schedule-heading">
              <div>
                <span className="eyebrow">Schedule for</span>
                <h2>{schedule.employeeName}</h2>
              </div>
              <div className="week-navigation" aria-label="Week navigation">
                <button
                  type="button"
                  className="week-arrow"
                  aria-label="Previous week"
                  onClick={() => changeWeek(-1)}
                  disabled={weekOffset === minWeekOffset || scheduleState.status === 'loading'}
                >
                  ←
                </button>
                <div className="week-range">
                  <strong>{weekLabels[weekOffset]}</strong>
                  <span>
                    {dateFormatter.format(parseDate(schedule.weekStart))} –{' '}
                    {dateFormatter.format(parseDate(schedule.weekEnd))}
                  </span>
                </div>
                <button
                  type="button"
                  className="week-arrow"
                  aria-label="Next week"
                  onClick={() => changeWeek(1)}
                  disabled={weekOffset === maxWeekOffset || scheduleState.status === 'loading'}
                >
                  →
                </button>
              </div>
            </div>

            <div className="schedule-table" role="table" aria-label="Selected week schedule">
                <div className="schedule-row schedule-header" role="row">
                  <span role="columnheader">Day</span>
                  <span role="columnheader">Start time</span>
                  <span role="columnheader">Finish time</span>
                  <span role="columnheader">Reset</span>
                </div>

              {schedule.days.map((day) => (
                <div className="schedule-row" role="row" key={day.date}>
                  <div className="day-cell" role="cell">
                    <strong>{day.dayOfWeek}</strong>
                    <span>{dateFormatter.format(parseDate(day.date))}</span>
                    {getShiftDuration(day.startTime, day.finishTime) && (
                      <span className="shift-duration">
                        {getShiftDuration(day.startTime, day.finishTime)}
                      </span>
                    )}
                  </div>
                  <div role="cell">
                    <label className="visually-hidden" htmlFor={`${day.date}-start`}>
                      {day.dayOfWeek} start time
                    </label>
                    <TimeSelector
                      id={`${day.date}-start`}
                      label={`${day.dayOfWeek} start time`}
                      value={day.startTime || ''}
                      onChange={(value) => updateDay(day.date, 'startTime', value)}
                    />
                  </div>
                    <div role="cell">
                      <label className="visually-hidden" htmlFor={`${day.date}-finish`}>
                        {day.dayOfWeek} finish time
                    </label>
                    <TimeSelector
                      id={`${day.date}-finish`}
                      label={`${day.dayOfWeek} finish time`}
                      value={day.finishTime || ''}
                        onChange={(value) => updateDay(day.date, 'finishTime', value)}
                      />
                    </div>
                    <div role="cell">
                      <button
                        type="button"
                        className="reset-button"
                        onClick={() => resetDay(day.date)}
                        disabled={!day.startTime && !day.finishTime}
                      >
                        Reset
                      </button>
                    </div>
                  </div>
                ))}
            </div>

            <div className="form-footer">
              <span className={`save-message ${saveState.status}`} aria-live="polite">
                {saveState.message || 'Leave both fields empty for a day off.'}
              </span>
              <button type="submit" disabled={saveState.status === 'saving'}>
                {saveState.status === 'saving' ? 'Saving…' : 'Save times'}
              </button>
            </div>
          </form>
        )}
      </section>
      </main>
    </>
  )
}

export default App
