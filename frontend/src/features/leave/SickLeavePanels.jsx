import { useEffect, useRef, useState } from 'react'
import { fetchJson } from '../../api/client'
import { formatDateOnly, formatHolidayDate } from '../../lib/format'
import { DatePicker } from '../../components/DatePicker'

function SickLeaveTable({ requests, showEmployee = false, management = false, onReview, busyId }) {
  return (
    <div className="holiday-table-wrapper">
      <table className="holiday-table sick-leave-table">
        <thead><tr>{showEmployee && <th>Employee</th>}<th>Start</th><th>Finish</th><th>Status</th><th>Sick note</th><th>Reviewed</th>{(management || !showEmployee) && <th>Action</th>}</tr></thead>
        <tbody>{requests.map((request) => (
          <tr key={request.id}>
            {showEmployee && <td className="cell-primary" data-label="Employee"><strong>{request.employeeName}</strong><small>#{request.employeeNumber}</small></td>}
            <td data-label="Start">{formatDateOnly(request.startDate)}</td><td data-label="Finish">{formatDateOnly(request.finishDate)}</td>
            <td data-label="Status"><span className={`holiday-status ${request.status.toLowerCase()}`}>{request.status}</span></td>
            <td data-label="Sick note">{request.hasAttachment ? <a className="table-link" href={`${management ? '/api/admin' : '/api'}/sick-leave/${request.id}/attachment`} target="_blank" rel="noreferrer">View note</a> : <span className="deleted-note">Deleted after review</span>}</td>
            <td data-label="Reviewed">{request.reviewedAtUtc ? formatHolidayDate(request.reviewedAtUtc) : '—'}{request.reviewedByName && <small>by {request.reviewedByName}</small>}{request.rejectionReason && <small>{request.rejectionReason}</small>}</td>
            {management && <td className="sick-leave-actions cell-actions"><button type="button" onClick={() => onReview(request.id, 'approve')} disabled={busyId === request.id}>Approve</button><button type="button" className="danger-button" onClick={() => onReview(request.id, 'reject')} disabled={busyId === request.id}>Reject</button></td>}
            {!management && !showEmployee && <td className="cell-actions">{request.status === 'Requested' && <button type="button" className="danger-button" onClick={() => onReview(request.id)} disabled={busyId === request.id}>Remove</button>}</td>}
          </tr>
        ))}</tbody>
      </table>
    </div>
  )
}

export function SickLeavePanel({ setErrorPopup, realtimeKey = 0 }) {
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
  useEffect(() => { void load() }, [realtimeKey])

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
            <DatePicker id="sick-start" required value={form.startDate} onChange={(value) => setForm((current) => ({ ...current, startDate: value }))} />
          </div>
          <div className="sick-leave-field">
            <label htmlFor="sick-finish">Finish date</label>
            <DatePicker id="sick-finish" min={form.startDate || undefined} required value={form.finishDate} onChange={(value) => setForm((current) => ({ ...current, finishDate: value }))} />
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

export function AdminSickLeavePanel({ setErrorPopup, includeOwnRequest = false, realtimeKey = 0 }) {
  const [state, setState] = useState({ status: 'loading', requested: [], reviewed: [] })
  const [action, setAction] = useState({ status: 'idle', message: '', busyId: null })
  async function load() {
    try { const payload = await fetchJson('/api/admin/sick-leave'); setState({ status: 'success', requested: payload.requested || [], reviewed: payload.reviewed || [] }) }
    catch (error) { setState((current) => ({ ...current, status: 'error' })); setErrorPopup(error.message) }
  }
  useEffect(() => { void load() }, [realtimeKey])
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
      {includeOwnRequest && <SickLeavePanel setErrorPopup={setErrorPopup} realtimeKey={realtimeKey} />}
    </>
  )
}
