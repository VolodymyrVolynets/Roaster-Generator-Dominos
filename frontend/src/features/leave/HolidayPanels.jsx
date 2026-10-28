import { useEffect, useState } from 'react'
import { fetchJson } from '../../api/client'
import { formatHolidayDate } from '../../lib/format'

export function HolidayPanel({ setErrorPopup, realtimeKey = 0 }) {
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
  }, [realtimeKey])

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
            <div className="number-stepper">
              <button
                type="button"
                aria-label="Fewer hours"
                onClick={() => setHours((current) => String(Math.max(1, (Number(current) || 1) - 1)))}
                disabled={Number(hours) <= 1}
              >
                −
              </button>
              <input
                id="holiday-hours"
                type="number"
                inputMode="numeric"
                min="1"
                max="80"
                step="1"
                placeholder="0"
                value={hours}
                onChange={(event) => setHours(event.target.value)}
                required
              />
              <span className="number-stepper-unit" aria-hidden="true">h</span>
              <button
                type="button"
                aria-label="More hours"
                onClick={() => setHours((current) => String(Math.min(80, (Number(current) || 0) + 1)))}
                disabled={Number(hours) >= 80}
              >
                +
              </button>
            </div>
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
            {showEmployee && <th>Payroll number</th>}
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
                <td className="cell-primary" data-label="Employee">
                  <strong>{holiday.employeeName}</strong>
                  <small>#{holiday.employeeNumber}</small>
                </td>
              )}
              {showEmployee && <td data-label="Payroll">{holiday.payrollNumber || 'Not set'}</td>}
              <td data-label="Hours">{holiday.hours}</td>
              <td data-label="Status"><span className={`holiday-status ${holiday.status.toLowerCase()}`}>{holiday.status}</span></td>
              <td data-label="Requested">{formatHolidayDate(holiday.createdAtUtc)}</td>
              <td data-label="Used">{formatHolidayDate(holiday.usedAtUtc)}</td>
              {showApprove && (
                <td className="cell-actions">
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

export function AdminHolidayPanel({ setErrorPopup, canExport = true, realtimeKey = 0 }) {
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
  }, [realtimeKey])

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
