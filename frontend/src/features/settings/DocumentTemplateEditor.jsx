import { useEffect, useRef, useState } from 'react'
import { fieldSources } from '../../lib/documentSettings'

export function DocumentTemplateEditor({ documentTypeId, fetchJson, onUploaded }) {
  const [template, setTemplate] = useState(null)
  const [state, setState] = useState({ status: 'loading', message: '' })
  const fileRef = useRef(null)
  const busy = state.status === 'saving'

  useEffect(() => {
    let cancelled = false
    fetchJson(`/api/admin/document-types/${documentTypeId}/template`, { cache: 'no-store' })
      .then((loaded) => {
        if (!cancelled) { setTemplate(loaded); setState({ status: 'idle', message: '' }) }
      })
      .catch((error) => {
        if (cancelled) return
        setState(error.status === 404 ? { status: 'idle', message: '' } : { status: 'error', message: error.message })
      })
    return () => { cancelled = true }
  }, [documentTypeId, fetchJson])

  async function upload(event) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    const body = new FormData()
    body.append('file', file)
    setState({ status: 'saving', message: '' })
    try {
      setTemplate(await fetchJson(`/api/admin/document-types/${documentTypeId}/template`, { method: 'POST', body }))
      setState({ status: 'idle', message: 'Form uploaded. Check each field below, then save.' })
      onUploaded()
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  const updateField = (name, change) => setTemplate((current) => ({
    ...current,
    fields: current.fields.map((field) => (field.name === name ? { ...field, ...change } : field)),
  }))

  async function save() {
    setState({ status: 'saving', message: '' })
    try {
      setTemplate(await fetchJson(`/api/admin/document-types/${documentTypeId}/template/fields`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fields: template.fields }),
      }))
      setState({ status: 'idle', message: 'Form fields saved.' })
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  return <div className="document-template">
    <div className="section-heading">
      <div><span className="eyebrow">Form</span><h3>{template?.fileName ?? 'No form uploaded yet'}</h3></div>
      <button type="button" className="secondary-button" onClick={() => fileRef.current.click()} disabled={busy}>
        {template ? 'Replace form' : 'Upload form'}
      </button>
    </div>
    <input ref={fileRef} type="file" accept="application/pdf" hidden onChange={upload} />
    {!template && <p className="holiday-help">Upload the blank form as a fillable PDF. Employees see a Sign button once it is here.</p>}
    {state.status === 'error' && <p className="message error-message" role="alert">{state.message}</p>}
    {state.message && state.status !== 'error' && <p className="save-message success" role="status">{state.message}</p>}
    {template && <>
      <ul className="document-template-fields">
        {template.fields.map((field) => <li key={field.name}>
          <div className="absent-field">
            <label htmlFor={`template-label-${field.name}`}>Label</label>
            <input id={`template-label-${field.name}`} value={field.label} maxLength={100} disabled={busy}
              onChange={(event) => updateField(field.name, { label: event.target.value })} />
          </div>
          <div className="absent-field">
            <label htmlFor={`template-source-${field.name}`}>Filled from</label>
            <select id={`template-source-${field.name}`} value={field.source} disabled={busy}
              onChange={(event) => updateField(field.name, { source: event.target.value })}>
              {fieldSources.map((item) => <option key={item.id} value={item.id}>{item.label}</option>)}
            </select>
          </div>
          <label className="document-type-toggle">
            <input type="checkbox" checked={field.required} disabled={busy}
              onChange={(event) => updateField(field.name, { required: event.target.checked })} />
            <span>Required</span>
          </label>
        </li>)}
      </ul>
      <button type="button" onClick={save} disabled={busy}>{busy ? 'Saving…' : 'Save form fields'}</button>
    </>}
  </div>
}
