import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'
import { createRosterEventState, mergeRosterEvents } from './rosterEvents'
import { compareRosterEmployees, driverRosterOnly, employeeHasRole, getRosterRoleGroup, rosterRoleGroups } from './employeeGroups'
import SavedRosterLabour from './SavedRosterLabour'

const weightFields = [
  ['targetHoursWeight', 'Equal target percentages', 'Primary fairness preference. Increase it to keep scheduled hours close to each employee’s target percentage; reduce it when availability or exact coverage needs more flexibility. Set to 0 to disable it.'],
  ['historyFairnessWeight', 'Compensate previous weeks', 'Uses up to four saved weeks. Increase it to give more hours to employees who have been below the group percentage and fewer to those above it; reduce it to focus mostly on this week.'],
  ['historyShiftLengthWeight', 'Compensate previous short shifts', 'Uses actual shift lengths from the previous four saved weeks. Increase it to prefer longer shifts for employees whose earlier shifts were shorter than the group average, with a preferred length between 6 and 8 hours. This is balanced against availability, demand and target-hour fairness. Set to 0 to disable; default: 100.'],
  ['fairnessSpreadWeight', 'Avoid gaps above 30 percentage points', 'Adds a strong penalty when target-percentage spread exceeds 30 points. Increase it to close large gaps; reduce it if coverage, availability, or shift shape needs more flexibility.'],
  ['longShiftBonus', 'Longer shifts', 'Rewards shifts in the preferred 6–8 hour range, with longer legal shifts scoring better. Increase it to join adjacent demand into longer shifts; very high values can make target balancing harder.'],
  ['shortShiftPenalty', 'Avoid short shifts', 'Penalises shifts below 6 hours while the hard minimum remains 3 hours. Increase it to avoid 3–5 hour shifts; reduce it when sparse demand makes short coverage useful.'],
  ['dailyShiftCountPenalty', 'Fewer shifts', 'Penalises multiple shifts for one employee on the same business day. Increase it to consolidate coverage into fewer shifts; reduce it when gaps or availability require separate shifts.'],
  ['shortBreakPenalty', 'Longer breaks', 'Penalises rest below the preferred-rest value while respecting the hard minimum. Increase it to spread consecutive shifts farther apart; reduce it when availability is tight.'],
]
const runningStatuses = new Set(['starting', 'started', 'running', 'queued'])
const numberFormatter = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 })
const dateFormatter = new Intl.DateTimeFormat(undefined, { weekday: 'short', day: 'numeric', month: 'short' })
const formatNumber = (value) => numberFormatter.format(Number(value ?? 0))
const formatStage = (stage) => (stage || 'starting').replace(/[-_]/g, ' ').replace(/\b\w/g, (letter) => letter.toUpperCase())
const parseDate = (value) => new Date(`${value}T12:00:00`)
const formatDate = (value) => value ? dateFormatter.format(parseDate(value)) : ''
const withSettingsDefaults = (settings) => ({ historyFairnessWeight: 100, historyShiftLengthWeight: 100, fairnessSpreadWeight: 1000, latestShiftStartHour: 20, ...settings })
const averageFormatter = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 })
const averageShiftHours = (shifts) => shifts.length ? Math.round(shifts.reduce((total, shift) => total + shift.durationHours, 0) * 100 / shifts.length) / 100 : 0
const rosterKind = 'drivers'
const hourOptions = Array.from({ length: 24 }, (_, hour) => `${String(hour).padStart(2, '0')}:00`)

function WeekSelector({ value, onChange, disabled }) {
  return (
    <label className="week-selector">
      Week
      <select value={value} onChange={(event) => onChange(Number(event.target.value))} disabled={disabled}>
        <option value={1}>Next week</option>
        <option value={2}>Week after next</option>
        <option value={3}>Three weeks ahead</option>
      </select>
    </label>
  )
}

function SettingsForm({ settings, setSettings, savedSettings, saveSettings, saving, running, error, onRetry }) {
  const dirty = settings && savedSettings && JSON.stringify(settings) !== JSON.stringify(savedSettings)
  return (
    <details className="roster-settings" open>
      <summary>Scheduling preferences</summary>
      <p className="demand-help">
        Demand is exact at every hour. Every shift must be 3–10 hours, within availability, and start no later than {settings?.latestShiftStartHour ?? 20}:00.
        Shifts may finish after the latest start time or overnight, with at most one shift per business day.
        Employees with a zero-hour target may cover demand as reserves and are excluded from percentage balancing.
        Weight fields use a 0–1000 scale: higher values give that preference more influence and 0 disables it. Exact coverage,
        availability, and hard shift rules always apply. Start with the defaults, then raise one weight at a time when a specific
        outcome needs more influence.
      </p>
      {!settings ? error ? <div>
        <p className="message error-message" role="alert">{error}</p>
        <button type="button" className="secondary-button" onClick={onRetry}>Retry loading preferences</button>
      </div> : <p className="message info-message">Loading saved preferences…</p> : (
        <form onSubmit={saveSettings}>
          <fieldset className="roster-settings-fields" disabled={saving || running}>
            <div className="roster-settings-grid">
              {weightFields.map(([field, label, help]) => (
                <label key={field}>
                  <span>{label}</span>
                  <input type="number" min="0" max="1000" step="1" required value={settings[field]}
                    onChange={(event) => setSettings({ ...settings, [field]: event.target.value === '' ? '' : Number(event.target.value) })} />
                  <small>{help}</small>
                </label>
              ))}
              <label>
                <span>Minimum rest (hours)</span>
                <input type="number" min="0" max="24" step="1" required value={settings.minimumRestHours}
                  onChange={(event) => setSettings({ ...settings, minimumRestHours: event.target.value === '' ? '' : Number(event.target.value) })} />
                <small>Hard limit between shifts, including adjacent saved weeks. Increase it for more recovery time; lower it only when availability cannot support the spacing. Default: 8 hours.</small>
              </label>
              <label>
                <span>Preferred rest (hours)</span>
                <input type="number" min={settings.minimumRestHours || 0} max="48" step="1" required value={settings.preferredRestHours}
                  onChange={(event) => setSettings({ ...settings, preferredRestHours: event.target.value === '' ? '' : Number(event.target.value) })} />
                <small>Soft goal balanced against the weights above. Increase it to prefer longer breaks; lower it when the week is tightly staffed. Default: 12 hours.</small>
              </label>
              <label>
                <span>Latest shift start (HH:00)</span>
                <input type="number" min="6" max="22" step="1" required value={settings.latestShiftStartHour}
                  onChange={(event) => setSettings({ ...settings, latestShiftStartHour: event.target.value === '' ? '' : Number(event.target.value) })} />
                <small>Hard start-time limit in whole hours. Default: 20:00. Lower it to finish staffing earlier; raise it only when later starts are safe. The limit cannot exceed 22:00 and overnight finishes remain allowed.</small>
              </label>
              <label>
                <span>Solver time budget (seconds)</span>
                <input type="number" min="1" max="120" step="1" required value={settings.maxSolveSeconds}
                  onChange={(event) => setSettings({ ...settings, maxSolveSeconds: event.target.value === '' ? '' : Number(event.target.value) })} />
                <small>Limits optimisation time on slower servers. Increase it for better fairness search; lower it for faster responses. A feasible roster can be saved before optimality is proven.</small>
              </label>
            </div>
            <div className="roster-generation-actions">
              <button type="submit" className="secondary-button" disabled={!dirty || saving || running}>
                {saving ? 'Saving preferences…' : 'Save preferences'}
              </button>
              <span className="save-message">{dirty ? 'Save your changes before generating.' : 'Saved preferences will be used for the next roster.'}</span>
            </div>
          </fieldset>
        </form>
      )}
    </details>
  )
}

export function RosterGenerationPanel({ fetchJson, setErrorPopup, isVisible, onShowSaved }) {
  const [weekOffset, setWeekOffset] = useState(1)
  const [hubStatus, setHubStatus] = useState('connecting')
  const [generation, setGeneration] = useState(null)
  const [logs, setLogs] = useState([])
  const [summary, setSummary] = useState(null)
  const [settings, setSettings] = useState(null)
  const [savedSettings, setSavedSettings] = useState(null)
  const [savingSettings, setSavingSettings] = useState(false)
  const [settingsError, setSettingsError] = useState('')
  const [settingsRefreshKey, setSettingsRefreshKey] = useState(0)
  const [starting, setStarting] = useState(false)
  const [cancelling, setCancelling] = useState(false)
  const [replayError, setReplayError] = useState('')
  const logRef = useRef(null)
  const controllerRef = useRef(null)
  const currentJobRef = useRef(null)
  const running = starting || runningStatuses.has(generation?.status)
  const dirtySettings = settings && savedSettings && JSON.stringify(settings) !== JSON.stringify(savedSettings)

  useEffect(() => {
    if (!isVisible || dirtySettings) return
    let active = true
    setSettingsError('')
    fetchJson('/api/admin/roster/settings', { cache: 'no-store' }).then((payload) => {
      if (active) { const normalized = withSettingsDefaults(payload); setSettings(normalized); setSavedSettings(normalized) }
    }).catch((error) => { if (active) { setSettingsError(error.message); setErrorPopup(error.message) } })
    return () => { active = false }
  }, [fetchJson, setErrorPopup, settingsRefreshKey, isVisible, dirtySettings])

  useEffect(() => {
    if (!isVisible) return
    let active = true
    setSummary(null)
    fetchJson(`/api/admin/roster/summary?weekOffset=${weekOffset}&rosterKind=${rosterKind}`, { cache: 'no-store' })
      .then((payload) => { if (active) setSummary(payload) })
      .catch((error) => { if (active) setErrorPopup(error.message) })
    return () => { active = false }
  }, [fetchJson, setErrorPopup, weekOffset, rosterKind, isVisible])

  useEffect(() => {
    if (logRef.current) logRef.current.scrollTop = logRef.current.scrollHeight
  }, [logs])

  useEffect(() => {
    let active = true
    let retryTimer
    let replayPending = false
    let eventState = createRosterEventState()
    const notifiedFailures = new Set()
    setGeneration(null)
    setLogs([])
    setReplayError('')
    setHubStatus('connecting')
    currentJobRef.current = null

    function ingest(events) {
      if (!active) return
      eventState = mergeRosterEvents(eventState, events, weekOffset, rosterKind)
      const currentJob = eventState.generation
      if (currentJob?.status === 'failed' && !notifiedFailures.has(currentJob.jobId)) {
        notifiedFailures.add(currentJob.jobId)
        const details = currentJob.diagnostics || []
        setErrorPopup([currentJob.message, ...details.slice(0, 2),
          ...(details.length > 2 ? [`${details.length} scheduling details are listed below. Use the filter to find a day, hour, or employee.`] : []),
        ].join('\n'))
      }
      currentJobRef.current = currentJob
      setGeneration(currentJob)
      setLogs(eventState.logs)
    }

    async function replay() {
      if (replayPending || !active) return
      replayPending = true
      try {
        const payload = await fetchJson(`/api/admin/roster/jobs?weekOffset=${weekOffset}&rosterKind=${rosterKind}`, { cache: 'no-store' })
        if (active) { ingest(payload); setReplayError('') }
      } catch (error) {
        if (active) setReplayError(`Cannot recover generation status: ${error.message}`)
      } finally {
        replayPending = false
      }
    }

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/roster-generation', { transport: HttpTransportType.WebSockets, skipNegotiation: true })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => Math.min(30000, 1000 * 2 ** Math.min(context.previousRetryCount, 5)) })
      .configureLogging(LogLevel.Warning)
      .build()
    connection.on('rosterGenerationProgress', (payload) => ingest([payload]))
    connection.onreconnecting(() => { if (active) setHubStatus('reconnecting') })
    connection.onreconnected(() => { if (active) { setHubStatus('connected'); void replay() } })
    connection.onclose(() => {
      if (active) { setHubStatus('disconnected'); retryTimer = window.setTimeout(connect, 5000) }
    })
    async function connect() {
      try {
        await connection.start()
        if (!active) { void connection.stop(); return }
        setHubStatus('connected')
        await replay()
      } catch {
        if (active) { setHubStatus('disconnected'); retryTimer = window.setTimeout(connect, 5000) }
      }
    }
    controllerRef.current = { ingest, replay }
    void replay()
    void connect()
    // Recover missed events and show progress even if the WebSocket is temporarily unavailable.
    const replayTimer = window.setInterval(() => {
      if (runningStatuses.has(eventState.generation?.status) || connection.state !== 'Connected') void replay()
    }, 10000)
    const recoverOnFocus = () => { void replay() }
    window.addEventListener('focus', recoverOnFocus)
    return () => {
      active = false
      controllerRef.current = null
      window.clearInterval(replayTimer)
      window.clearTimeout(retryTimer)
      window.removeEventListener('focus', recoverOnFocus)
      // Stopping the connection never cancels the server's generation job.
      void connection.stop()
    }
  }, [fetchJson, setErrorPopup, weekOffset, rosterKind])

  async function saveSettings(event) {
    event.preventDefault()
    setSavingSettings(true)
    try {
      const payload = await fetchJson('/api/admin/roster/settings', {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(settings),
      })
      const normalized = withSettingsDefaults(payload)
      setSettings(normalized)
      setSavedSettings(normalized)
    } catch (error) { setErrorPopup(error.message) }
    finally { setSavingSettings(false) }
  }

  async function generate() {
    setStarting(true)
    try {
      const payload = await fetchJson(`/api/admin/roster/generate?rosterKind=${rosterKind}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ weekOffset }),
      })
      controllerRef.current?.ingest([payload])
    } catch (error) { setErrorPopup(error.message) }
    finally { await controllerRef.current?.replay(); setStarting(false) }
  }

  async function cancel() {
    const jobId = currentJobRef.current?.jobId
    if (!jobId) return
    setCancelling(true)
    try {
      await fetchJson(`/api/admin/roster/cancel?rosterKind=${rosterKind}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ weekOffset, jobId }),
      })
      await controllerRef.current?.replay()
    } catch (error) { setErrorPopup(error.message) }
    finally { setCancelling(false) }
  }

  const diagnostics = [...new Set(logs.flatMap((log) => log.diagnostics || []))]
  const progress = Math.max(0, Math.min(100, Number(generation?.progress || 0)))
  return (
    <section className="admin-tools roster-generation-tools" hidden={!isVisible}>
      <div className="section-heading">
        <div><span className="eyebrow">Administration</span><h2>Generate roster</h2></div>
        <WeekSelector value={weekOffset} onChange={setWeekOffset} disabled={running} />
      </div>
      <p className="demand-help">
        Build an exact demand match, then balance target percentages, past-hour differences, shift lengths, and rest.
        A successful roster is saved automatically. An impossible week is explained in the activity log.
        {' '}Every e-bike shift must have a car or moped driver alongside it.
      </p>
      {summary && (
        <>
          <p className="save-message">Week starting {formatDate(summary.weekStart)}</p>
          <div className="roster-week-summary" aria-label="Roster week summary">
            <Metric label="Hours needed" value={`${formatNumber(summary.requiredHours ?? summary.requiredDriverHours)}h`} />
            <Metric label="Availability entered" value={`${formatNumber(summary.enteredAvailabilityHours)}h`} />
            <Metric label="Employees with no availability" value={summary.employeesWithoutAvailability ?? summary.driversWithoutAvailability} />
          </div>
          {!summary.demandPlanExists && <p className="message info-message">Import a demand plan before generating this week.</p>}
        </>
      )}
      <SettingsForm settings={settings} setSettings={setSettings} savedSettings={savedSettings}
        saveSettings={saveSettings} saving={savingSettings} running={running} error={settingsError}
        onRetry={() => setSettingsRefreshKey((value) => value + 1)} />
      <div className="roster-generation-actions">
        <button type="button" onClick={generate} disabled={running || savingSettings || !settings || dirtySettings || summary?.demandPlanExists === false}>
          {starting ? 'Starting…' : running ? 'Generating roster…' : 'Generate driver roster'}
        </button>
        {running && <button type="button" className="danger-button" onClick={cancel} disabled={!generation?.jobId || starting || cancelling}>
          {cancelling ? 'Cancelling…' : 'Cancel generation'}
        </button>}
        <span className={`hub-status ${hubStatus}`}>Live updates: {hubStatus}</span>
      </div>
      <p className="roster-footnote">Generation continues if you change tabs or close this page. Regenerating replaces only the selected roster type for this week, once a valid result is ready.</p>
      {hubStatus !== 'connected' && <p className="save-message">Reconnecting to live updates. The latest job status is also recovered automatically.</p>}
      {replayError && <p className="message error-message" role="alert">{replayError}</p>}
      {generation && (
        <div className={`roster-generation-status ${generation.status}`} aria-live="polite">
          <div className="roster-generation-status-heading">
            <div className="roster-generation-stage"><span className="eyebrow">{generation.status}</span><strong>{formatStage(generation.stage)}</strong></div>
            <span>{progress}%</span>
          </div>
          <p className="roster-generation-message">{generation.message}</p>
          <div className="roster-progress" role="progressbar" aria-label="Generation progress" aria-valuemin="0" aria-valuemax="100" aria-valuenow={progress}>
            <span style={{ width: `${progress}%` }} />
          </div>
          <small>Week starting {formatDate(generation.weekStart)}</small>
          {generation.status === 'completed' && <div className="roster-generation-actions">
            <button type="button" onClick={() => onShowSaved(generation.weekStart)}>View saved roster</button>
          </div>}
        </div>
      )}
      {diagnostics.length > 0 && <DiagnosticList key={generation?.jobId} entries={diagnostics}
        title={generation?.status === 'failed' ? 'Why this week could not be generated' : 'Scheduling details'} />}
      <div className="roster-generation-log" aria-label="Generation activity log">
        <div className="roster-generation-log-heading">
          <div><span className="eyebrow">Live activity</span><h3>Generation log</h3></div>
          <span>{logs.length} events</span>
        </div>
        {!logs.length && <p className="roster-footnote">Generation steps, constraint checks, warnings, and errors will appear here.</p>}
        <ol ref={logRef} role="log" aria-live="polite" aria-relevant="additions">
          {logs.map((log) => <li key={`${log.jobId}-${log.sequence}`} className={`log-${log.severity || 'info'}`}>
            <time dateTime={log.timestampUtc}>{log.timestampUtc ? new Date(log.timestampUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }) : ''}</time>
            <strong>{log.severity === 'error' ? 'Error · ' : log.severity === 'warning' ? 'Warning · ' : ''}{formatStage(log.stage)}</strong>
            <span>{log.message}</span>
          </li>)}
        </ol>
      </div>
    </section>
  )
}

function Metric({ label, value }) {
  return <div className="roster-week-summary-card"><span>{label}</span><strong>{value}</strong></div>
}

function DiagnosticList({ entries, title }) {
  const [filter, setFilter] = useState('')
  const terms = filter.toLocaleLowerCase().trim().split(/\s+/).filter(Boolean)
  const filtered = entries.filter((entry) => terms.every((term) => entry.toLocaleLowerCase().includes(term)))
  return <div className="roster-diagnostics" role="region" aria-label={title}>
    <h3>{title}</h3>
    {entries.length > 8 && <label className="roster-diagnostic-filter">
      Filter scheduling details
      <input type="search" value={filter} onChange={(event) => setFilter(event.target.value)} placeholder="Sunday 13:00, employee name…" />
      <small>{filtered.length} of {entries.length} details</small>
    </label>}
    <ul>{filtered.map((entry, index) => <li key={index}>{entry}</li>)}</ul>
    {!filtered.length && <p className="roster-footnote">No scheduling details match that filter.</p>}
  </div>
}

function weekDates(weekStart) {
  const start = parseDate(weekStart)
  return Array.from({ length: 7 }, (_, offset) => {
    const date = new Date(start)
    date.setDate(date.getDate() + offset)
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
  })
}

function shiftTime(time, dayOffset = Number(time.slice(0, 2)) < 6 ? 1 : 0) {
  return `${time.slice(0, 5)}${dayOffset > 0 ? ` +${dayOffset}d` : ''}`
}

function shiftStartHour(shift) {
  const hour = Number(shift.startTime.slice(0, 2)) + Number(shift.startTime.slice(3, 5)) / 60
  return Date.parse(`${shift.date}T00:00:00Z`) / 3600000 + hour + (shift.startDayOffset ?? (hour < 6 ? 1 : 0)) * 24
}

function clockHour(time) {
  return Number(String(time || '00:00').slice(0, 2))
}

function absoluteShiftHour(time, dayOffset = 0) {
  return clockHour(time) + Number(dayOffset || 0) * 24
}

function hourInputValue(hour) {
  return `${String(((Number(hour) % 24) + 24) % 24).padStart(2, '0')}:00`
}

function draftShift(employeeId, shift) {
  return {
    employeeId: String(employeeId),
    date: shift.date,
    startHour: absoluteShiftHour(shift.startTime, shift.startDayOffset),
    finishHour: absoluteShiftHour(shift.finishTime, shift.finishDayOffset),
  }
}

function rosterDraftShifts(roster) {
  return roster.employees.flatMap((employee) => employee.shifts.map((shift) => draftShift(employee.employeeId, shift)))
}

function RosterCellEditorDialog({ editor, setEditor, onApply, onRemove, onClose }) {
  const startInputRef = useRef(null)
  const finishDayOffset = Math.max(0, Math.floor(Number(editor.finishHour) / 24))
  const duration = Number(editor.finishHour) - Number(editor.startHour)
  const durationIsValid = duration >= 3 && duration <= 10

  useEffect(() => {
    startInputRef.current?.focus()
    const closeOnEscape = (event) => {
      if (event.key === 'Escape') setEditor(null)
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [setEditor])

  function updateClock(field, value, dayOffset) {
    setEditor((current) => ({ ...current, [field]: Number(dayOffset) * 24 + clockHour(value) }))
  }

  function updateDayOffset(field, value) {
    setEditor((current) => ({ ...current, [field]: Number(value) * 24 + (Number(current[field]) % 24) }))
  }

  return (
    <div className="roster-cell-dialog-backdrop" role="presentation" onMouseDown={(event) => {
      if (event.target === event.currentTarget) onClose()
    }}>
      <section className="roster-cell-dialog" role="dialog" aria-modal="true" aria-labelledby="roster-cell-dialog-title">
        <div className="roster-cell-dialog-heading">
          <div>
            <span className="eyebrow">Edit roster shift</span>
            <h3 id="roster-cell-dialog-title">{editor.employeeName}</h3>
            <p>{formatDate(editor.date)}</p>
          </div>
          <button type="button" className="secondary-button roster-cell-dialog-close" onClick={onClose} aria-label="Close shift editor">×</button>
        </div>
        <form onSubmit={onApply}>
          <div className="roster-cell-dialog-fields">
            <label>
              Start time
              <select
                ref={startInputRef}
                value={hourInputValue(editor.startHour)}
                onChange={(event) => updateClock('startHour', event.target.value, 0)}
              >
                {hourOptions.map((time) => <option key={time} value={time}>{time}</option>)}
              </select>
            </label>
            <label>
              Finish time
              <select
                value={hourInputValue(editor.finishHour)}
                onChange={(event) => updateClock('finishHour', event.target.value, finishDayOffset)}
              >
                {hourOptions.map((time) => <option key={time} value={time}>{time}</option>)}
              </select>
            </label>
            <label>
              Finish day
              <select value={finishDayOffset} onChange={(event) => updateDayOffset('finishHour', event.target.value)}>
                <option value="0">Same date</option>
                <option value="1">Following date</option>
              </select>
            </label>
          </div>
          <p className={`roster-cell-dialog-duration ${durationIsValid ? 'valid' : 'invalid'}`} role="status">
            {durationIsValid
              ? `${duration} hour shift`
              : 'Shift duration must be between 3 and 10 hours, and finish must be after start.'}
          </p>
          <p className="roster-footnote">Changes are staged until you press Save roster changes.</p>
          <div className="roster-cell-dialog-actions">
            <button type="submit" disabled={!durationIsValid}>{editor.hasShift ? 'Apply times' : 'Add shift'}</button>
            {editor.hasShift && <button type="button" className="danger-button" onClick={onRemove}>Remove shift</button>}
            <button type="button" className="secondary-button" onClick={onClose}>Cancel</button>
          </div>
        </form>
      </section>
    </div>
  )
}

function minimumEmployeeRest(shifts) {
  const ordered = [...shifts].sort((left, right) => shiftStartHour(left) - shiftStartHour(right))
  if (ordered.length < 2) return null
  return Math.min(...ordered.slice(1).map((shift, index) => shiftStartHour(shift) - shiftStartHour(ordered[index]) - ordered[index].durationHours))
}

function exportRosterCsv(roster, dates) {
  const rows = [['Employee', 'Target hours', 'Scheduled hours', 'Average hours per shift', 'Target %', 'Previous saved weeks',
    'Previous scheduled hours', 'Previous target hours', 'Previous target %', 'Previous shifts with known durations', 'Previous average hours per shift', 'History-adjusted goal hours this week', 'Cumulative target %', ...dates]]
  for (const employee of roster.employees) {
    rows.push([employee.employeeName, employee.targetHours, employee.scheduledHours,
      employee.averageHoursPerShift ?? averageShiftHours(employee.shifts),
      employee.targetPercentage ?? (employee.targetHours > 0 ? employee.scheduledHours / employee.targetHours * 100 : ''),
      employee.historyWeeks ?? '', employee.previousScheduledHours ?? '', employee.previousTargetHours ?? '',
      employee.previousTargetPercentage ?? '', employee.previousShiftCount ?? '', employee.previousAverageHoursPerShift ?? '',
      employee.balancedTargetHours ?? '', employee.cumulativeTargetPercentage ?? '',
      ...dates.map((date) => employee.shifts.filter((shift) => shift.date === date)
        .map((shift) => `${shiftTime(shift.startTime, shift.startDayOffset)}–${shiftTime(shift.finishTime, shift.finishDayOffset)} (${shift.durationHours}h)`).join('; '))])
  }
  const csv = rows.map((row) => row.map((value) => {
    const text = String(value ?? '')
    const safe = /^[=+\-@\t\r]/.test(text) ? `'${text}` : text
    return `"${safe.replaceAll('"', '""')}"`
  }).join(',')).join('\r\n')
  const url = URL.createObjectURL(new Blob(['\uFEFF', csv], { type: 'text/csv;charset=utf-8;' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `roster-drivers-${roster.weekStart}.csv`
  link.click()
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}

function FairnessComparison({ roster, employeeGroups }) {
  const currentSpread = roster.fairnessSpreadPercentagePoints
  const cumulativeSpread = roster.historicalFairnessSpreadPercentagePoints
  const hasHistory = roster.employees.some((employee) => employee.historyWeeks > 0)
  const groups = employeeGroups?.length
    ? employeeGroups
    : [{ id: 'all', label: 'Employees', employees: roster.employees }]
  const warnings = []
  if (currentSpread > 30) warnings.push(`This week’s target percentages differ by ${formatNumber(currentSpread)} percentage points, above the 30-point review threshold.`)
  if (hasHistory && cumulativeSpread > 30) warnings.push(`The cumulative target percentages differ by ${formatNumber(cumulativeSpread)} percentage points across the saved history and this week.`)
  if (warnings.length) warnings.push('Past-hour compensation, availability, exact demand, shift lengths, and rest limits may affect the gap. The search time budget can also limit improvement; review the individual percentages before using this roster.')
  return <details className="roster-fairness-details">
    <summary>
      <span>Fairness across five weeks</span>
      <small>{formatNumber(currentSpread)} pp this week · {cumulativeSpread == null ? 'No cumulative history' : `${formatNumber(cumulativeSpread)} pp cumulative`}</small>
    </summary>
    <section className="roster-fairness-section" aria-label="Fairness across five weeks">
      <p className="demand-help">
        Compare this week with up to four earlier saved weeks. Each percentage is scheduled hours divided by target hours;
        the cumulative percentage uses the combined hours and targets. The history-adjusted goal gradually compensates for
        the employee’s difference from the group’s previous percentage, divided across the saved weeks and capped at a
        15-percentage-point correction this week. This reference goal is balanced with your weights and the shift rules.
        Previous average shift length uses recorded shift durations from those four weeks; older weeks without duration
        details are excluded. The short-shift compensation preference uses this average to favour longer shifts for drivers
        who previously had shorter shifts, while preserving availability and demand coverage.
      </p>
      <div className="roster-week-summary roster-fairness-metrics">
        <Metric label="This week’s percentage gap" value={`${formatNumber(currentSpread)} pp`} />
        <Metric label="Cumulative percentage gap" value={cumulativeSpread == null ? 'Not recorded' : `${formatNumber(cumulativeSpread)} pp`} />
      </div>
      {!hasHistory && <p className="roster-footnote">No earlier saved employee history is available for this roster. Fairness starts with this week.</p>}
      {warnings.length > 0 && <DiagnosticList entries={warnings} title="Review the remaining fairness gap" />}
      <div className="demand-table-wrapper">
        <table className="saved-roster-table roster-fairness-table">
          <caption className="visually-hidden">Previous weeks, current week, and cumulative target-hour percentages</caption>
          <thead><tr>
            <th scope="col">Employee</th>
            <th scope="col">Previous weeks</th>
            <th scope="col">Previous hours / target</th>
            <th scope="col">Previous target %</th>
            <th scope="col">Previous average shift</th>
            <th scope="col">This week’s average shift</th>
            <th scope="col">This week’s target %</th>
            <th scope="col">History-adjusted goal</th>
            <th scope="col">Cumulative target %</th>
          </tr></thead>
          <tbody>{groups.flatMap((group) => [
            <tr className={`roster-group-divider roster-group-divider-${group.id}`} key={`${group.id}-heading`}>
              <th colSpan="9" scope="rowgroup">{group.label}</th>
            </tr>,
            ...group.employees.map((employee) => {
              const currentPercentage = employee.targetPercentage ?? (employee.targetHours > 0 ? employee.scheduledHours / employee.targetHours * 100 : null)
              const priorPercentage = employee.previousTargetPercentage
              const cumulativePercentage = employee.cumulativeTargetPercentage
              return <tr key={employee.employeeId}>
                <th scope="row">{employee.employeeName}</th>
                <td>{employee.historyWeeks || 0} / 4</td>
                <td>{employee.historyWeeks ? `${formatNumber(employee.previousScheduledHours)} / ${formatNumber(employee.previousTargetHours)}h` : 'No history'}</td>
                <td>{priorPercentage == null ? '—' : `${formatNumber(priorPercentage)}%`}</td>
                <td>{employee.previousAverageHoursPerShift == null ? 'No recorded shifts' : `${averageFormatter.format(employee.previousAverageHoursPerShift)}h`}
                  {employee.previousShiftCount != null && <small>{employee.previousShiftCount} recorded shifts</small>}</td>
                <td>{averageFormatter.format(employee.averageHoursPerShift ?? averageShiftHours(employee.shifts))}h</td>
                <td><strong className="roster-target-percentage">{currentPercentage == null ? 'N/A' : `${formatNumber(currentPercentage)}%`}</strong><small>{formatNumber(employee.scheduledHours)} / {formatNumber(employee.targetHours)}h</small></td>
                <td>{employee.balancedTargetHours == null ? '—' : `${formatNumber(employee.balancedTargetHours)}h`}</td>
                <td><strong className="roster-target-percentage">{cumulativePercentage == null ? '—' : `${formatNumber(cumulativePercentage)}%`}</strong></td>
              </tr>
            }),
          ])}</tbody>
        </table>
      </div>
    </section>
  </details>
}

function SavedRosterEmployeeTables({
  roster,
  groupedRosterEmployees,
  dates,
  demandMismatchByDate,
  editing = false,
  draftShifts = [],
  onEditCell,
}) {
  const mismatchDetails = (date) => demandMismatchByDate.get(date)?.join(' · ')
  const mismatchEntries = [...demandMismatchByDate.entries()].flatMap(([date, details]) => details.map((detail) => ({ date, detail })))

  return <>
    <h3 className="roster-section-title">Employee shifts and target hours</h3>
    <p className="demand-help">Target percentage is scheduled hours divided by target hours. Red dates indicate an hourly demand mismatch; the roster remains saved so the administrator can review and correct it.</p>
    {demandMismatchByDate.size > 0 && (
      <div className="roster-demand-warning" role="alert">
        <strong>Demand mismatch saved with this roster</strong>
        <p>Availability and hard shift rules passed. Red dates show where scheduled staff differ from the demand plan.</p>
        <ul>
          {mismatchEntries.slice(0, 12).map(({ date, detail }) => (
            <li key={`${date}-${detail}`}><strong>{formatDate(date)}</strong> {detail}</li>
          ))}
        </ul>
        {mismatchEntries.length > 12 && <small>Additional mismatched hours are highlighted in red in the tables.</small>}
      </div>
    )}
    <div className="roster-role-groups saved-roster-role-groups">
      {groupedRosterEmployees.map((group) => (
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
            <div className="demand-table-wrapper">
              <table className={`saved-roster-table${editing ? ' roster-table-editing' : ''}`}>
                <caption className="visually-hidden">{group.label} roster for the week starting {roster.weekStart}</caption>
                <thead><tr>
                  <th scope="col">Employee</th>
                  <th scope="col">Hours / target</th>
                  <th scope="col">Target %</th>
                  {dates.map((date) => {
                    const mismatch = mismatchDetails(date)
                    return <th scope="col" key={date} className={mismatch ? 'demand-mismatch' : ''} title={mismatch || undefined}>
                      {formatDate(date)}
                      {mismatch && <small className="roster-mismatch-label">Demand mismatch</small>}
                    </th>
                  })}
                  <th scope="col">Shortest rest*</th>
                </tr></thead>
                <tbody>{group.employees.map((employee) => {
                  const percentage = employee.targetPercentage ?? (employee.targetHours > 0 ? employee.scheduledHours / employee.targetHours * 100 : null)
                  const rest = minimumEmployeeRest(employee.shifts)
                  return <tr key={employee.employeeId}>
                    <th scope="row">{employee.employeeName}</th>
                    <td>{formatNumber(employee.scheduledHours)} / {formatNumber(employee.targetHours)}h
                      <small>{employee.shifts.length ? `Avg ${averageFormatter.format(employee.averageHoursPerShift ?? averageShiftHours(employee.shifts))}h per shift` : 'No shifts'}</small>
                    </td>
                    <td><strong className="roster-target-percentage">{percentage == null ? 'N/A' : `${formatNumber(percentage)}%`}</strong>{percentage == null && <small>Zero target · reserve</small>}</td>
                    {dates.map((date) => {
                      const mismatch = mismatchDetails(date)
                      const cellShifts = editing
                        ? draftShifts.filter((shift) => String(shift.employeeId) === String(employee.employeeId) && shift.date === date)
                        : employee.shifts.filter((shift) => shift.date === date)
                      const content = cellShifts.length > 0
                        ? cellShifts.map((shift, index) => {
                          const startTime = editing ? hourInputValue(shift.startHour) : shift.startTime
                          const finishTime = editing ? hourInputValue(shift.finishHour) : shift.finishTime
                          const startOffset = editing ? Math.floor(Number(shift.startHour) / 24) : shift.startDayOffset
                          const finishOffset = editing ? Math.floor(Number(shift.finishHour) / 24) : shift.finishDayOffset
                          const duration = editing ? Number(shift.finishHour) - Number(shift.startHour) : shift.durationHours
                          return <span className="roster-shift" key={index}>
                            <span>{shiftTime(startTime, startOffset)}–{shiftTime(finishTime, finishOffset)}</span><small>{formatNumber(duration)}h</small>
                          </span>
                        })
                        : <span className="roster-day-off">Off</span>
                      const title = [mismatch, editing ? `Edit ${employee.employeeName} on ${formatDate(date)}` : null].filter(Boolean).join(' · ')

                      return <td
                        key={date}
                        className={`roster-shift-cell${mismatch ? ' demand-mismatch' : ''}${editing ? ' roster-shift-cell-editable' : ''}`}
                        title={title || undefined}
                      >
                        {editing ? (
                          <button
                            type="button"
                            className="roster-shift-cell-button"
                            onClick={() => onEditCell(employee, date)}
                            aria-label={`${cellShifts.length ? 'Edit' : 'Add'} shift for ${employee.employeeName} on ${formatDate(date)}`}
                          >
                            {content}
                            <span className="roster-cell-edit-hint">Click to edit</span>
                          </button>
                        ) : content}
                      </td>
                    })}
                    <td>{rest == null ? '—' : `${formatNumber(rest)}h`}</td>
                  </tr>
                })}</tbody>
              </table>
            </div>
          ) : (
            <p className="roster-role-group-empty">No {group.label.toLowerCase()} are included in this roster.</p>
          )}
        </section>
      ))}
    </div>
    <p className="roster-footnote">* Per-employee rest shown here is between shifts in this roster. The generator also checks adjacent saved weeks.</p>
  </>
}

export function SavedRosterPanel({ fetchJson, setErrorPopup, initialWeekStart, canEdit = true }) {
  const [history, setHistory] = useState([])
  const [selection, setSelection] = useState(initialWeekStart ? `date:${initialWeekStart}` : 'offset:1')
  const [roster, setRoster] = useState(null)
  const [availableEmployees, setAvailableEmployees] = useState([])
  const [draftShifts, setDraftShifts] = useState([])
  const [editing, setEditing] = useState(false)
  const [cellEditor, setCellEditor] = useState(null)
  const [editStatus, setEditStatus] = useState({ status: 'idle', message: '' })
  const [status, setStatus] = useState('loading')
  const [refreshKey, setRefreshKey] = useState(0)
  const [labourRefreshKey, setLabourRefreshKey] = useState(0)

  useEffect(() => {
    let active = true
    setHistory([])
    fetchJson(`/api/admin/roster/history?rosterKind=${rosterKind}`, { cache: 'no-store' })
      .then((payload) => { if (active) setHistory(payload.filter((item) => !item.rosterKind || item.rosterKind === rosterKind)) })
      .catch((error) => { if (active) setErrorPopup(error.message) })
    return () => { active = false }
  }, [fetchJson, setErrorPopup, refreshKey, rosterKind])

  useEffect(() => {
    if (!canEdit) {
      setAvailableEmployees([])
      return undefined
    }

    let active = true
    setAvailableEmployees([])
    fetchJson('/api/admin/employees', { cache: 'no-store' })
      .then((payload) => { if (active) setAvailableEmployees(payload.filter((employee) => employee.isActive && employeeHasRole(employee, 'Driver') && getRosterRoleGroup(employee)?.id === rosterKind)) })
      .catch((error) => { if (active) setErrorPopup(error.message) })
    return () => { active = false }
  }, [canEdit, fetchJson, setErrorPopup, rosterKind])

  useEffect(() => {
    let active = true
    setStatus('loading')
    setRoster(null)
    setEditStatus({ status: 'idle', message: '' })
    const [kind, value] = selection.split(':')
    const query = kind === 'date' ? `weekStart=${encodeURIComponent(value)}` : `weekOffset=${value}`
    fetchJson(`/api/admin/roster?${query}&rosterKind=${rosterKind}`, { cache: 'no-store' }).then((payload) => {
      if (active) {
        const driverRoster = driverRosterOnly(payload)
        setRoster(driverRoster)
        setStatus(driverRoster ? 'success' : 'empty')
      }
    }).catch((error) => {
      if (!active) return
      setStatus(error.status === 404 ? 'empty' : 'error')
      if (error.status !== 404) setErrorPopup(error.message)
    })
    return () => { active = false }
  }, [fetchJson, setErrorPopup, selection, refreshKey, rosterKind])

  useEffect(() => {
    if (!roster) return
    setDraftShifts(rosterDraftShifts(roster))
    setEditing(false)
    setCellEditor(null)
  }, [roster])

  function openCellEditor(employee, date) {
    const existingShift = draftShifts.find((shift) => (
      String(shift.employeeId) === String(employee.employeeId) && shift.date === date
    ))

    setCellEditor({
      employeeId: String(employee.employeeId),
      employeeName: employee.employeeName,
      date,
      startHour: existingShift?.startHour ?? 12,
      finishHour: existingShift?.finishHour ?? 18,
      hasShift: Boolean(existingShift),
    })
    setEditStatus({ status: 'idle', message: '' })
  }

  function applyCellEdit(event) {
    event.preventDefault()
    if (!cellEditor) return

    const updatedShift = {
      employeeId: cellEditor.employeeId,
      date: cellEditor.date,
      startHour: Number(cellEditor.startHour),
      finishHour: Number(cellEditor.finishHour),
    }
    setDraftShifts((current) => {
      const existingIndex = current.findIndex((shift) => (
        String(shift.employeeId) === cellEditor.employeeId && shift.date === cellEditor.date
      ))
      if (existingIndex < 0) return [...current, updatedShift]
      return current.map((shift, index) => index === existingIndex ? updatedShift : shift)
    })
    setCellEditor(null)
    setEditStatus({ status: 'idle', message: '' })
  }

  function removeCellShift() {
    if (!cellEditor) return
    setDraftShifts((current) => current.filter((shift) => !(
      String(shift.employeeId) === cellEditor.employeeId && shift.date === cellEditor.date
    )))
    setCellEditor(null)
    setEditStatus({ status: 'idle', message: '' })
  }

  function toggleEditing() {
    if (editing && roster) {
      setDraftShifts(rosterDraftShifts(roster))
      setCellEditor(null)
    }
    setEditing((value) => !value)
    setEditStatus({ status: 'idle', message: '' })
  }

  async function saveEditedRoster(event) {
    event.preventDefault()
    setEditStatus({ status: 'saving', message: '' })
    try {
      const payload = await fetchJson('/api/admin/roster', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          weekStart: roster.weekStart,
          rosterKind,
          shifts: draftShifts.map((shift) => ({
            employeeId: shift.employeeId,
            date: shift.date,
            startHour: Number(shift.startHour),
            finishHour: Number(shift.finishHour),
          })),
        }),
      })
      setRoster(driverRosterOnly(payload))
      setLabourRefreshKey((value) => value + 1)
      setEditStatus({ status: 'success', message: 'Roster changes saved and revalidated.' })
      setEditing(false)
      setCellEditor(null)
    } catch (error) {
      setEditStatus({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  const dates = roster ? weekDates(roster.weekStart) : []
  const shifts = roster?.employees.flatMap((employee) => employee.shifts) || []
  const preferredShiftCount = shifts.filter((shift) => shift.durationHours >= 6 && shift.durationHours <= 8).length
  const demandMismatchByDate = new Map()
  for (const slot of roster?.coverage || []) {
    if (slot.required === slot.scheduled) continue
    const details = demandMismatchByDate.get(slot.date) || []
    details.push(`${shiftTime(slot.startTime, slot.startDayOffset)}: required ${slot.required}, scheduled ${slot.scheduled}`)
    demandMismatchByDate.set(slot.date, details)
  }
  const availableEmployeeById = new Map(availableEmployees.map((employee) => [String(employee.id), employee]))
  const groupedRosterEmployees = rosterRoleGroups.map((group) => ({
    ...group,
    employees: (roster?.employees || [])
      .filter((employee) => {
        const employeeDirectoryEntry = employee.roles?.length ? employee : availableEmployeeById.get(String(employee.employeeId)) || { roles: ['Driver'] }
        return getRosterRoleGroup(employeeDirectoryEntry)?.id === group.id
      })
      .sort(compareRosterEmployees),
  }))
  const populatedRosterEmployeeGroups = groupedRosterEmployees.filter((group) => group.employees.length > 0)
  const savedDates = [...new Set([...(initialWeekStart ? [initialWeekStart] : []), ...history.map((item) => item.weekStart)])]
    .sort((left, right) => right.localeCompare(left))
  const [selectionKind, selectionValue] = selection.split(':')
  const labourQuery = selectionKind === 'date' ? `weekStart=${encodeURIComponent(selectionValue)}` : `weekOffset=${selectionValue}`

  return (
    <section className="admin-tools saved-roster-tools">
      <div className="section-heading">
        <div><span className="eyebrow">Administration</span><h2>Saved rosters</h2></div>
        <label className="week-selector">
          Week
          <select value={selection} onChange={(event) => setSelection(event.target.value)}>
            <optgroup label="Upcoming weeks">
              <option value="offset:1">Next week</option><option value="offset:2">Week after next</option><option value="offset:3">Three weeks ahead</option>
            </optgroup>
            {savedDates.length > 0 && <optgroup label="Saved weeks">{savedDates.map((date) => <option key={date} value={`date:${date}`}>Week of {formatDate(date)}</option>)}</optgroup>}
          </select>
        </label>
      </div>
      <div className="roster-generation-actions">
        <button type="button" className="secondary-button" disabled={status === 'loading'} onClick={() => setRefreshKey((value) => value + 1)}>Refresh saved roster</button>
        {roster && <button type="button" className="secondary-button" onClick={() => exportRosterCsv(roster, dates)}>Download CSV</button>}
        {canEdit && roster && <button type="button" className="secondary-button" onClick={toggleEditing}>{editing ? 'Cancel editing' : 'Edit roster'}</button>}
      </div>
      {editStatus.message && !editing && <p className={`save-message ${editStatus.status}`} role="status">{editStatus.message}</p>}
      {status === 'loading' && <p className="message info-message" role="status">Loading saved roster…</p>}
      {status === 'empty' && <p className="message info-message">No roster has been saved for this week. {canEdit ? 'Use Generate roster to create one.' : 'An administrator must generate one.'}</p>}
      {status === 'error' && <p className="message error-message" role="alert">The saved roster could not be loaded. Try refreshing.</p>}
      {roster && <>
        {!canEdit && <p className="message info-message" role="status">Read-only view for managers. Only administrators can edit saved roster shifts.</p>}
        <p className="roster-footnote">Week starting {formatDate(roster.weekStart)} · Saved {new Date(roster.updatedAtUtc).toLocaleString()}</p>
        {canEdit && editing && <form className="roster-edit-panel" onSubmit={saveEditedRoster}>
          <div>
            <span className="eyebrow">Administrator editing</span>
            <h3>Edit shifts directly in the roster</h3>
          </div>
          <p className="demand-help">Click any day cell below to add, change, or remove that employee’s shift. Changes are checked against availability, rest, driver support, and shift-length rules when saved.</p>
          <div className="roster-generation-actions">
            <button type="submit" disabled={editStatus.status === 'saving'}>{editStatus.status === 'saving' ? 'Validating and saving…' : 'Save roster changes'}</button>
            <button type="button" className="secondary-button" onClick={toggleEditing}>Discard changes</button>
            {editStatus.message && <span className={`save-message ${editStatus.status}`}>{editStatus.message}</span>}
          </div>
        </form>}
        <SavedRosterEmployeeTables
          roster={roster}
          groupedRosterEmployees={groupedRosterEmployees}
          dates={dates}
          demandMismatchByDate={demandMismatchByDate}
          editing={canEdit && editing}
          draftShifts={draftShifts}
          onEditCell={openCellEditor}
        />
        {cellEditor && <RosterCellEditorDialog
          editor={cellEditor}
          setEditor={setCellEditor}
          onApply={applyCellEdit}
          onRemove={removeCellShift}
          onClose={() => setCellEditor(null)}
        />}
      </>}
      <SavedRosterLabour fetchJson={fetchJson} query={labourQuery}
        refreshKey={`${rosterKind}:${refreshKey}:${labourRefreshKey}`} editing={editing && !!roster} />
      {roster && <>
        <div className="roster-week-summary roster-result-metrics">
          <Metric label="Demand coverage" value={roster.coveragePercent == null ? 'Unverified' : `${formatNumber(roster.coveragePercent)}%`} />
          <Metric label="Scheduled / needed" value={`${formatNumber(roster.totalScheduledHours)} / ${formatNumber(roster.totalDemandHours)}h`} />
          <Metric label="Target percentage spread" value={`${formatNumber(roster.fairnessSpreadPercentagePoints)} pp`} />
          <Metric label="Shortest rest" value={roster.minimumRestHours == null ? 'No shift pairs' : `${formatNumber(roster.minimumRestHours)}h`} />
          <Metric label="6–8 hour shifts" value={`${preferredShiftCount} / ${shifts.length}`} />
          <Metric label="Average hours per shift" value={`${averageFormatter.format(roster.averageHoursPerShift ?? averageShiftHours(shifts))}h`} />
          <Metric label="Solver time" value={`${formatNumber(roster.solveSeconds)}s`} />
        </div>
        <p className="message info-message">
          {roster.solverStatus === 'legacy' ? 'This older roster has no generation audit snapshot.' : roster.solverStatus === 'manual' ? 'Manually updated by an administrator; all hard constraints were revalidated.' : roster.isOptimal ? 'Best preference score proven for these constraints.' : 'Valid roster saved; the solver has not proven the best possible preference score.'}
          {' '}Status: {formatStage(roster.solverStatus)}.
        </p>
        {!!roster.warnings?.length && <DiagnosticList entries={roster.warnings} title="Roster notes" />}
        <FairnessComparison roster={roster} employeeGroups={populatedRosterEmployeeGroups} />
        {roster.settings && <details className="roster-settings"><summary>Preferences used for this roster</summary>
          <dl className="roster-snapshot-grid">
            {weightFields.map(([field, label]) => <div key={field}><dt>{label}</dt><dd>{roster.settings[field] ?? 'Not recorded'}</dd></div>)}
            <div><dt>Minimum rest</dt><dd>{roster.settings.minimumRestHours}h</dd></div>
            <div><dt>Preferred rest</dt><dd>{roster.settings.preferredRestHours}h</dd></div>
            <div><dt>Latest shift start</dt><dd>{String(roster.settings.latestShiftStartHour ?? 20).padStart(2, '0')}:00</dd></div>
            <div><dt>Solver time budget</dt><dd>{roster.settings.maxSolveSeconds}s</dd></div>
          </dl>
        </details>}
      </>}
    </section>
  )
}
