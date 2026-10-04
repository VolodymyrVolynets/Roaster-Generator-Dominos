import { useEffect, useRef, useState } from 'react'

const emptyForm = {
  shiftEntryMode: 'saved',
  savedRosterShiftId: '',
  shiftDate: '',
  shiftStartTime: '',
  shiftFinishTime: '',
  notificationDate: '',
  notificationTime: '',
  notificationMethod: '',
  cancellationReason: '',
}
const emptyFilters = { shiftStartDate: '', shiftFinishDate: '', employeeName: '' }
const wholeHours = Array.from({ length: 24 }, (_, hour) => `${String(hour).padStart(2, '0')}:00`)
const dateFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' })
const shopDateFormatter = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Dublin', dateStyle: 'short' })

function formatDate(value) {
  const [year, month, day] = value.split('-').map(Number)
  return dateFormatter.format(new Date(year, month - 1, day))
}

function shiftLabel(shift) {
  const daySuffix = (offset) => offset > 1 ? ` ${offset} days later` : offset ? ' next day' : ''
  return `${formatDate(shift.date)} · ${shift.startTime}${daySuffix(shift.startDayOffset)} – ${shift.finishTime}${daySuffix(shift.finishDayOffset)}`
}

function HourSelect({ id, value, onChange, required = true, disabled = false }) {
  return <select id={id} value={value} onChange={(event) => onChange(event.target.value)} required={required} disabled={disabled}>
    <option value="">Select hour</option>
    {wholeHours.map((hour) => <option key={hour} value={hour}>{hour}</option>)}
  </select>
}

function FormHistory({
  forms,
  management = false,
  canEdit = false,
  shifts = [],
  editingForm,
  busyId,
  onBeginEdit,
  onChangeEdit,
  onCancelEdit,
  onSaveEdit,
  onDelete,
}) {
  return <div className="absent-history-list">
    {forms.map((form) => <article className="absent-record" key={form.id}>
      <div className="section-heading">
        <h4>{shiftLabel(form.shift)}</h4>
        <div className="absent-record-actions">
          <span className="role-badge">Submitted</span>
          {management && <a className="secondary-button" href={`/api/admin/absent/${form.id}/pdf`} download>
            Download PDF
          </a>}
          {canEdit && editingForm?.id !== form.id && <>
            <button type="button" className="secondary-button" onClick={() => onBeginEdit(form)} disabled={Boolean(busyId)}>Edit</button>
            <button type="button" className="danger-button" onClick={() => onDelete(form)} disabled={Boolean(busyId)}>Remove</button>
          </>}
        </div>
      </div>
      {editingForm?.id === form.id ? <form className="absent-admin-edit" onSubmit={onSaveEdit}>
        <fieldset className="absent-fields" disabled={busyId === form.id}>
          <div className="absent-field absent-wide">
            <label htmlFor={`absent-edit-shift-${form.id}`}>Shift cancelled</label>
            <select id={`absent-edit-shift-${form.id}`} value={editingForm.shiftEntryMode === 'saved' ? `saved:${editingForm.savedRosterShiftId}` : editingForm.shiftEntryMode} onChange={(event) => onChangeEdit('shiftChoice', event.target.value)}>
              <option value="keep">Keep recorded shift — {shiftLabel(form.shift)}</option>
              {shifts.filter((shift) => shift.id !== form.shift.id).map((shift) => <option key={shift.id} value={`saved:${shift.id}`}>Change to — {shiftLabel(shift)}</option>)}
              <option value="manual">Enter a different shift manually</option>
            </select>
            <small>Keeping the recorded shift remains available even if the roster has since changed.</small>
          </div>
          {editingForm.shiftEntryMode === 'manual' && <>
            <div className="absent-field absent-wide">
              <label htmlFor={`absent-edit-shift-date-${form.id}`}>Shift date</label>
              <input id={`absent-edit-shift-date-${form.id}`} type="date" value={editingForm.shiftDate} onChange={(event) => onChangeEdit('shiftDate', event.target.value)} required />
            </div>
            <div className="absent-field">
              <label htmlFor={`absent-edit-shift-start-${form.id}`}>Shift start hour</label>
              <HourSelect id={`absent-edit-shift-start-${form.id}`} value={editingForm.shiftStartTime} onChange={(value) => onChangeEdit('shiftStartTime', value)} />
            </div>
            <div className="absent-field">
              <label htmlFor={`absent-edit-shift-finish-${form.id}`}>Shift finish hour</label>
              <HourSelect id={`absent-edit-shift-finish-${form.id}`} value={editingForm.shiftFinishTime} onChange={(value) => onChangeEdit('shiftFinishTime', value)} />
            </div>
          </>}
          <div className="absent-field">
            <label htmlFor={`absent-edit-date-${form.id}`}>Notification date</label>
            <input id={`absent-edit-date-${form.id}`} type="date" value={editingForm.notificationDate} onChange={(event) => onChangeEdit('notificationDate', event.target.value)} required />
          </div>
          <div className="absent-field">
            <label htmlFor={`absent-edit-time-${form.id}`}>Notification time</label>
            <HourSelect id={`absent-edit-time-${form.id}`} value={editingForm.notificationTime} onChange={(value) => onChangeEdit('notificationTime', value)} />
          </div>
          <div className="absent-field absent-wide">
            <label htmlFor={`absent-edit-method-${form.id}`}>Method of notification</label>
            <input id={`absent-edit-method-${form.id}`} value={editingForm.notificationMethod} onChange={(event) => onChangeEdit('notificationMethod', event.target.value)} maxLength={100} required />
          </div>
          <div className="absent-field absent-wide">
            <label htmlFor={`absent-edit-reason-${form.id}`}>Reason for cancellation</label>
            <textarea id={`absent-edit-reason-${form.id}`} value={editingForm.cancellationReason} onChange={(event) => onChangeEdit('cancellationReason', event.target.value)} rows={4} maxLength={2000} required />
          </div>
        </fieldset>
        <div className="absent-form-footer absent-edit-footer">
          <span>Driver identity remains unchanged.</span>
          <div className="absent-edit-actions">
            <button type="button" className="secondary-button" onClick={onCancelEdit} disabled={busyId === form.id}>Cancel</button>
            <button type="submit" disabled={busyId === form.id || !editingForm.notificationDate || !editingForm.notificationTime || !editingForm.notificationMethod.trim() || !editingForm.cancellationReason.trim() || (editingForm.shiftEntryMode === 'manual' && (!editingForm.shiftDate || !editingForm.shiftStartTime || !editingForm.shiftFinishTime || editingForm.shiftStartTime === editingForm.shiftFinishTime))}>{busyId === form.id ? 'Saving…' : 'Save changes'}</button>
          </div>
        </div>
      </form> : <dl className="absent-record-details">
          <div><dt>Driver’s full name</dt><dd>{form.driverFullName}</dd></div>
          <div><dt>Payroll number</dt><dd>{form.payrollNumber || 'Not set'}</dd></div>
          <div><dt>Notification date and time</dt><dd>{formatDate(form.notificationDate)} · {form.notificationTime}</dd></div>
          <div><dt>Method of notification</dt><dd>{form.notificationMethod}</dd></div>
          <div className="absent-wide"><dt>Reason for cancellation</dt><dd>{form.cancellationReason}</dd></div>
        </dl>}
    </article>)}
  </div>
}

export function AbsentPanel({ admin = false, management = admin, fetchJson, realtimeKey = 0 }) {
  const [data, setData] = useState(null)
  const [loadState, setLoadState] = useState({ status: 'loading', message: '' })
  const [form, setForm] = useState(emptyForm)
  const [action, setAction] = useState({ status: 'idle', message: '' })
  const [editingForm, setEditingForm] = useState(null)
  const [filters, setFilters] = useState(emptyFilters)
  const [appliedFilters, setAppliedFilters] = useState(emptyFilters)
  const [refresh, setRefresh] = useState(0)
  const [today, setToday] = useState(() => shopDateFormatter.format(new Date()))
  const submitting = useRef(false)
  const loadVersion = useRef(0)

  useEffect(() => {
    if (management) return undefined
    // Refresh the server-provided date if the form stays open across Dublin midnight.
    const timer = window.setInterval(() => setToday(shopDateFormatter.format(new Date())), 30000)
    return () => window.clearInterval(timer)
  }, [management])

  useEffect(() => {
    let cancelled = false
    const version = ++loadVersion.current
    setLoadState({ status: 'loading', message: '' })
    const query = new URLSearchParams()
    if (management) {
      if (appliedFilters.shiftStartDate) query.set('shiftStartDate', appliedFilters.shiftStartDate)
      if (appliedFilters.shiftFinishDate) query.set('shiftFinishDate', appliedFilters.shiftFinishDate)
      if (appliedFilters.employeeName.trim()) query.set('employeeName', appliedFilters.employeeName.trim())
    }
    const endpoint = management ? '/api/admin/absent' : '/api/absent'
    fetchJson(query.size ? `${endpoint}?${query}` : endpoint)
      .then((payload) => {
        if (cancelled || version !== loadVersion.current) return
        setData(payload)
        if (!management) {
          setForm((current) => current.notificationDate
            ? current
            : { ...current, notificationDate: payload.notificationDate })
        }
        setLoadState({ status: 'success', message: '' })
      })
      .catch((error) => {
        if (cancelled || version !== loadVersion.current) return
        setLoadState({ status: 'error', message: error.message })
      })
    return () => { cancelled = true }
  }, [appliedFilters, fetchJson, management, realtimeKey, refresh, today])

  function update(field, value) {
    setForm((current) => ({ ...current, [field]: value }))
    setAction({ status: 'idle', message: '' })
  }

  function updateFilter(field, value) {
    setFilters((current) => ({ ...current, [field]: value }))
  }

  function applyFilters(event) {
    event.preventDefault()
    setAppliedFilters({
      shiftStartDate: filters.shiftStartDate,
      shiftFinishDate: filters.shiftFinishDate,
      employeeName: filters.employeeName.trim(),
    })
  }

  function clearFilters() {
    setFilters(emptyFilters)
    setAppliedFilters(emptyFilters)
  }

  async function submit(event) {
    event.preventDefault()
    if (submitting.current) return
    submitting.current = true
    setAction({ status: 'saving', message: '' })
    try {
      await fetchJson('/api/absent', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          savedRosterShiftId: form.shiftEntryMode === 'saved' ? form.savedRosterShiftId : null,
          shiftDate: form.shiftEntryMode === 'manual' ? form.shiftDate : null,
          shiftStartTime: form.shiftEntryMode === 'manual' ? form.shiftStartTime : null,
          shiftFinishTime: form.shiftEntryMode === 'manual' ? form.shiftFinishTime : null,
          notificationDate: form.notificationDate,
          notificationTime: form.notificationTime,
          notificationMethod: form.notificationMethod,
          cancellationReason: form.cancellationReason,
        }),
      })
      ++loadVersion.current
      setForm({ ...emptyForm, notificationDate: data?.notificationDate || '' })
      setAction({ status: 'success', message: 'Absent form submitted. You can submit another form whenever needed.' })
      setRefresh((current) => current + 1)
    } catch (error) {
      setAction({ status: 'error', message: error.message })
      if (error.fieldErrors?.savedRosterShiftId) setRefresh((current) => current + 1)
    } finally {
      submitting.current = false
    }
  }

  function beginEdit(item) {
    setEditingForm({
      id: item.id,
      shiftEntryMode: 'keep',
      savedRosterShiftId: '',
      shiftDate: item.shift.date,
      shiftStartTime: item.shift.startTime,
      shiftFinishTime: item.shift.finishTime,
      notificationDate: item.notificationDate,
      notificationTime: item.notificationTime,
      notificationMethod: item.notificationMethod,
      cancellationReason: item.cancellationReason,
    })
    setAction({ status: 'idle', message: '', busyId: null })
  }

  function changeEdit(field, value) {
    if (field === 'shiftChoice') {
      setEditingForm((current) => value.startsWith('saved:')
        ? { ...current, shiftEntryMode: 'saved', savedRosterShiftId: value.slice(6) }
        : { ...current, shiftEntryMode: value, savedRosterShiftId: '' })
      setAction({ status: 'idle', message: '', busyId: null })
      return
    }
    setEditingForm((current) => ({ ...current, [field]: value }))
    setAction({ status: 'idle', message: '', busyId: null })
  }

  async function saveEdit(event) {
    event.preventDefault()
    if (!editingForm || submitting.current) return
    submitting.current = true
    setAction({ status: 'saving', message: '', busyId: editingForm.id })
    try {
      await fetchJson(`/api/admin/absent/${editingForm.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          savedRosterShiftId: editingForm.shiftEntryMode === 'saved' ? editingForm.savedRosterShiftId : null,
          shiftDate: editingForm.shiftEntryMode === 'manual' ? editingForm.shiftDate : null,
          shiftStartTime: editingForm.shiftEntryMode === 'manual' ? editingForm.shiftStartTime : null,
          shiftFinishTime: editingForm.shiftEntryMode === 'manual' ? editingForm.shiftFinishTime : null,
          notificationDate: editingForm.notificationDate,
          notificationTime: editingForm.notificationTime,
          notificationMethod: editingForm.notificationMethod,
          cancellationReason: editingForm.cancellationReason,
        }),
      })
      setEditingForm(null)
      setAction({ status: 'success', message: 'Absent form updated.', busyId: null })
      setRefresh((current) => current + 1)
    } catch (error) {
      setAction({ status: 'error', message: error.message, busyId: null })
    } finally {
      submitting.current = false
    }
  }

  async function removeForm(item) {
    if (submitting.current || !window.confirm(`Remove the absent form submitted by ${item.driverFullName}?`)) return
    submitting.current = true
    setAction({ status: 'deleting', message: '', busyId: item.id })
    try {
      await fetchJson(`/api/admin/absent/${item.id}`, { method: 'DELETE' })
      if (editingForm?.id === item.id) setEditingForm(null)
      setAction({ status: 'success', message: 'Absent form removed.', busyId: null })
      setRefresh((current) => current + 1)
    } catch (error) {
      setAction({ status: 'error', message: error.message, busyId: null })
    } finally {
      submitting.current = false
    }
  }

  const shifts = management ? [] : data?.shifts || []
  const selectedShiftMissing = form.savedRosterShiftId && !shifts.some((shift) => shift.id === form.savedRosterShiftId)
  const manualShiftComplete = form.shiftDate && form.shiftStartTime && form.shiftFinishTime && form.shiftStartTime !== form.shiftFinishTime
  const shiftComplete = form.shiftEntryMode === 'manual' ? manualShiftComplete : form.savedRosterShiftId && !selectedShiftMissing
  const hasActiveFilters = Object.values(appliedFilters).some((value) => value)
  const visibleFormCount = data?.reduce((count, group) => count + group.forms.length, 0) ?? 0

  return <section className="holiday-panel role-details absent-panel">
    <div className="section-heading">
      <div><span className="eyebrow">Absent</span><h2>{management ? 'Driver absent forms' : 'Submit an absent form'}</h2></div>
      <button type="button" className="secondary-button" onClick={() => setRefresh((current) => current + 1)} disabled={loadState.status === 'loading' || Boolean(action.busyId)}>Refresh</button>
    </div>
    <p className="holiday-help">{management
      ? 'Forms are grouped by driver, with the newest notification dates first. Downloaded PDFs include the signed-in admin or manager name. No approval is required.'
      : 'Record a shift cancellation by choosing a saved roster shift or entering the shift yourself. Forms are saved immediately, and you can submit more than one.'}</p>
    {loadState.status === 'loading' && <p className="message info-message" role="status">Loading absent forms…</p>}
    {loadState.status === 'error' && <p className="message error-message" role="alert">{loadState.message}</p>}
    {admin && action.message && <p className={`save-message ${action.status}`} role={action.status === 'error' ? 'alert' : 'status'}>{action.message}</p>}

    {management && <form className="absent-filter-form" onSubmit={applyFilters}>
      <div className="absent-filter-fields">
        <div className="absent-filter-field">
          <label htmlFor="absent-filter-start-date">Shift start date</label>
          <input id="absent-filter-start-date" type="date" value={filters.shiftStartDate} max={filters.shiftFinishDate || undefined} onChange={(event) => updateFilter('shiftStartDate', event.target.value)} />
        </div>
        <div className="absent-filter-field">
          <label htmlFor="absent-filter-finish-date">Shift finish date</label>
          <input id="absent-filter-finish-date" type="date" value={filters.shiftFinishDate} min={filters.shiftStartDate || undefined} onChange={(event) => updateFilter('shiftFinishDate', event.target.value)} />
        </div>
        <div className="absent-filter-field absent-filter-name">
          <label htmlFor="absent-filter-employee">Employee name</label>
          <input id="absent-filter-employee" value={filters.employeeName} onChange={(event) => updateFilter('employeeName', event.target.value)} placeholder="Search by name" maxLength={201} />
        </div>
      </div>
      <div className="absent-filter-actions">
        <button type="submit" disabled={loadState.status === 'loading'}>Apply filters</button>
        <button type="button" className="secondary-button" onClick={clearFilters} disabled={!hasActiveFilters && !Object.values(filters).some((value) => value)}>Clear</button>
        {hasActiveFilters && data && <span className="absent-filter-count" role="status">{visibleFormCount} {visibleFormCount === 1 ? 'form' : 'forms'} shown</span>}
      </div>
    </form>}

    {!management && data && <>
      <form onSubmit={submit}>
        <fieldset className="absent-fields" disabled={action.status === 'saving'}>
          <div className="absent-field">
            <label htmlFor="absent-name">Driver’s full name</label>
            <input id="absent-name" value={data.driverFullName} readOnly />
          </div>
          <div className="absent-field">
            <label htmlFor="absent-payroll">Payroll number</label>
            <input id="absent-payroll" value={data.payrollNumber || ''} placeholder="Not set" readOnly />
          </div>
          <div className="absent-field absent-wide">
            <span className="absent-field-label">Shift cancelled</span>
            <div className="absent-shift-mode" role="radiogroup" aria-label="How to enter the cancelled shift">
              <button type="button" role="radio" aria-checked={form.shiftEntryMode === 'saved'} className={form.shiftEntryMode === 'saved' ? 'active' : ''} onClick={() => update('shiftEntryMode', 'saved')}>Choose saved shift</button>
              <button type="button" role="radio" aria-checked={form.shiftEntryMode === 'manual'} className={form.shiftEntryMode === 'manual' ? 'active' : ''} onClick={() => update('shiftEntryMode', 'manual')}>Enter shift manually</button>
            </div>
          </div>
          {form.shiftEntryMode === 'saved' ? <div className="absent-field absent-wide">
            <label htmlFor="absent-shift">Saved shift</label>
            <select id="absent-shift" value={selectedShiftMissing ? '' : form.savedRosterShiftId} onChange={(event) => update('savedRosterShiftId', event.target.value)} required disabled={!shifts.length}>
              <option value="">Select a saved roster shift</option>
              {shifts.map((shift) => <option key={shift.id} value={shift.id}>{shiftLabel(shift)}</option>)}
            </select>
            {!shifts.length && <small>No saved driver shifts are available. Choose “Enter shift manually” above.</small>}
            {selectedShiftMissing && <small role="alert">The selected shift has changed. Select a current saved shift or enter it manually.</small>}
          </div> : <>
            <div className="absent-field absent-wide">
              <label htmlFor="absent-shift-date">Shift date</label>
              <input id="absent-shift-date" type="date" value={form.shiftDate} onChange={(event) => update('shiftDate', event.target.value)} required />
            </div>
            <div className="absent-field">
              <label htmlFor="absent-shift-start">Shift start hour</label>
              <HourSelect id="absent-shift-start" value={form.shiftStartTime} onChange={(value) => update('shiftStartTime', value)} />
            </div>
            <div className="absent-field">
              <label htmlFor="absent-shift-finish">Shift finish hour</label>
              <HourSelect id="absent-shift-finish" value={form.shiftFinishTime} onChange={(value) => update('shiftFinishTime', value)} />
              {form.shiftStartTime && form.shiftFinishTime === form.shiftStartTime && <small role="alert">Start and finish hours must be different.</small>}
            </div>
          </>}
          <div className="absent-field">
            <label htmlFor="absent-date">Notification date</label>
            <input id="absent-date" type="date" value={form.notificationDate} onChange={(event) => update('notificationDate', event.target.value)} required aria-describedby="absent-date-help" />
            <small id="absent-date-help">Defaults to today in Dublin and can be corrected.</small>
          </div>
          <div className="absent-field">
            <label htmlFor="absent-time">Notification hour (24-hour)</label>
            <HourSelect id="absent-time" value={form.notificationTime} onChange={(value) => update('notificationTime', value)} />
          </div>
          <div className="absent-field absent-wide">
            <label htmlFor="absent-method">Method of notification</label>
            <input id="absent-method" value={form.notificationMethod} onChange={(event) => update('notificationMethod', event.target.value)} placeholder="e.g. Phone call, text message, WhatsApp, in person" maxLength={100} required />
          </div>
          <div className="absent-field absent-wide">
            <label htmlFor="absent-reason">Reason for cancellation</label>
            <textarea id="absent-reason" value={form.cancellationReason} onChange={(event) => update('cancellationReason', event.target.value)} rows={4} maxLength={2000} required />
          </div>
        </fieldset>
        <div className="absent-form-footer">
          <span>No approval required.</span>
          <button type="submit" disabled={action.status === 'saving' || loadState.status !== 'success' || !shiftComplete || !form.notificationDate || !form.notificationTime || !form.notificationMethod.trim() || !form.cancellationReason.trim()}>
            {action.status === 'saving' ? 'Submitting…' : 'Submit absent form'}
          </button>
        </div>
      </form>
      {action.message && <p className={`save-message ${action.status}`} role={action.status === 'error' ? 'alert' : 'status'}>{action.message}</p>}
      <section className="holiday-history">
        <div className="section-heading"><h3>Your submitted forms</h3><span>{data.forms.length} {data.forms.length === 1 ? 'form' : 'forms'}</span></div>
        <p className="holiday-help">Newest notification dates first.</p>
        {data.forms.length ? <FormHistory forms={data.forms} /> : <p className="message info-message">You haven’t submitted any absent forms yet.</p>}
      </section>
    </>}

    {management && data && (data.length ? data.map((group) => <section className="holiday-history" key={group.employeeId}>
      <div className="section-heading"><h3>{group.driverFullName}</h3><span>{group.forms.length} {group.forms.length === 1 ? 'form' : 'forms'}</span></div>
      <FormHistory
        forms={group.forms}
        management
        canEdit={admin}
        shifts={group.shifts || []}
        editingForm={editingForm}
        busyId={action.busyId}
        onBeginEdit={beginEdit}
        onChangeEdit={changeEdit}
        onCancelEdit={() => setEditingForm(null)}
        onSaveEdit={saveEdit}
        onDelete={removeForm}
      />
    </section>) : <p className="message info-message">{hasActiveFilters ? 'No absent forms match these filters.' : 'No absent forms have been submitted yet.'}</p>)}
  </section>
}
