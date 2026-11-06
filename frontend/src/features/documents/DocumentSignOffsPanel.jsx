import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchJson } from '../../api/client'
import { confirmAction } from '../../components/ConfirmDialog'
import { formatHolidayDate } from '../../lib/format'
import { SignaturePad } from './SignaturePad'

export function DocumentSignOffsPanel({ realtimeKey = 0, ownEmployeeName, isAdmin = false }) {
  const [state, setState] = useState({ status: 'loading', items: [], message: '' })
  const [openId, setOpenId] = useState(null)
  const [notice, setNotice] = useState('')
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    let cancelled = false
    fetchJson('/api/admin/document-signoffs', { cache: 'no-store' })
      .then((items) => {
        if (!cancelled) setState({ status: 'success', items, message: '' })
      })
      .catch((error) => {
        if (!cancelled) setState((current) => ({ ...current, status: 'error', message: error.message }))
      })
    return () => { cancelled = true }
  }, [realtimeKey, refresh])

  const finish = useCallback((message) => {
    setOpenId(null)
    if (message) {
      setNotice(message)
      setRefresh((current) => current + 1)
    }
  }, [])

  return <section className="admin-tools role-details documents-panel">
    <div className="section-heading">
      <div><span className="eyebrow">Documents</span><h2>Forms to sign</h2></div>
    </div>
    {state.status === 'loading' && <p className="message info-message" role="status">Loading forms…</p>}
    {state.status === 'error' && <p className="message error-message" role="alert">{state.message}</p>}
    {notice && <p className="save-message success" role="status">{notice}</p>}
    {isAdmin && <AutomaticSignature onSaved={setNotice} refreshKey={refresh} />}
    {state.status === 'success' && state.items.length === 0 && <p className="holiday-help">No forms are waiting for your signature.</p>}
    <ul className="documents-list">
      {state.items.map((item) => <li key={item.documentId}>
        <div className="documents-row-text">
          <strong>{item.documentTypeName}</strong>
          <span>{item.employeeName}</span>
          <small>Signed by the employee {formatHolidayDate(item.signedAtUtc)}</small>
        </div>
        {item.employeeName === ownEmployeeName
          ? <small>Another manager has to sign this</small>
          : <button type="button" onClick={() => { setNotice(''); setOpenId(item.documentId) }}
              aria-label={`Sign ${item.documentTypeName} for ${item.employeeName}`}>Sign</button>}
      </li>)}
    </ul>
    {openId && <SignOffSheet documentId={openId} onFinish={finish} />}
  </section>
}

function AutomaticSignature({ onSaved, refreshKey }) {
  const [saved, setSaved] = useState(null)
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState('')
  const [signed, setSigned] = useState(false)
  const [state, setState] = useState({ busy: false, message: '' })
  const padRef = useRef(null)
  const url = '/api/admin/document-signoffs/automatic-signature'

  useEffect(() => {
    let cancelled = false
    fetchJson(url, { cache: 'no-store' })
      .then((value) => { if (!cancelled) setSaved(value) })
      .catch((error) => { if (!cancelled) setState({ busy: false, message: error.message }) })
    return () => { cancelled = true }
  }, [refreshKey])

  async function save(event) {
    event.preventDefault()
    setState({ busy: true, message: '' })
    try {
      const value = await fetchJson(url, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ managerName: name, signature: padRef.current.toBase64() }),
      })
      setSaved(value)
      setEditing(false)
      setSigned(false)
      setState({ busy: false, message: '' })
      onSaved('Your signature will now be added automatically.')
    } catch (error) {
      setState({ busy: false, message: error.message })
    }
  }

  async function remove() {
    if (!(await confirmAction({
      title: 'Stop automatic signing?',
      message: 'Forms will wait here for a manager to sign them.',
      confirmLabel: 'Stop signing',
      destructive: true,
    }))) return
    setState({ busy: true, message: '' })
    try {
      await fetchJson(url, { method: 'DELETE' })
      setSaved(null)
      setState({ busy: false, message: '' })
    } catch (error) {
      setState({ busy: false, message: error.message })
    }
  }

  return <div className="document-vehicle" role="group" aria-label="Automatic signature">
    <div className="documents-row-text">
      <strong>{saved ? `Signing automatically as ${saved.managerName}` : 'Automatic signature is off'}</strong>
      <small>{saved
        ? `Saved ${formatHolidayDate(saved.savedAtUtc)}. Added to forms as soon as the employee signs.`
        : 'Save your signature once and it is added to forms as soon as the employee signs.'}</small>
    </div>
    <div className="document-type-actions">
      <button type="button" className="secondary-button" onClick={() => setEditing((current) => !current)} disabled={state.busy}>
        {saved ? 'Replace signature' : 'Set up'}
      </button>
      {saved && <button type="button" className="secondary-button" onClick={remove} disabled={state.busy}>Stop</button>}
    </div>
    {state.message && <p className="message error-message" role="alert">{state.message}</p>}
    {editing && <form className="document-review document-note-wide" onSubmit={save}>
      <div className="absent-field">
        <label htmlFor="auto-signature-name">Name printed on forms</label>
        <input id="auto-signature-name" value={name} maxLength={200} required onChange={(event) => setName(event.target.value)} />
      </div>
      <SignaturePad ref={padRef} disabled={state.busy} onChange={setSigned} />
      <button type="submit" disabled={!signed || state.busy}>{state.busy ? 'Saving…' : 'Save signature'}</button>
    </form>}
  </div>
}

function SignOffSheet({ documentId, onFinish }) {
  const [form, setForm] = useState(null)
  const [managerName, setManagerName] = useState('')
  const [signed, setSigned] = useState(false)
  const [state, setState] = useState({ status: 'loading', message: '' })
  const padRef = useRef(null)
  const saving = state.status === 'saving'
  const url = `/api/admin/document-signoffs/${documentId}`

  useEffect(() => {
    let cancelled = false
    fetchJson(url, { cache: 'no-store' })
      .then((loaded) => {
        if (cancelled) return
        setForm(loaded)
        setManagerName(loaded.managerName)
        setState({ status: 'idle', message: '' })
      })
      .catch((error) => {
        if (!cancelled) setState({ status: 'error', message: error.message })
      })
    return () => { cancelled = true }
  }, [url])

  async function save(event) {
    event.preventDefault()
    setState({ status: 'saving', message: '' })
    try {
      await fetchJson(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ managerName, signature: padRef.current.toBase64() }),
      })
      onFinish(`${form.documentTypeName} signed for ${form.employeeName}.`)
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  return <div className="sheet-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget && !saving) onFinish() }}>
    <section className="sheet document-upload-sheet" role="dialog" aria-modal="true" aria-labelledby="signoff-title">
      <div className="section-heading">
        <div><span className="eyebrow">{form?.employeeName ?? 'Sign'}</span><h2 id="signoff-title">{form?.documentTypeName ?? 'Loading…'}</h2></div>
        <button type="button" className="secondary-button" onClick={() => onFinish()} disabled={saving}>Cancel</button>
      </div>
      {state.status === 'loading' && <p className="message info-message" role="status">Loading the form…</p>}
      {state.status === 'error' && <p className="message error-message" role="alert">{state.message}</p>}
      {form && <form className="document-review" onSubmit={save}>
        <a className="secondary-button document-link-button" href={`${url}/form`} target="_blank" rel="noreferrer">Read the completed form</a>
        {form.asksForManagerName && <div className="absent-field">
          <label htmlFor="signoff-name">Your name</label>
          <input id="signoff-name" value={managerName} maxLength={200} required onChange={(event) => setManagerName(event.target.value)} />
        </div>}
        <SignaturePad ref={padRef} disabled={saving} onChange={setSigned} />
        <div className="document-sheet-actions">
          <button type="submit" disabled={!signed || saving}>{saving ? 'Saving…' : 'Sign and save'}</button>
        </div>
      </form>}
    </section>
  </div>
}
