import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'
import { createRosterEventState, mergeRosterEvents } from './rosterEvents'
import { compareRosterEmployees, getRosterRoleGroup, rosterRoleGroups } from './employeeGroups'

const weightFields = [
  ['targetHoursWeight', 'Equal target percentages', 'Primary fairness preference. Increase it to keep scheduled hours close to each employee’s target percentage; reduce it when availability or exact coverage needs more flexibility. Set to 0 to disable it.'],
  ['historyFairnessWeight', 'Compensate previous weeks', 'Uses up to four saved weeks. Increase it to give more hours to employees who have been below the group percentage and fewer to those above it; reduce it to focus mostly on this week.'],
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
const withSettingsDefaults = (settings) => ({ historyFairnessWeight: 100, fairnessSpreadWeight: 1000, latestShiftStartHour: 20, ...settings })
const averageFormatter = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 })
const averageShiftHours = (shifts) => shifts.length ? Math.round(shifts.reduce((total, shift) => total + shift.durationHours, 0) * 100 / shifts.length) / 100 : 0

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
    fetchJson(`/api/admin/roster/summary?weekOffset=${weekOffset}`, { cache: 'no-store' })
      .then((payload) => { if (active) setSummary(payload) })
      .catch((error) => { if (active) setErrorPopup(error.message) })
    return () => { active = false }
  }, [fetchJson, setErrorPopup, weekOffset, isVisible])

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
      eventState = mergeRosterEvents(eventState, events, weekOffset)
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
        const payload = await fetchJson(`/api/admin/roster/jobs?weekOffset=${weekOffset}`, { cache: 'no-store' })
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
  }, [fetchJson, setErrorPopup, weekOffset])

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
      const payload = await fetchJson('/api/admin/roster/generate', {
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
      await fetchJson('/api/admin/roster/cancel', {
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
      </p>
      {summary && (
        <>
          <p className="save-message">Week starting {formatDate(summary.weekStart)}</p>
          <div className="roster-week-summary" aria-label="Roster week summary">
            <Metric label="Hours needed" value={`${formatNumber(summary.requiredDriverHours)}h`} />
            <Metric label="Availability entered" value={`${formatNumber(summary.enteredAvailabilityHours)}h`} />
            <Metric label="Drivers with no availability" value={summary.driversWithoutAvailability} />
          </div>
          {!summary.demandPlanExists && <p className="message info-message">Import a demand plan before generating this week.</p>}
        </>
      )}
      <SettingsForm settings={settings} setSettings={setSettings} savedSettings={savedSettings}
        saveSettings={saveSettings} saving={savingSettings} running={running} error={settingsError}
        onRetry={() => setSettingsRefreshKey((value) => value + 1)} />
      <div className="roster-generation-actions">
        <button type="button" onClick={generate} disabled={running || savingSettings || !settings || dirtySettings || summary?.demandPlanExists === false}>
          {starting ? 'Starting…' : running ? 'Generating roster…' : 'Generate and save roster'}
        </button>
        {running && <button type="button" className="danger-button" onClick={cancel} disabled={!generation?.jobId || starting || cancelling}>
          {cancelling ? 'Cancelling…' : 'Cancel generation'}
        </button>}
        <span className={`hub-status ${hubStatus}`}>Live updates: {hubStatus}</span>
      </div>
      <p className="roster-footnote">Generation continues if you change tabs or close this page. Regenerating replaces the saved roster for this week only after a valid result is ready.</p>
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

function minimumEmployeeRest(shifts) {
  const ordered = [...shifts].sort((left, right) => shiftStartHour(left) - shiftStartHour(right))
  if (ordered.length < 2) return null
  return Math.min(...ordered.slice(1).map((shift, index) => shiftStartHour(shift) - shiftStartHour(ordered[index]) - ordered[index].durationHours))
}

function exportRosterCsv(roster, dates) {
  const rows = [['Employee', 'Target hours', 'Scheduled hours', 'Average hours per shift', 'Target %', 'Previous saved weeks',
    'Previous scheduled hours', 'Previous target hours', 'Previous target %', 'History-adjusted goal hours this week', 'Cumulative target %', ...dates]]
  for (const employee of roster.employees) {
    rows.push([employee.employeeName, employee.targetHours, employee.scheduledHours,
      employee.averageHoursPerShift ?? averageShiftHours(employee.shifts),
      employee.targetPercentage ?? (employee.targetHours > 0 ? employee.scheduledHours / employee.targetHours * 100 : ''),
      employee.historyWeeks ?? '', employee.previousScheduledHours ?? '', employee.previousTargetHours ?? '',
      employee.previousTargetPercentage ?? '', employee.balancedTargetHours ?? '', employee.cumulativeTargetPercentage ?? '',
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
  link.download = `roster-${roster.weekStart}.csv`
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
  return <section className="roster-fairness-section" aria-label="Fairness across five weeks">
    <h3 className="roster-section-title">Fairness across five weeks</h3>
    <p className="demand-help">
      Compare this week with up to four earlier saved weeks. Each percentage is scheduled hours divided by target hours;
      the cumulative percentage uses the combined hours and targets. The history-adjusted goal gradually compensates for
      the employee’s difference from the group’s previous percentage, divided across the saved weeks and capped at a
      15-percentage-point correction this week. This reference goal is balanced with your weights and the shift rules.
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
          <th scope="col">This week’s target %</th>
          <th scope="col">History-adjusted goal</th>
          <th scope="col">Cumulative target %</th>
        </tr></thead>
        <tbody>{groups.flatMap((group) => [
          <tr className={`roster-group-divider roster-group-divider-${group.id}`} key={`${group.id}-heading`}>
            <th colSpan="7" scope="rowgroup">{group.label}</th>
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
              <td><strong className="roster-target-percentage">{currentPercentage == null ? 'N/A' : `${formatNumber(currentPercentage)}%`}</strong><small>{formatNumber(employee.scheduledHours)} / {formatNumber(employee.targetHours)}h</small></td>
              <td>{employee.balancedTargetHours == null ? '—' : `${formatNumber(employee.balancedTargetHours)}h`}</td>
              <td><strong className="roster-target-percentage">{cumulativePercentage == null ? '—' : `${formatNumber(cumulativePercentage)}%`}</strong></td>
            </tr>
          }),
        ])}</tbody>
      </table>
    </div>
  </section>
}

export function SavedRosterPanel({ fetchJson, setErrorPopup, initialWeekStart }) {
  const [history, setHistory] = useState([])
  const [selection, setSelection] = useState(initialWeekStart ? `date:${initialWeekStart}` : 'offset:1')
  const [roster, setRoster] = useState(null)
  const [availableEmployees, setAvailableEmployees] = useState([])
  const [draftShifts, setDraftShifts] = useState([])
  const [editing, setEditing] = useState(false)
  const [editStatus, setEditStatus] = useState({ status: 'idle', message: '' })
  const [status, setStatus] = useState('loading')
  const [refreshKey, setRefreshKey] = useState(0)
  const [coverageDay, setCoverageDay] = useState('')

  useEffect(() => {
    let active = true
    fetchJson('/api/admin/roster/history', { cache: 'no-store' })
      .then((payload) => { if (active) setHistory(payload) })
      .catch((error) => { if (active) setErrorPopup(error.message) })
    return () => { active = false }
  }, [fetchJson, setErrorPopup, refreshKey])

  useEffect(() => {
    let active = true
    fetchJson('/api/admin/employees', { cache: 'no-store' })
      .then((payload) => { if (active) setAvailableEmployees(payload.filter((employee) => employee.isActive)) })
      .catch((error) => { if (active) setErrorPopup(error.message) })
    return () => { active = false }
  }, [fetchJson, setErrorPopup])

  useEffect(() => {
    let active = true
    setStatus('loading')
    setRoster(null)
    setEditStatus({ status: 'idle', message: '' })
    const [kind, value] = selection.split(':')
    const query = kind === 'date' ? `weekStart=${encodeURIComponent(value)}` : `weekOffset=${value}`
    fetchJson(`/api/admin/roster?${query}`, { cache: 'no-store' }).then((payload) => {
      if (active) { setRoster(payload); setCoverageDay(payload.weekStart); setStatus('success') }
    }).catch((error) => {
      if (!active) return
      setStatus(error.status === 404 ? 'empty' : 'error')
      if (error.status !== 404) setErrorPopup(error.message)
    })
    return () => { active = false }
  }, [fetchJson, setErrorPopup, selection, refreshKey])

  useEffect(() => {
    if (!roster) return
    setDraftShifts(rosterDraftShifts(roster))
    setEditing(false)
  }, [roster])

  function updateDraftShift(index, changes) {
    setDraftShifts((current) => current.map((shift, shiftIndex) => (
      shiftIndex === index ? { ...shift, ...changes } : shift
    )))
    setEditStatus({ status: 'idle', message: '' })
  }

  function addDraftShift() {
    const employeeId = String((availableEmployees[0] || roster?.employees[0])?.id || roster?.employees[0]?.employeeId || '')
    setDraftShifts((current) => [
      ...current,
      { employeeId, date: roster.weekStart, startHour: 12, finishHour: 18 },
    ])
    setEditing(true)
    setEditStatus({ status: 'idle', message: '' })
  }

  function removeDraftShift(index) {
    setDraftShifts((current) => current.filter((_, shiftIndex) => shiftIndex !== index))
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
          shifts: draftShifts.map((shift) => ({
            employeeId: shift.employeeId,
            date: shift.date,
            startHour: Number(shift.startHour),
            finishHour: Number(shift.finishHour),
          })),
        }),
      })
      setRoster(payload)
      setEditStatus({ status: 'success', message: 'Roster changes saved and revalidated.' })
      setEditing(false)
    } catch (error) {
      setEditStatus({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  const dates = roster ? weekDates(roster.weekStart) : []
  const shifts = roster?.employees.flatMap((employee) => employee.shifts) || []
  const preferredShiftCount = shifts.filter((shift) => shift.durationHours >= 6 && shift.durationHours <= 8).length
  const selectedCoverage = roster?.coverage?.filter((slot) => slot.date === coverageDay) || []
  const availableEmployeeById = new Map(availableEmployees.map((employee) => [String(employee.id), employee]))
  const groupedRosterEmployees = rosterRoleGroups.map((group) => ({
    ...group,
    employees: (roster?.employees || [])
      .filter((employee) => {
        const employeeDirectoryEntry = availableEmployeeById.get(String(employee.employeeId)) || { roles: ['Driver'] }
        return getRosterRoleGroup(employeeDirectoryEntry).id === group.id
      })
      .sort(compareRosterEmployees),
  }))
  const populatedRosterEmployeeGroups = groupedRosterEmployees.filter((group) => group.employees.length > 0)
  const savedDates = [...new Set([...(initialWeekStart ? [initialWeekStart] : []), ...history.map((item) => item.weekStart)])]
    .sort((left, right) => right.localeCompare(left))

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
        {roster && <button type="button" className="secondary-button" onClick={() => { setEditing((value) => !value); setEditStatus({ status: 'idle', message: '' }) }}>{editing ? 'Close roster editor' : 'Edit roster'}</button>}
      </div>
      {editStatus.message && !editing && <p className={`save-message ${editStatus.status}`} role="status">{editStatus.message}</p>}
      {status === 'loading' && <p className="message info-message" role="status">Loading saved roster…</p>}
      {status === 'empty' && <p className="message info-message">No roster has been saved for this week. Use Generate roster to create one.</p>}
      {status === 'error' && <p className="message error-message" role="alert">The saved roster could not be loaded. Try refreshing.</p>}
      {roster && <>
        <p className="roster-footnote">Week starting {formatDate(roster.weekStart)} · Saved {new Date(roster.updatedAtUtc).toLocaleString()}</p>
        {editing && <form className="roster-edit-panel" onSubmit={saveEditedRoster}>
          <div className="section-heading">
            <div><span className="eyebrow">Administrator</span><h3>Edit generated shifts</h3></div>
            <button type="button" className="secondary-button" onClick={addDraftShift}>Add shift</button>
          </div>
          <p className="demand-help">Change an employee, date or time, add a shift, or remove one. Saving runs the full demand, availability, shift length, start-time, supervision and rest validation again.</p>
          <div className="roster-edit-list">
            {draftShifts.map((shift, index) => {
              const employee = roster.employees.find((item) => String(item.employeeId) === String(shift.employeeId))
              const rosterEmployees = roster.employees.map((item) => ({ id: item.employeeId, firstName: item.employeeName, lastName: '' }))
              const employeeOptions = [...availableEmployees, ...rosterEmployees.filter((item) => !availableEmployees.some((option) => String(option.id) === String(item.id)))]
              const finishDayOffset = Math.max(0, Math.floor(Number(shift.finishHour) / 24))
              return <div className="roster-edit-row" key={`${index}-${shift.employeeId}-${shift.date}`}>
                <label>
                  Employee
                  <select value={shift.employeeId} onChange={(event) => updateDraftShift(index, { employeeId: event.target.value })}>
                    {employeeOptions.map((option) => <option key={option.id} value={option.id}>{option.firstName} {option.lastName}</option>)}
                  </select>
                </label>
                <label>
                  Date
                  <input type="date" value={shift.date} min={roster.weekStart} max={weekDates(roster.weekStart)[6]} onChange={(event) => updateDraftShift(index, { date: event.target.value })} />
                </label>
                <label>
                  Start
                  <input type="time" step="3600" value={hourInputValue(shift.startHour)} onChange={(event) => updateDraftShift(index, { startHour: clockHour(event.target.value) })} />
                </label>
                <label>
                  Finish
                  <span className="roster-edit-finish">
                    <input type="time" step="3600" value={hourInputValue(shift.finishHour)} onChange={(event) => updateDraftShift(index, { finishHour: finishDayOffset * 24 + clockHour(event.target.value) })} />
                    <select value={finishDayOffset} onChange={(event) => updateDraftShift(index, { finishHour: Number(event.target.value) * 24 + (Number(shift.finishHour) % 24) })} aria-label="Finish day offset">
                      <option value="0">Same day</option><option value="1">Next day</option>
                    </select>
                  </span>
                </label>
                <span className="roster-edit-duration">{Math.max(0, Number(shift.finishHour) - Number(shift.startHour))}h{employee ? '' : ' · unknown employee'}</span>
                <button type="button" className="danger-button roster-edit-remove" onClick={() => removeDraftShift(index)}>Remove</button>
              </div>
            })}
            {draftShifts.length === 0 && <p className="message info-message">No shifts. Save to keep the roster empty only when demand is zero.</p>}
          </div>
          <div className="roster-generation-actions">
            <button type="submit" disabled={editStatus.status === 'saving'}>{editStatus.status === 'saving' ? 'Validating and saving…' : 'Save roster changes'}</button>
            <button type="button" className="secondary-button" onClick={() => { setDraftShifts(rosterDraftShifts(roster)); setEditing(false); setEditStatus({ status: 'idle', message: '' }) }}>Cancel</button>
            {editStatus.message && <span className={`save-message ${editStatus.status}`}>{editStatus.message}</span>}
          </div>
        </form>}
        <div className="roster-week-summary roster-result-metrics">
          <Metric label="Demand matched" value={roster.coveragePercent == null ? 'Unverified' : `${formatNumber(roster.coveragePercent)}%`} />
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
        <h3 className="roster-section-title">Employee shifts and target hours</h3>
        <p className="demand-help">Target percentage is scheduled hours divided by target hours. The spread compares employees with positive targets; zero-target employees are reserves with no percentage. +1d means the following calendar day; business days run from 06:00 to 05:59.</p>
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
                  <table className="saved-roster-table">
                    <caption className="visually-hidden">{group.label} roster for the week starting {roster.weekStart}</caption>
                    <thead><tr><th scope="col">Employee</th><th scope="col">Hours / target</th><th scope="col">Target %</th>{dates.map((date) => <th scope="col" key={date}>{formatDate(date)}</th>)}<th scope="col">Shortest rest*</th></tr></thead>
                    <tbody>{group.employees.map((employee) => {
                      const percentage = employee.targetPercentage ?? (employee.targetHours > 0 ? employee.scheduledHours / employee.targetHours * 100 : null)
                      const rest = minimumEmployeeRest(employee.shifts)
                      return <tr key={employee.employeeId}>
                        <th scope="row">{employee.employeeName}</th>
                        <td>{formatNumber(employee.scheduledHours)} / {formatNumber(employee.targetHours)}h
                          <small>{employee.shifts.length ? `Avg ${averageFormatter.format(employee.averageHoursPerShift ?? averageShiftHours(employee.shifts))}h per shift` : 'No shifts'}</small>
                        </td>
                        <td><strong className="roster-target-percentage">{percentage == null ? 'N/A' : `${formatNumber(percentage)}%`}</strong>{percentage == null && <small>Zero target · reserve</small>}</td>
                        {dates.map((date) => <td key={date} className="roster-shift-cell">
                          {employee.shifts.filter((shift) => shift.date === date).map((shift, index) => <span className="roster-shift" key={index}>
                            <span>{shiftTime(shift.startTime, shift.startDayOffset)}–{shiftTime(shift.finishTime, shift.finishDayOffset)}</span><small>{formatNumber(shift.durationHours)}h</small>
                          </span>)}
                          {!employee.shifts.some((shift) => shift.date === date) && <span className="roster-day-off">Off</span>}
                        </td>)}
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
        <div className="section-heading roster-coverage-heading">
          <div><h3>Hourly demand coverage</h3><p className="demand-help">Required drivers and assigned drivers for every hour.</p></div>
          <label className="week-selector">Day<select value={coverageDay} onChange={(event) => setCoverageDay(event.target.value)}>{dates.map((date) => <option value={date} key={date}>{formatDate(date)}</option>)}</select></label>
        </div>
        <div className="roster-coverage-grid">
          {selectedCoverage.map((slot) => <div key={slot.startTime} className={`roster-coverage-slot ${slot.required === slot.scheduled ? 'matched' : 'mismatch'}`}>
            <strong>{shiftTime(slot.startTime, slot.startDayOffset)}</strong><span>{slot.scheduled} / {slot.required} drivers</span>
            <small>{slot.required === slot.scheduled ? slot.required ? 'Matched' : 'No demand' : `${Math.abs(slot.required - slot.scheduled)} ${slot.required > slot.scheduled ? 'missing' : 'extra'}`}</small>
          </div>)}
          {!selectedCoverage.length && <p className="save-message">No hourly coverage snapshot is available for this saved roster.</p>}
        </div>
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
