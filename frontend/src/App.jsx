import { useEffect, useRef, useState } from 'react'
import { RosterGenerationPanel, SavedRosterPanel } from './RosterAdmin'
import { calculateDemandLabour, recalculateDemandPlan, recalculateDemandValue } from './demandPlanning'
import { getAvailabilityEmployeeId } from './availabilityAccess'
import {
  compareEmployees,
  employeeHasRole,
  employeeRoleGroups,
  getEmployeeRoleGroup,
  getRosterRoleGroup,
  rosterRoleGroups,
} from './employeeGroups'

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
  payrollNumber: '',
  hourlyRate: 14.5,
  roles: ['Driver'],
  targetHours: 20,
  insideTargetHours: 20,
  driverType: 'Car',
}

const employeeFieldLabels = {
  employeeNumber: 'Employee number',
  firstName: 'First name',
  lastName: 'Last name',
  phoneNumber: 'Phone number',
  payrollNumber: 'Payroll number',
  hourlyRate: 'Hourly pay rate',
  roles: 'Roles',
  targetHours: 'Target hours',
  insideTargetHours: 'Inside target hours',
  driverType: 'Driver type',
  identity: 'Employee login',
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
    const rawFieldErrors = payload?.errors && typeof payload.errors === 'object'
      ? payload.errors
      : {}
    const fieldErrors = Object.fromEntries(
      Object.entries(rawFieldErrors).map(([field, messages]) => [
        field.length > 0 ? `${field[0].toLowerCase()}${field.slice(1)}` : field,
        (Array.isArray(messages) ? messages : [messages])
          .filter((message) => message !== null && message !== undefined && String(message).trim())
          .map((message) => String(message)),
      ]),
    )
    const validationMessages = Object.values(fieldErrors).flat()
    const messages = [payload?.message, payload?.detail, payload?.title, ...validationMessages]
      .filter(Boolean)
      .filter((message, index, allMessages) => allMessages.indexOf(message) === index)

    if (response.status === 401 &&
        requestPath !== '/api/auth/login' &&
        requestPath !== '/api/auth/me' &&
        requestPath !== '/api/auth/logout') {
      window.dispatchEvent(new CustomEvent('auth-expired', {
        detail: { showMessage: requestPath !== '/api/auth/me' },
      }))
    }

    const message = response.status === 401 && requestPath !== '/api/auth/login'
      ? 'Your session has expired. Please sign in again.'
      : response.status === 403
        ? messages.join('\n') || 'You do not have permission to perform this action.'
        : messages.join('\n') || body || 'The API request failed.'
    const error = new Error(message)
    error.status = response.status
    error.fieldErrors = fieldErrors
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

function getEmployeeFieldErrors(saveState, field) {
  return saveState?.status === 'error' ? saveState.fieldErrors?.[field] || [] : []
}

function EmployeeFieldError({ field, messages }) {
  if (!messages?.length) {
    return null
  }

  return (
    <p id={`admin-employee-${field}-error`} className="employee-field-error">
      {messages.join(' ')}
    </p>
  )
}

function getEmployeeErrorSummary(error) {
  const fieldMessages = new Set(Object.values(error?.fieldErrors || {}).flat())
  const summary = String(error?.message || '')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line && !fieldMessages.has(line))
  return summary.join('\n') || 'The employee could not be saved.'
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

const holidayDateFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'short',
})

function formatHolidayDate(value) {
  return value ? holidayDateFormatter.format(new Date(value)) : '—'
}

function HolidayPanel({ setErrorPopup }) {
  const [holidayState, setHolidayState] = useState({
    status: 'loading',
    requested: null,
    used: [],
  })
  const [hours, setHours] = useState('')
  const [saveState, setSaveState] = useState({ status: 'idle', message: '' })

  async function loadHolidays() {
    try {
      const payload = await fetchJson('/api/holidays')
      setHolidayState({ status: 'success', requested: payload.requested, used: payload.used || [] })
      setHours(payload.requested ? String(payload.requested.hours) : '')
    } catch (error) {
      setHolidayState((current) => ({ ...current, status: 'error' }))
      setErrorPopup(error.message)
    }
  }

  useEffect(() => {
    void loadHolidays()
  }, [])

  async function saveHoliday(event) {
    event.preventDefault()
    setSaveState({ status: 'saving', message: '' })

    const endpoint = holidayState.requested
      ? `/api/holidays/${holidayState.requested.id}`
      : '/api/holidays'

    try {
      const saved = await fetchJson(endpoint, {
        method: holidayState.requested ? 'PUT' : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ hours: Number(hours) }),
      })
      setHolidayState((current) => ({ ...current, status: 'success', requested: saved }))
      setHours(String(saved.hours))
      setSaveState({ status: 'success', message: holidayState.requested ? 'Holiday request updated.' : 'Holiday request submitted.' })
    } catch (error) {
      setSaveState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function removeHoliday() {
    if (!holidayState.requested || !window.confirm('Remove this holiday request?')) {
      return
    }

    try {
      await fetchJson(`/api/holidays/${holidayState.requested.id}`, { method: 'DELETE' })
      setHolidayState((current) => ({ ...current, requested: null }))
      setHours('')
      setSaveState({ status: 'success', message: 'Holiday request removed.' })
    } catch (error) {
      setSaveState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  return (
    <section className="holiday-panel role-details">
      <div className="section-heading">
        <div>
          <span className="eyebrow">Holiday</span>
          <h2>Holiday hours</h2>
        </div>
        <span className="holiday-limit">Maximum 80 hours</span>
      </div>
      <p className="holiday-help">
        Submit one active holiday request. You can edit or remove it until an administrator approves it.
        Approved requests move to your used holiday history.
      </p>

      {holidayState.status === 'loading' && (
        <p className="message info-message">Loading holiday hours…</p>
      )}

      {holidayState.status === 'error' && (
        <p className="message error-message">Unable to load your holiday hours.</p>
      )}

      {holidayState.status !== 'loading' && (
        <form className="holiday-form" onSubmit={saveHoliday}>
          <label htmlFor="holiday-hours">Holiday hours requested</label>
          <div className="holiday-form-row">
            <input
              id="holiday-hours"
              type="number"
              min="1"
              max="80"
              step="1"
              value={hours}
              onChange={(event) => setHours(event.target.value)}
              required
            />
            <button type="submit" disabled={saveState.status === 'saving'}>
              {saveState.status === 'saving'
                ? 'Saving…'
                : holidayState.requested
                  ? 'Save changes'
                  : 'Submit request'}
            </button>
            {holidayState.requested && (
              <button type="button" className="danger-button" onClick={removeHoliday}>
                Remove
              </button>
            )}
          </div>
          {holidayState.requested && (
            <p className="holiday-request-meta">
              Status: <strong>Requested</strong> · Last updated {formatHolidayDate(holidayState.requested.updatedAtUtc)}
            </p>
          )}
          {saveState.message && (
            <p className={`save-message ${saveState.status}`}>{saveState.message}</p>
          )}
        </form>
      )}

      <section className="holiday-history">
        <div className="section-heading">
          <div>
            <span className="eyebrow">History</span>
            <h3>Used holiday hours</h3>
          </div>
          <strong className="holiday-total">{holidayState.used.reduce((total, item) => total + item.hours, 0)} hours</strong>
        </div>
        {holidayState.used.length === 0 ? (
          <p className="message info-message">No used holiday hours yet.</p>
        ) : (
          <HolidayTable holidays={holidayState.used} />
        )}
      </section>
    </section>
  )
}

function HolidayTable({ holidays, showEmployee = false, showApprove = false, onApprove }) {
  return (
    <div className="holiday-table-wrapper">
      <table className="holiday-table">
        <thead>
          <tr>
            {showEmployee && <th>Employee</th>}
            <th>Hours</th>
            <th>Status</th>
            <th>Requested</th>
            <th>Used</th>
            {showApprove && <th>Action</th>}
          </tr>
        </thead>
        <tbody>
          {holidays.map((holiday) => (
            <tr key={holiday.id}>
              {showEmployee && (
                <td>
                  <strong>{holiday.employeeName}</strong>
                  <small>#{holiday.employeeNumber}</small>
                </td>
              )}
              <td>{holiday.hours}</td>
              <td><span className={`holiday-status ${holiday.status.toLowerCase()}`}>{holiday.status}</span></td>
              <td>{formatHolidayDate(holiday.createdAtUtc)}</td>
              <td>{formatHolidayDate(holiday.usedAtUtc)}</td>
              {showApprove && (
                <td>
                  <button type="button" className="secondary-button" onClick={() => onApprove(holiday.id)}>
                    Approve
                  </button>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function AdminHolidayPanel({ setErrorPopup, canExport = true }) {
  const [holidayState, setHolidayState] = useState({ status: 'loading', requested: [], used: [] })
  const [actionState, setActionState] = useState({ status: 'idle', message: '' })

  async function loadHolidays() {
    try {
      const payload = await fetchJson('/api/admin/holidays')
      setHolidayState({ status: 'success', requested: payload.requested || [], used: payload.used || [] })
    } catch (error) {
      setHolidayState((current) => ({ ...current, status: 'error' }))
      setErrorPopup(error.message)
    }
  }

  useEffect(() => {
    void loadHolidays()
  }, [])

  async function approveHoliday(holidayId) {
    setActionState({ status: 'saving', message: '' })
    try {
      await fetchJson(`/api/admin/holidays/${holidayId}/approve`, { method: 'POST' })
      setActionState({ status: 'success', message: 'Holiday request approved and marked as used.' })
      await loadHolidays()
    } catch (error) {
      setActionState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function approveAll() {
    if (holidayState.requested.length === 0 || !window.confirm('Approve all requested holiday hours?')) {
      return
    }

    setActionState({ status: 'saving', message: '' })
    try {
      const result = await fetchJson('/api/admin/holidays/approve-all', { method: 'POST' })
      setActionState({ status: 'success', message: `${result.approvedCount} request${result.approvedCount === 1 ? '' : 's'} approved and marked as used.` })
      await loadHolidays()
    } catch (error) {
      setActionState({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  const requestedHours = holidayState.requested.reduce((total, holiday) => total + holiday.hours, 0)
  const usedHours = holidayState.used.reduce((total, holiday) => total + holiday.hours, 0)

  return (
    <section className="admin-tools holiday-admin-panel">
      <div className="section-heading">
        <div>
          <span className="eyebrow">Administration</span>
          <h2>Holiday requests</h2>
        </div>
        <div className="holiday-admin-actions">
          {canExport && (
            <a className="secondary-button" href="/api/admin/holidays/export" download="holiday-requests.csv">
              Download CSV
            </a>
          )}
          <button
            type="button"
            onClick={approveAll}
            disabled={holidayState.status !== 'success' || holidayState.requested.length === 0 || actionState.status === 'saving'}
          >
            {actionState.status === 'saving' ? 'Approving…' : 'Approve all'}
          </button>
        </div>
      </div>

      {holidayState.status === 'loading' && <p className="message info-message">Loading holiday requests…</p>}
      {holidayState.status === 'error' && <p className="message error-message">Unable to load holiday requests.</p>}

      <div className="holiday-summary">
        <div><span>Requested</span><strong>{requestedHours} hours</strong><small>{holidayState.requested.length} request{holidayState.requested.length === 1 ? '' : 's'}</small></div>
        <div><span>Used</span><strong>{usedHours} hours</strong><small>{holidayState.used.length} record{holidayState.used.length === 1 ? '' : 's'}</small></div>
      </div>

      {actionState.message && <p className={`save-message ${actionState.status}`}>{actionState.message}</p>}

      <section className="holiday-history">
        <div className="section-heading">
          <div>
            <span className="eyebrow">Pending</span>
            <h3>Requested holiday hours</h3>
          </div>
        </div>
        {holidayState.requested.length === 0 ? (
          <p className="message info-message">No holiday requests are waiting for approval.</p>
        ) : (
          <HolidayTable holidays={holidayState.requested} showEmployee showApprove onApprove={approveHoliday} />
        )}
      </section>

      <section className="holiday-history">
        <div className="section-heading">
          <div>
            <span className="eyebrow">Completed</span>
            <h3>Used holiday hours</h3>
          </div>
        </div>
        {holidayState.used.length === 0 ? (
          <p className="message info-message">No holiday hours have been approved yet.</p>
        ) : (
          <HolidayTable holidays={holidayState.used} showEmployee />
        )}
      </section>
    </section>
  )
}

function formatDateOnly(value) {
  return value ? dateFormatter.format(parseDate(value)) : '—'
}

function SickLeaveTable({ requests, showEmployee = false, management = false, onReview, busyId }) {
  return (
    <div className="holiday-table-wrapper">
      <table className="holiday-table sick-leave-table">
        <thead><tr>{showEmployee && <th>Employee</th>}<th>Start</th><th>Finish</th><th>Status</th><th>Sick note</th><th>Reviewed</th>{(management || !showEmployee) && <th>Action</th>}</tr></thead>
        <tbody>{requests.map((request) => (
          <tr key={request.id}>
            {showEmployee && <td><strong>{request.employeeName}</strong><small>#{request.employeeNumber}</small></td>}
            <td>{formatDateOnly(request.startDate)}</td><td>{formatDateOnly(request.finishDate)}</td>
            <td><span className={`holiday-status ${request.status.toLowerCase()}`}>{request.status}</span></td>
            <td>{request.hasAttachment ? <a className="table-link" href={`${management ? '/api/admin' : '/api'}/sick-leave/${request.id}/attachment`} target="_blank" rel="noreferrer">View note</a> : <span className="deleted-note">Deleted after review</span>}</td>
            <td>{request.reviewedAtUtc ? formatHolidayDate(request.reviewedAtUtc) : '—'}{request.reviewedByName && <small>by {request.reviewedByName}</small>}{request.rejectionReason && <small>{request.rejectionReason}</small>}</td>
            {management && <td className="sick-leave-actions"><button type="button" onClick={() => onReview(request.id, 'approve')} disabled={busyId === request.id}>Approve</button><button type="button" className="danger-button" onClick={() => onReview(request.id, 'reject')} disabled={busyId === request.id}>Reject</button></td>}
            {!management && !showEmployee && <td>{request.status === 'Requested' && <button type="button" className="danger-button" onClick={() => onReview(request.id)} disabled={busyId === request.id}>Remove</button>}</td>}
          </tr>
        ))}</tbody>
      </table>
    </div>
  )
}

function SickLeavePanel({ setErrorPopup }) {
  const [state, setState] = useState({ status: 'loading', requested: [], reviewed: [] })
  const [form, setForm] = useState({ startDate: '', finishDate: '', file: null })
  const [action, setAction] = useState({ status: 'idle', message: '', busyId: null })
  const fileInputRef = useRef(null)

  async function load() {
    try {
      const payload = await fetchJson('/api/sick-leave')
      setState({ status: 'success', requested: payload.requested || [], reviewed: payload.reviewed || [] })
    } catch (error) {
      setState((current) => ({ ...current, status: 'error' }))
      setErrorPopup(error.message)
    }
  }
  useEffect(() => { void load() }, [])

  async function submit(event) {
    event.preventDefault()
    const body = new FormData()
    body.append('startDate', form.startDate); body.append('finishDate', form.finishDate)
    if (form.file) body.append('file', form.file)
    setAction({ status: 'saving', message: '', busyId: null })
    try {
      await fetchJson('/api/sick-leave', { method: 'POST', body })
      setForm({ startDate: '', finishDate: '', file: null })
      if (fileInputRef.current) fileInputRef.current.value = ''
      setAction({ status: 'success', message: 'Sick leave request submitted.', busyId: null })
      await load()
    } catch (error) {
      setAction({ status: 'error', message: error.message, busyId: null }); setErrorPopup(error.message)
    }
  }

  async function remove(requestId) {
    if (!window.confirm('Remove this sick leave request and its sick note?')) return
    setAction({ status: 'saving', message: '', busyId: requestId })
    try {
      await fetchJson(`/api/sick-leave/${requestId}`, { method: 'DELETE' })
      setAction({ status: 'success', message: 'Sick leave request removed.', busyId: null }); await load()
    } catch (error) {
      setAction({ status: 'error', message: error.message, busyId: null }); setErrorPopup(error.message)
    }
  }

  return (
    <section className="holiday-panel role-details">
      <div className="section-heading"><div><span className="eyebrow">Sick leave</span><h2>Request sick leave</h2></div><span className="holiday-limit">Sick note required</span></div>
      <p className="holiday-help">Enter the inclusive start and finish dates and attach a PDF or image. The note is permanently deleted as soon as a manager approves or rejects the request.</p>
      <form className="sick-leave-form" onSubmit={submit}>
        <div className="sick-leave-fields">
          <div className="sick-leave-field">
            <label htmlFor="sick-start">Start date</label>
            <input id="sick-start" type="date" required value={form.startDate} onChange={(event) => setForm((current) => ({ ...current, startDate: event.target.value }))} />
          </div>
          <div className="sick-leave-field">
            <label htmlFor="sick-finish">Finish date</label>
            <input id="sick-finish" type="date" min={form.startDate || undefined} required value={form.finishDate} onChange={(event) => setForm((current) => ({ ...current, finishDate: event.target.value }))} />
          </div>
          <div className="sick-leave-field sick-note-field">
            <span className="sick-note-label">Sick note</span>
            <input
              ref={fileInputRef}
              className="visually-hidden"
              id="sick-note"
              type="file"
              accept=".pdf,.png,.jpg,.jpeg,.gif,.webp,.bmp"
              required
              onChange={(event) => setForm((current) => ({ ...current, file: event.target.files?.[0] || null }))}
            />
            <label className={`sick-note-picker ${form.file ? 'has-file' : ''}`} htmlFor="sick-note">
              <span className="sick-note-button">Choose file</span>
              <span className="sick-note-name">{form.file?.name || 'No file selected'}</span>
            </label>
          </div>
        </div>
        <div className="sick-leave-form-footer">
          <small>PDF, PNG, JPEG, GIF, WebP or BMP · maximum 10 MB</small>
          <button type="submit" disabled={action.status === 'saving'}>{action.status === 'saving' ? 'Submitting…' : 'Submit request'}</button>
        </div>
      </form>
      {action.message && <p className={`save-message ${action.status}`}>{action.message}</p>}
      <section className="holiday-history"><div className="section-heading"><div><span className="eyebrow">Pending</span><h3>Waiting for review</h3></div></div>{state.status === 'loading' ? <p className="message info-message">Loading sick leave…</p> : state.requested.length === 0 ? <p className="message info-message">No sick leave requests are waiting for review.</p> : <SickLeaveTable requests={state.requested} onReview={remove} busyId={action.busyId} />}</section>
      <section className="holiday-history"><div className="section-heading"><div><span className="eyebrow">History</span><h3>Reviewed sick leave</h3></div></div>{state.reviewed.length === 0 ? <p className="message info-message">No reviewed sick leave yet.</p> : <SickLeaveTable requests={state.reviewed} />}</section>
    </section>
  )
}

function AdminSickLeavePanel({ setErrorPopup, includeOwnRequest = false }) {
  const [state, setState] = useState({ status: 'loading', requested: [], reviewed: [] })
  const [action, setAction] = useState({ status: 'idle', message: '', busyId: null })
  async function load() {
    try { const payload = await fetchJson('/api/admin/sick-leave'); setState({ status: 'success', requested: payload.requested || [], reviewed: payload.reviewed || [] }) }
    catch (error) { setState((current) => ({ ...current, status: 'error' })); setErrorPopup(error.message) }
  }
  useEffect(() => { void load() }, [])
  async function review(requestId, decision) {
    let reason = null
    if (decision === 'reject') { reason = window.prompt('Optional reason for rejection:', ''); if (reason === null) return }
    else if (!window.confirm('Approve this sick leave request? The attached sick note will be permanently deleted.')) return
    setAction({ status: 'saving', message: '', busyId: requestId })
    try {
      await fetchJson(`/api/admin/sick-leave/${requestId}/${decision}`, { method: 'POST', headers: decision === 'reject' ? { 'Content-Type': 'application/json' } : undefined, body: decision === 'reject' ? JSON.stringify({ reason }) : undefined })
      setAction({ status: 'success', message: `Sick leave ${decision === 'approve' ? 'approved' : 'rejected'}; the sick note was deleted.`, busyId: null }); await load()
    } catch (error) { setAction({ status: 'error', message: error.message, busyId: null }); setErrorPopup(error.message) }
  }
  return (
    <>
      <section className="admin-tools holiday-admin-panel">
        <div className="section-heading"><div><span className="eyebrow">Administration</span><h2>Sick leave requests</h2></div><span className="holiday-limit">{state.requested.length} pending</span></div>
        <p className="holiday-help">Review the note before deciding. Approval or rejection permanently deletes the attachment while retaining decision history.</p>
        {action.message && <p className={`save-message ${action.status}`}>{action.message}</p>}
        <section className="holiday-history"><div className="section-heading"><div><span className="eyebrow">Pending</span><h3>Waiting for review</h3></div></div>{state.status === 'loading' ? <p className="message info-message">Loading sick leave requests…</p> : state.requested.length === 0 ? <p className="message info-message">No sick leave requests are waiting for review.</p> : <SickLeaveTable requests={state.requested} showEmployee management onReview={review} busyId={action.busyId} />}</section>
        <section className="holiday-history"><div className="section-heading"><div><span className="eyebrow">History</span><h3>Reviewed requests</h3></div></div>{state.reviewed.length === 0 ? <p className="message info-message">No reviewed sick leave yet.</p> : <SickLeaveTable requests={state.reviewed} showEmployee />}</section>
      </section>
      {includeOwnRequest && <SickLeavePanel setErrorPopup={setErrorPopup} />}
    </>
  )
}

function DemandManager({ setErrorPopup, employees = [], canEdit = true }) {
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

  function updateDemandValue(hour, position, field, rawValue) {
    const numericValue = rawValue === '' ? null : Number(rawValue)
    setPlan((current) => ({
      ...current,
      rows: current.rows.map((row) => {
        if (row.hour !== hour) {
          return row
        }

        const existing = row.values.find((item) => item.position === position)
        let nextValue = { ...existing, position, [field]: numericValue }
        if (field === 'deliveries') nextValue = recalculateDemandValue(nextValue, current, ['demand'])

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

  function updateTargetSales(position, rawValue) {
    const targetSales = rawValue === '' ? 0 : Number(rawValue)
    setPlan((current) => ({
      ...current,
      columns: current.columns.map((column) => (
        column.position === position ? { ...column, targetSales } : column
      )),
    }))
    setStatus({ status: 'idle', message: '' })
  }

  function updatePlanningSetting(field, rawValue) {
    const value = rawValue === '' ? '' : Number(rawValue)
    setPlan((current) => {
      const updated = { ...current, [field]: value }
      return field === 'deliveriesPerDriverHour'
        ? recalculateDemandPlan(updated) : updated
    })
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

  async function savePlan(event, recalculateDemand = false) {
    event?.preventDefault()
    setStatus({ status: 'saving', message: '' })

    try {
      const payload = await fetchJson(`/api/admin/demand/${selectedPlanId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name,
          weekStart,
          deliveriesPerDriverHour: Number(plan.deliveriesPerDriverHour ?? 2.7),
          recalculateDemand,
          columns: plan.columns.map((column) => ({
            position: column.position,
            label: column.label,
            targetSales: Number(column.targetSales ?? 0),
          })),
          rows: plan.rows.map((row) => ({
            hour: row.hour,
            values: row.values.map((value) => ({
              position: value.position,
              deliveries: value.deliveries ?? null,
              demand: value.demand,
            })),
          })),
        }),
      })
      setPlan(payload)
      setStatus({ status: 'success', message: recalculateDemand ? 'Driver demand recalculated and saved.' : 'Demand changes saved.' })
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
  const demandSummary = calculateDemandLabour(plan, employees)
  const dailyStaffingRows = demandSummary.days
  const selectedDaySummary = dailyStaffingRows.find((day) => day.position === selectedDemandDayPosition)
  const selectedDayHourlyRows = selectedDaySummary?.hourly || []
  const formatMoney = (value) => value == null ? '—' : `€${Number(value).toFixed(2)}`
  const formatPercentage = (value) => value == null ? '—' : `${Number(value).toFixed(2)}%`
  const formatMetric = (value, digits = 2) => value == null ? '—' : Number(value).toFixed(digits)
  const formatSignedMoney = (value) => {
    if (value == null) return '—'
    const amount = Number(value)
    return `${amount > 0 ? '+' : amount < 0 ? '−' : ''}€${Math.abs(amount).toFixed(2)}`
  }
  const formatSignedMetric = (value) => {
    if (value == null) return '—'
    const amount = Number(value)
    return `${amount > 0 ? '+' : amount < 0 ? '−' : ''}${Math.abs(amount).toFixed(2)}`
  }
  const staffingStatus = (row) => {
    if (row.staffingStatus === 'under') return { label: `Under by ${Math.abs(row.driverDifference)}`, tone: 'under' }
    if (row.staffingStatus === 'above') return { label: `Above by ${row.driverDifference}`, tone: 'above' }
    if (row.staffingStatus === 'matched') return { label: 'Whole need met', tone: 'matched' }
    return { label: 'Missing data', tone: 'unknown' }
  }
  const demandFields = [['deliveries', 'Deliveries'], ['demand', 'Drivers']]
  function demandCell(row, column, field, label) {
    const value = row.values.find((item) => item.position === column.position) || {}
    const missing = value.isOpen && value[field] == null
    return <td key={`${row.hour}-${column.position}-${field}`} className={missing ? 'demand-missing-value' : undefined}>
      <input type="number" min="0" max={field === 'demand' ? 100000000 : 1000000}
        step={field === 'demand' ? '1' : '0.01'} value={value[field] ?? ''}
        onChange={(event) => updateDemandValue(row.hour, column.position, field, event.target.value)}
        readOnly={!canEdit || value.isOpen === false} aria-readonly={!canEdit || value.isOpen === false}
        placeholder={value.isOpen === false ? 'Closed' : '—'}
        aria-label={`${column.label} ${String(row.hour).padStart(2, '0')}:00 ${label}`}
        title={missing ? `${label} missing for this open hour` : undefined} />
    </td>
  }

  return (
    <section className="admin-tools demand-tools">
      <div className="section-heading">
        <div>
          <span className="eyebrow">Administration</span>
          <h2>Demand input</h2>
        </div>
      </div>

      {!canEdit && (
        <p className="message info-message" role="status">
          Read-only view for managers. Only administrators can create or change demand plans.
        </p>
      )}

      <p className="demand-help">
        Import the weekly Excel/CSV/table template. Driver demand is deliveries ÷ deliveries per driver-hour,
        rounded up. Delivery counts and driver demand can be adjusted below.
        Hours run from 06–23 followed by next-day 00–05. Imports preserve saved sales targets and productivity settings.
      </p>

      {canEdit && (
        <>
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
        </>
      )}

      {status.message && (
        <p className={`save-message ${status.status}`}>{status.message}</p>
      )}

      {plan && (
        <form onSubmit={canEdit ? savePlan : (event) => event.preventDefault()}>
          <div className="demand-labour-settings">
            <div>
              <span className="eyebrow">Planned labour</span>
              <h3>Demand, sales targets and estimated labour</h3>
              <p>
                Productivity changes recalculate the demand preview immediately;
                save to apply it to generation. Raising productivity needs fewer staff, lowering it needs more.
                Recalculation replaces manual staff counts; save productivity changes before applying staffing overrides.
                Planned labour uses required driver-hours and the target-hour-weighted average pay rate of active drivers.
                “Actual demand labour” below means the cost of the currently entered Drivers values. Saved-roster labour
                remains separate and is calculated from the employees actually assigned.
              </p>
            </div>
            <div className="demand-productivity-grid">
              {[
                ['deliveriesPerDriverHour', 'Deliveries per hour per driver', 2.7, 0.01, 1000],
              ].map(([field, label, defaultValue, min, max]) => <label key={field}>{label}
                <input type="number" min={min} max={max} step="0.01" required value={plan[field] ?? defaultValue}
                  onChange={(event) => updatePlanningSetting(field, event.target.value)} readOnly={!canEdit} aria-readonly={!canEdit} />
              </label>)}
            </div>
          </div>

          <div className="demand-overview-groups">
            <section className="demand-overview-group demand-overview-volume">
              <div className="demand-overview-group-heading">
                <span>1</span>
                <div><h4>Work to deliver</h4><p>The expected orders and sales for the week.</p></div>
              </div>
              <dl>
                <div><dt>Deliveries</dt><dd>{formatMetric(demandSummary.deliveries, 0)}</dd></div>
                <div><dt>Target sales</dt><dd>{formatMoney(demandSummary.targetSales)}</dd></div>
                <div><dt>Deliveries per entered hour</dt><dd>{formatMetric(demandSummary.deliveriesPerEnteredDriverHour)}</dd></div>
              </dl>
            </section>
            <section className="demand-overview-group demand-overview-staffing">
              <div className="demand-overview-group-heading">
                <span>2</span>
                <div><h4>Drivers required</h4><p>Compare the saved demand with the productivity calculation.</p></div>
              </div>
              <dl>
                <div><dt>Entered demand</dt><dd>{formatMetric(demandSummary.driverHours, 0)} hours</dd></div>
                <div><dt>Ideal workload</dt><dd>{formatMetric(demandSummary.idealDriverHours)} hours</dd></div>
                <div><dt>Whole-driver need</dt><dd>{formatMetric(demandSummary.wholeDriverHours, 0)} hours</dd></div>
                <div><dt>Entered minus ideal</dt><dd>{formatSignedMetric(demandSummary.driverHourDifference)} hours</dd></div>
                <div><dt>Capacity used</dt><dd>{formatPercentage(demandSummary.capacityUtilization)}</dd></div>
              </dl>
            </section>
            <section className="demand-overview-group demand-overview-cost">
              <div className="demand-overview-group-heading">
                <span>3</span>
                <div><h4>Expected labour cost</h4><p>What the entered demand costs versus the fractional ideal.</p></div>
              </div>
              <dl>
                <div><dt>Entered-demand labour</dt><dd>{formatMoney(demandSummary.labourCost)}</dd></div>
                <div><dt>Ideal labour</dt><dd>{formatMoney(demandSummary.idealLabourCost)}</dd></div>
                <div><dt>Cost difference</dt><dd className={Number(demandSummary.labourCostDifference) > 0 ? 'metric-warning' : ''}>{formatSignedMoney(demandSummary.labourCostDifference)}</dd></div>
                <div><dt>Labour / sales</dt><dd>{formatPercentage(demandSummary.labourPercentage)}</dd></div>
                <div><dt>Cost per delivery</dt><dd>{formatMoney(demandSummary.labourCostPerDelivery)}</dd></div>
              </dl>
            </section>
          </div>

          <div className="demand-assumptions" aria-label="Labour calculation assumptions">
            <span><strong>{formatMetric(demandSummary.productivity)}</strong> deliveries per driver-hour</span>
            <span><strong>{formatMoney(demandSummary.averageHourlyRate)}</strong> average hourly pay</span>
            <span><strong>{demandSummary.eligibleDriverCount}</strong> eligible drivers</span>
            <span><strong>{formatMetric(demandSummary.totalTargetHours)}</strong> combined target hours</span>
            <span>Demand uses <strong>{formatPercentage(demandSummary.demandToTargetHoursPercentage)}</strong> of target hours</span>
          </div>

          <details className="demand-metric-guide">
            <summary>How these numbers are calculated</summary>
            <div>
              <p><strong>Ideal drivers</strong> = deliveries ÷ configured deliveries per driver-hour. This can be a decimal.</p>
              <p><strong>Whole-driver need</strong> rounds ideal drivers up because a fraction of a person cannot be rostered.</p>
              <p><strong>Entered demand</strong> is the Drivers value saved in the demand table. It may include minimum shop cover or manual changes.</p>
              <p><strong>Entered-demand labour</strong> uses entered demand and average driver pay. It is not the named-employee cost from a saved roster.</p>
              <p>Sunday premium is applied to calendar-Sunday hours. A dash means required data is unavailable.</p>
            </div>
          </details>

          <div className="demand-labour-table-wrapper">
            <table className="demand-labour-table demand-daily-table">
              <thead>
                <tr>
                  <th>Day</th>
                  <th>Target sales</th>
                  <th>Deliveries</th>
                  <th>Driver hours</th>
                  <th>Delivery capacity</th>
                  <th>Demand labour</th>
                  <th>Hourly checks</th>
                </tr>
              </thead>
              <tbody>
                {dailyStaffingRows.map((day) => (
                  <tr key={day.position}>
                    <th>{day.label}</th>
                    <td>
                      <input
                        type="number"
                        min="0"
                        step="0.01"
                        value={day.targetSales}
                        onChange={(event) => updateTargetSales(day.position, event.target.value)}
                        readOnly={!canEdit}
                        aria-readonly={!canEdit}
                        aria-label={`${day.label} target sales`}
                      />
                    </td>
                    <td className="demand-stacked-cell">
                      <strong>{formatMetric(day.deliveries, 0)}</strong>
                      <small>{day.openHours} open hours</small>
                    </td>
                    <td className="demand-stacked-cell">
                      <strong>{day.requiredDriverHours} entered</strong>
                      <small>{formatMetric(day.idealDriverHours)} ideal · {formatMetric(day.wholeDriverHours, 0)} whole need</small>
                    </td>
                    <td className="demand-stacked-cell">
                      <strong className={Number(day.capacityUtilization) > 100 ? 'metric-danger' : undefined}>
                        {formatPercentage(day.capacityUtilization)} used
                      </strong>
                      <small>{formatMetric(day.deliveryCapacity)} capacity · {formatSignedMetric(day.unusedDeliveryCapacity)} spare</small>
                      <small>{formatMetric(day.deliveriesPerEnteredDriverHour)} deliveries per driver-hour</small>
                    </td>
                    <td className="demand-stacked-cell">
                      <strong>{formatMoney(day.labourCost)} entered</strong>
                      <small>{formatMoney(day.idealLabourCost)} ideal · <span className={Number(day.labourCostDifference) > 0 ? 'metric-warning' : undefined}>{formatSignedMoney(day.labourCostDifference)}</span> difference</small>
                      <small>{formatPercentage(day.labourPercentage)} of sales · {formatMoney(day.labourCostPerDelivery)} per delivery</small>
                      {day.sundayPremiumHours > 0 && <small>{day.sundayPremiumHours} Sunday-premium hours</small>}
                    </td>
                    <td className="demand-stacked-cell">
                      <span className={day.understaffedHours > 0 ? 'metric-danger' : 'metric-success'}>
                        {day.understaffedHours} under
                      </span>
                      <small>{day.matchedHours} match whole need</small>
                      <small>{day.aboveMinimumHours} above whole need</small>
                      {day.unknownHours > 0 && <small>{day.unknownHours} missing data</small>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <section className="demand-hourly-analysis" aria-labelledby="demand-hourly-heading">
            <div className="demand-hourly-heading">
              <div>
                <span className="eyebrow">Hourly breakdown</span>
                <h3 id="demand-hourly-heading">Delivery productivity and labour by hour</h3>
                <p>Use this to find under-entered demand, low capacity use, and the hourly cost of whole-driver rounding.</p>
              </div>
              <label>
                Day
                <select
                  value={selectedDemandDayPosition}
                  onChange={(event) => setSelectedDemandDayPosition(Number(event.target.value))}
                >
                  {plan.columns.map((column) => (
                    <option key={column.position} value={column.position}>{column.label}</option>
                  ))}
                </select>
              </label>
            </div>

            {selectedDaySummary && (
              <div className="demand-hourly-summary">
                <div><span>Deliveries</span><strong>{formatMetric(selectedDaySummary.deliveries, 0)}</strong></div>
                <div><span>Entered / ideal hours</span><strong>{selectedDaySummary.requiredDriverHours} / {formatMetric(selectedDaySummary.idealDriverHours)}</strong></div>
                <div><span>Capacity used</span><strong>{formatPercentage(selectedDaySummary.capacityUtilization)}</strong></div>
                <div><span>Actual / ideal labour</span><strong>{formatMoney(selectedDaySummary.labourCost)} / {formatMoney(selectedDaySummary.idealLabourCost)}</strong></div>
                <div><span>Cost per delivery</span><strong>{formatMoney(selectedDaySummary.labourCostPerDelivery)}</strong></div>
              </div>
            )}

            <div className="demand-labour-table-wrapper demand-hourly-table-wrapper">
              <table className="demand-labour-table demand-hourly-table">
                <thead>
                  <tr>
                    <th>Hour</th>
                    <th>Deliveries</th>
                    <th>Entered drivers</th>
                    <th>Ideal need</th>
                    <th>Staffing check</th>
                    <th>Capacity</th>
                    <th>Labour / hour</th>
                    <th>Cost / delivery</th>
                  </tr>
                </thead>
                <tbody>
                  {selectedDayHourlyRows.map((row) => {
                    const statusDetails = staffingStatus(row)
                    return (
                      <tr key={`${selectedDemandDayPosition}-${row.hour}`}>
                        <th>{String(row.hour).padStart(2, '0')}:00</th>
                        <td className="demand-stacked-cell">
                          <strong>{formatMetric(row.deliveries)}</strong>
                          <small>{formatMetric(row.deliveriesPerEnteredDriver)} per entered driver</small>
                        </td>
                        <td><strong>{formatMetric(row.enteredDrivers, 0)}</strong></td>
                        <td className="demand-stacked-cell">
                          <strong>{formatMetric(row.idealDrivers)} drivers</strong>
                          <small>{formatMetric(row.wholeDrivers, 0)} after rounding up</small>
                        </td>
                        <td><span className={`staffing-status staffing-status-${statusDetails.tone}`}>{statusDetails.label}</span></td>
                        <td className="demand-stacked-cell">
                          <strong className={Number(row.capacityUtilization) > 100 ? 'metric-danger' : undefined}>
                            {formatPercentage(row.capacityUtilization)} used
                          </strong>
                          <small>{formatMetric(row.deliveryCapacity)} deliveries capacity</small>
                          <small className={Number(row.unusedDeliveryCapacity) < 0 ? 'metric-danger' : undefined}>
                            {formatSignedMetric(row.unusedDeliveryCapacity)} spare
                          </small>
                        </td>
                        <td className="demand-stacked-cell">
                          <strong>{formatMoney(row.demandLabourCost)} entered</strong>
                          <small>{formatMoney(row.idealLabourCost)} ideal</small>
                          <small className={Number(row.labourCostDifference) > 0 ? 'metric-warning' : undefined}>
                            {formatSignedMoney(row.labourCostDifference)} difference
                          </small>
                          <small>{formatMoney(demandSummary.averageHourlyRate)} pay{row.isSundayPremium ? ' × 1.25 Sunday' : ''}</small>
                        </td>
                        <td>{formatMoney(row.labourCostPerDelivery)}</td>
                      </tr>
                    )
                  })}
                  {selectedDayHourlyRows.length === 0 && (
                    <tr><td colSpan="8" className="demand-empty-hourly">No open demand hours are available for this day.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>

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
                        {dailyStaffingRows.find((day) => day.position === column.position)?.requiredDriverHours ?? 0} driver-hours
                      </small>
                    </th>
                  ))}
                </tr>
                <tr>
                  {plan.columns.flatMap((column) => demandFields.map(([field, label]) => <th key={`${column.position}-${field}`}>{label}</th>))}
                </tr>
              </thead>
              <tbody>
                {plan.rows.map((row) => (
                  <tr key={row.hour}>
                    <th>{String(row.hour).padStart(2, '0')}</th>
                    {plan.columns.flatMap((column) => demandFields.map(([field, label]) => demandCell(row, column, field, label)))}
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
                        {selectedDaySummary?.requiredDriverHours ?? 0} driver-hours
                      </small>
                    </th>
                  </tr>
                  <tr>
                    <th>Hour</th>
                    {demandFields.map(([field, label]) => <th key={field}>{label}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {plan.rows.map((row) => <tr key={row.hour}>
                    <th>{String(row.hour).padStart(2, '0')}</th>
                    {selectedDemandColumn && demandFields.map(([field, label]) => demandCell(row, selectedDemandColumn, field, label))}
                  </tr>)}
                </tbody>
              </table>
            </div>
          </div>

          {canEdit && (
            <div className="demand-actions">
              <button type="submit" disabled={status.status === 'saving'}>Save demand changes</button>
              <button type="button" className="secondary-button" disabled={status.status === 'saving'} onClick={(event) => {
                if (event.currentTarget.form.reportValidity()) void savePlan(null, true)
              }}>Recalculate and save demand</button>
              <button type="button" className="secondary-button" onClick={deletePlan}>Delete plan</button>
            </div>
          )}
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
  const employeeEditorRef = useRef(null)

  useEffect(() => {
    if (employeeEditorOpen) {
      employeeEditorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }, [employeeEditorOpen])

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
    updateEmployeeForm('roles', ['Driver'])
    updateEmployeeForm('targetHours', 20)
    updateEmployeeForm('insideTargetHours', 20)
    updateEmployeeForm('driverType', 'Car')
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
  const groupedEmployees = employeeRoleGroups
    .map((group) => ({
      ...group,
      employees: employees
        .filter((employee) => getEmployeeRoleGroup(employee).id === group.id)
        .sort(compareEmployees),
    }))
    .filter((group) => group.employees.length > 0)
  const employeesById = new Map(employees.map((employee) => [String(employee.id), employee]))
  const groupedAvailability = rosterRoleGroups
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
  const editableRoles = authState.user.isAdmin
    ? ['Driver', 'InStore', 'Manager', 'Admin']
    : ['Driver', 'InStore', 'Manager']
  const employeeNumberErrors = getEmployeeFieldErrors(employeeSaveState, 'employeeNumber')
  const firstNameErrors = getEmployeeFieldErrors(employeeSaveState, 'firstName')
  const lastNameErrors = getEmployeeFieldErrors(employeeSaveState, 'lastName')
  const phoneNumberErrors = getEmployeeFieldErrors(employeeSaveState, 'phoneNumber')
  const payrollNumberErrors = getEmployeeFieldErrors(employeeSaveState, 'payrollNumber')
  const roleErrors = getEmployeeFieldErrors(employeeSaveState, 'roles')
  const targetHoursErrors = getEmployeeFieldErrors(employeeSaveState, 'targetHours')
  const hourlyRateErrors = getEmployeeFieldErrors(employeeSaveState, 'hourlyRate')
  const driverTypeErrors = getEmployeeFieldErrors(employeeSaveState, 'driverType')

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
            <span className="role-badge">{authState.user.isAdmin ? 'Admin' : 'Manager'}</span>
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
              className={activeTab === 'holiday' ? 'admin-tab active' : 'admin-tab'}
              onClick={() => switchTab('holiday')}
            >
              Holiday
            </button>
            <button type="button" className={activeTab === 'sick-leave' ? 'admin-tab active' : 'admin-tab'} onClick={() => switchTab('sick-leave')}>
              Sick leave
            </button>
            <button
              type="button"
              className={activeTab === 'roster' ? 'admin-tab active' : 'admin-tab'}
              onClick={() => switchTab('roster')}
            >
              Availability roster
            </button>
            {authState.user.isAdmin && (
              <button
                type="button"
                className={activeTab === 'employee-availability' ? 'admin-tab active' : 'admin-tab'}
                onClick={() => switchTab('employee-availability')}
              >
                Employee availability
              </button>
            )}
            {!authState.user.isAdmin && authState.user.employeeId && (
              <button
                type="button"
                className={activeTab === 'my-availability' ? 'admin-tab active' : 'admin-tab'}
                onClick={() => switchTab('my-availability')}
              >
                My availability
              </button>
            )}
            <button
              type="button"
              className={activeTab === 'demand' ? 'admin-tab active' : 'admin-tab'}
              onClick={() => switchTab('demand')}
            >
              Demand
            </button>
            {authState.user.isAdmin && (
              <button
                type="button"
                className={activeTab === 'generate-roster' ? 'admin-tab active' : 'admin-tab'}
                onClick={() => switchTab('generate-roster')}
              >
                Generate roster
              </button>
            )}
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
                        >
                          <strong>{employee.firstName} {employee.lastName}</strong>
                          <span>Employee number: {employee.employeeNumber}</span>
                          <span>Payroll number: {employee.payrollNumber || 'Not set'}</span>
                          <span>Hourly pay: €{Number(employee.hourlyRate ?? 14.5).toFixed(2)}</span>
                          <span>Phone: {employee.phoneNumber || 'Not set'}</span>
                          <span>Roles: {(employee.roles || []).join(', ') || 'Not set'}</span>
                          {employeeHasRole(employee, 'Driver') && (
                            <>
                              <span>Driver type: {employee.driverType || 'Car'}</span>
                              <span>Target hours: {employee.targetHours}</span>
                            </>
                          )}
                          <span className="employee-card-status">
                            {employee.isActive ? 'Active' : 'Inactive'}
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

              {employeeEditorOpen && (
                <section ref={employeeEditorRef} className="employee-editor-card">
                  <div className="section-heading">
                    <div>
                      <span className="eyebrow">Employee card</span>
                      <h2>{isCreatingEmployee ? 'Add employee' : 'Edit employee'}</h2>
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

                    <fieldset
                      className="employee-role-fieldset"
                      aria-invalid={roleErrors.length > 0}
                      aria-describedby={roleErrors.length > 0 ? 'admin-employee-roles-error' : undefined}
                    >
                      <legend>Roles</legend>
                      {editableRoles.map((role) => (
                        <label className="checkbox-label" htmlFor={`admin-employee-role-${role}`} key={role}>
                          <input
                            id={`admin-employee-role-${role}`}
                            type="checkbox"
                            checked={employeeForm.roles?.includes(role) || false}
                            onChange={(event) => {
                              const currentRoles = employeeForm.roles || []
                              const nextRoles = event.target.checked
                                ? [...new Set([...currentRoles, role])]
                                : currentRoles.filter((item) => item !== role)
                              updateEmployeeForm('roles', nextRoles)
                            }}
                          />
                          {role}
                        </label>
                      ))}
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
                          <label htmlFor="admin-employee-target-hours">Driver target hours per week</label>
                          <input
                            id="admin-employee-target-hours"
                            type="number"
                            min="3"
                            max="168"
                            step="1"
                            value={employeeForm.targetHours}
                            onChange={(event) => updateEmployeeForm('targetHours', Number(event.target.value))}
                            aria-invalid={targetHoursErrors.length > 0}
                            aria-describedby={targetHoursErrors.length > 0 ? 'admin-employee-targetHours-error' : undefined}
                            required
                          />
                          <EmployeeFieldError field="targetHours" messages={targetHoursErrors} />
                        </div>
                      </>
                    )}

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
                      <p className={`save-message ${employeeSaveState.status}`} role={employeeSaveState.status === 'error' ? 'alert' : undefined}>
                        {employeeSaveState.message}
                      </p>
                    )}
                  </form>
                </section>
              )}
            </section>
          )}

          {activeTab === 'holiday' && (
            authState.user.isAdmin
              ? <AdminHolidayPanel setErrorPopup={setErrorPopup} />
              : <>
                <AdminHolidayPanel setErrorPopup={setErrorPopup} canExport={false} />
                <HolidayPanel setErrorPopup={setErrorPopup} />
              </>
          )}

          {activeTab === 'sick-leave' && <AdminSickLeavePanel setErrorPopup={setErrorPopup} includeOwnRequest={!authState.user.isAdmin && Boolean(authState.user.employeeId)} />}

          {activeTab === 'roster' && (
            <section className="availability-section admin-tools">
              <div className="section-heading">
                <div>
                  <span className="eyebrow">Admin overview</span>
                  <h2>Driver availability roster</h2>
                </div>
                <WeekSelector weekOffset={weekOffset} onChange={setWeekOffset} />
              </div>

              {availability && (
                <>
                  <div className="week-range availability-week-range">
                    {dateFormatter.format(parseDate(availability.weekStart))} –{' '}
                    {dateFormatter.format(parseDate(availability.weekEnd))}
                  </div>
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
                scheduleState={scheduleState}
                weekOffset={weekOffset}
                setWeekOffset={setWeekOffset}
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

          {activeTab === 'demand' && <DemandManager
            setErrorPopup={setErrorPopup}
            employees={employees}
            canEdit={authState.user.isAdmin}
          />}

          {authState.user.isAdmin && (
            <RosterGenerationPanel
              fetchJson={fetchJson}
              setErrorPopup={setErrorPopup}
              isVisible={activeTab === 'generate-roster'}
              onShowSaved={(weekStart) => { setSavedWeekStart(weekStart); switchTab('saved-rosters') }}
            />
          )}
          {activeTab === 'saved-rosters' && <SavedRosterPanel
            key={savedWeekStart || 'upcoming'}
            fetchJson={fetchJson}
            setErrorPopup={setErrorPopup}
            initialWeekStart={savedWeekStart}
            canEdit={authState.user.isAdmin}
          />}
        </section>
      </main>
    </>
  )
}

function TimeSelector({ id, label, value, onChange, disabled = false }) {
  const selectedHour = value ? value.split(':')[0] : ''

  return (
    <div className="time-selector" id={id} aria-label={label}>
      <select
        aria-label={`${label} (24-hour format)`}
        value={selectedHour}
        onChange={(event) =>
          onChange(event.target.value ? `${event.target.value}:00` : '')
        }
        disabled={disabled}
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

function ScheduleEditor({
  schedule,
  scheduleState,
  weekOffset,
  setWeekOffset,
  saveState,
  saveSchedule,
  updateDay,
  resetDay,
}) {
  const minimumEditableWeekOffset = schedule?.minimumEditableWeekOffset ?? minWeekOffset
  const canEdit = schedule?.canEdit !== false

  function changeWeek(direction) {
    setWeekOffset((current) =>
      Math.min(maxWeekOffset, Math.max(minimumEditableWeekOffset, current + direction)),
    )
  }

  if (!schedule) {
    return null
  }

  return (
    <form className="schedule-form" onSubmit={saveSchedule}>
      <div className="schedule-heading">
        <div>
          <span className="eyebrow">Weekly availability</span>
          <h2>{schedule.employeeName}</h2>
        </div>
        <div className="week-navigation" aria-label="Week navigation">
          <button
            type="button"
            className="week-arrow"
            aria-label="Previous week"
            onClick={() => changeWeek(-1)}
            disabled={weekOffset <= minimumEditableWeekOffset || scheduleState.status === 'loading'}
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

      {!canEdit && (
        <p className="message info-message" role="status">
          Availability for next week is locked from Saturday. Select the week after next to make changes.
        </p>
      )}

      <div className="schedule-table" role="table" aria-label="Weekly availability">
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
                disabled={!canEdit}
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
                disabled={!canEdit}
              />
            </div>
            <div role="cell">
              <button
                type="button"
                className="reset-button"
                onClick={() => resetDay(day.date)}
                disabled={!canEdit || (!day.startTime && !day.finishTime)}
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
        <button type="submit" disabled={!canEdit || saveState.status === 'saving'}>
          {saveState.status === 'saving' ? 'Saving…' : 'Save availability'}
        </button>
      </div>
    </form>
  )
}

function DriverWorkspace(props) {
  return (
    <EmployeeWorkspace
      {...props}
      variant="driver"
      title="Driver workspace"
      lead="Set the hours you are available to work for the selected week."
    />
  )
}

function InStoreWorkspace(props) {
  return (
    <EmployeeWorkspace
      {...props}
      variant="instore"
      title="In-store workspace"
      lead="Set the hours you are available to work for the selected week."
    />
  )
}

function EmployeeWorkspace({
  variant,
  title,
  lead,
  children,
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
}) {
  const [activeTab, setActiveTab] = useState('availability')
  const roleLabel = variant === 'driver'
    ? 'Driver'
    : variant === 'instore'
      ? 'In-store'
      : 'Employee'

  return (
    <>
      <ErrorPopup message={errorPopup} onClose={() => setErrorPopup('')} />
      <main className="page-shell">
        <section className={`app-card employee-workspace ${variant}-workspace`}>
          <header className="page-header">
            <span className="eyebrow">Roaster Generator</span>
            <h1>{title}</h1>
            <p className="lead">{lead}</p>
          </header>

          <div className="app-toolbar">
            <div className="current-employee">
              <span className="eyebrow">Signed in as</span>
              <strong>{authState.user.employeeName || authState.user.username}</strong>
            </div>
            <span className="role-badge">{roleLabel}</span>
            <button type="button" className="secondary-button" onClick={logout}>Sign out</button>
          </div>

          <nav className="workspace-tabs" aria-label="Employee sections">
            <button
              type="button"
              className={activeTab === 'availability' ? 'workspace-tab active' : 'workspace-tab'}
              onClick={() => setActiveTab('availability')}
            >
              Availability
            </button>
            <button
              type="button"
              className={activeTab === 'holiday' ? 'workspace-tab active' : 'workspace-tab'}
              onClick={() => setActiveTab('holiday')}
            >
              Holiday
            </button>
            <button type="button" className={activeTab === 'sick-leave' ? 'workspace-tab active' : 'workspace-tab'} onClick={() => setActiveTab('sick-leave')}>
              Sick leave
            </button>
          </nav>

          {activeTab === 'holiday' ? (
            <HolidayPanel setErrorPopup={setErrorPopup} />
          ) : activeTab === 'sick-leave' ? (
            <SickLeavePanel setErrorPopup={setErrorPopup} />
          ) : (
            <>
              {children}

              {employeesState.status === 'error' && (
                <p className="message error-message">{employeesState.message}</p>
              )}

              {employeesState.status === 'success' && !employee && (
                <p className="message info-message">Your employee profile is not available.</p>
              )}

              {scheduleState.status === 'loading' && (
                <p className="message info-message">Loading availability…</p>
              )}

              {scheduleState.status === 'error' && (
                <p className="message error-message">{scheduleState.message}</p>
              )}

              <ScheduleEditor
                schedule={schedule}
                scheduleState={scheduleState}
                weekOffset={weekOffset}
                setWeekOffset={setWeekOffset}
                saveState={saveState}
                saveSchedule={saveSchedule}
                updateDay={updateDay}
                resetDay={resetDay}
              />
            </>
          )}
        </section>
      </main>
    </>
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
  const isSystemAdmin = isAuthenticated && authState.user.isAdmin
  const isManager = isAuthenticated && authState.user.roles?.includes('Manager')
  const isAdmin = isSystemAdmin || isManager
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
  }, [availabilityEmployeeId, weekOffset, dataRefreshKey])

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
        payrollNumber: employee.payrollNumber || '',
        hourlyRate: employee.hourlyRate ?? 14.5,
        roles: employee.roles?.length ? employee.roles : ['Driver'],
        targetHours: employee.targetHours ?? 20,
        insideTargetHours: employee.insideTargetHours ?? 20,
        driverType: employee.driverType || 'Car',
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
    if (!availabilityEmployeeId || schedule?.employeeId !== availabilityEmployeeId ||
        scheduleState.status !== 'success' || schedule?.canEdit === false || saveState.status === 'saving') {
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
      <section className="role-details employee-details">
        <span className="eyebrow">Employee profile</span>
        <h2>Your availability</h2>
        <p>Your shared employee information is available to your manager.</p>
      </section>
    </EmployeeWorkspace>
  )
}

export default App
