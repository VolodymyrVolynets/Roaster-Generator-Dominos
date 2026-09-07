import { useEffect, useState } from 'react'

const dateFormatter = new Intl.DateTimeFormat(undefined, {
  day: 'numeric',
  month: 'short',
})

const hours = Array.from({ length: 24 }, (_, index) => String(index).padStart(2, '0'))
const minWeekOffset = 1
const maxWeekOffset = 3
const weekLabels = ['Current week', 'Next week', 'Week after next', 'Three weeks ahead']

function parseDate(dateValue) {
  const [year, month, day] = dateValue.split('-').map(Number)
  return new Date(year, month - 1, day)
}

async function fetchJson(url, options) {
  const response = await fetch(url, options)
  const body = await response.text()
  let payload = null

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

    throw new Error(messages.join('\n') || body || 'The API request failed.')
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
  const [employees, setEmployees] = useState([])
  const [selectedEmployeeId, setSelectedEmployeeId] = useState('')
  const [employeesState, setEmployeesState] = useState({ status: 'loading' })
  const [scheduleState, setScheduleState] = useState({ status: 'idle' })
  const [schedule, setSchedule] = useState(null)
  const [saveState, setSaveState] = useState({ status: 'idle' })
  const [weekOffset, setWeekOffset] = useState(minWeekOffset)
  const [errorPopup, setErrorPopup] = useState('')

  useEffect(() => {
    fetchJson('/api/employees')
      .then((payload) => {
        setEmployees(payload)
        setSelectedEmployeeId(payload.length ? String(payload[0].id) : '')
        setEmployeesState({ status: 'success' })
      })
      .catch((error) => {
        setEmployeesState({ status: 'error', message: error.message })
        setErrorPopup(error.message)
      })
  }, [])

  useEffect(() => {
    if (!selectedEmployeeId) {
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
  }, [selectedEmployeeId, weekOffset])

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

  return (
    <>
      <ErrorPopup message={errorPopup} onClose={() => setErrorPopup('')} />
      <main className="page-shell">
      <section className="app-card">
        <header className="page-header">
          <span className="eyebrow">Roaster Generator</span>
          <h1>Weekly shifts</h1>
          <p className="lead">
            Choose an employee and set their start and finish times for next week.
          </p>
        </header>

        <div className="employee-picker">
          <label htmlFor="employee">Employee</label>
          <select
            id="employee"
            value={selectedEmployeeId}
            onChange={(event) => {
              setSelectedEmployeeId(event.target.value)
              setWeekOffset(minWeekOffset)
            }}
            disabled={employeesState.status !== 'success' || employees.length === 0}
          >
            <option value="">
              {employeesState.status === 'loading' ? 'Loading employees…' : 'Select an employee'}
            </option>
            {employees.map((employee) => (
              <option key={employee.id} value={employee.id}>
                {employee.firstName} {employee.lastName}
              </option>
            ))}
          </select>
        </div>

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
