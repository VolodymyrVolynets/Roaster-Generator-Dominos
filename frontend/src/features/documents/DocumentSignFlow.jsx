import { useEffect, useRef, useState } from 'react'
import { fetchJson } from '../../api/client'
import { groupSignFields, missingSignFields } from '../../lib/documents'
import { SignaturePad } from './SignaturePad'

export function DocumentSignFlow({ documentTypeId, onFinish }) {
  const [form, setForm] = useState(null)
  const [values, setValues] = useState({})
  const [signed, setSigned] = useState(false)
  const [state, setState] = useState({ status: 'loading', message: '' })
  const padRef = useRef(null)
  const saving = state.status === 'saving'

  useEffect(() => {
    let cancelled = false
    fetchJson(`/api/documents/sign/${documentTypeId}`, { cache: 'no-store' })
      .then((loaded) => {
        if (cancelled) return
        setForm(loaded)
        setValues(Object.fromEntries(loaded.fields.map((field) => [field.name, field.value ?? (field.isCheckbox ? 'false' : '')])))
        setState({ status: 'idle', message: '' })
      })
      .catch((error) => {
        if (!cancelled) setState({ status: 'error', message: error.message })
      })
    return () => { cancelled = true }
  }, [documentTypeId])

  async function save(event) {
    event.preventDefault()
    const missing = missingSignFields(form.fields, values)
    if (missing.length > 0) {
      setState({ status: 'error', message: `Please complete: ${missing.map((field) => field.label).join(', ')}.` })
      return
    }
    setState({ status: 'saving', message: '' })
    try {
      await fetchJson(`/api/documents/sign/${documentTypeId}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ values, signature: padRef.current.toBase64() }),
      })
      onFinish(`${form.documentTypeName} signed.`)
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  return <div className="sheet-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget && !saving) onFinish() }}>
    <section className="sheet document-upload-sheet" role="dialog" aria-modal="true" aria-labelledby="document-sign-title">
      <div className="section-heading">
        <div><span className="eyebrow">Sign</span><h2 id="document-sign-title">{form?.documentTypeName ?? 'Loading…'}</h2></div>
        <button type="button" className="secondary-button" onClick={() => onFinish()} disabled={saving}>Cancel</button>
      </div>
      {state.status === 'loading' && <p className="message info-message" role="status">Loading the form…</p>}
      {state.status === 'error' && <p className="message error-message" role="alert">{state.message}</p>}
      {form && <form className="document-review" onSubmit={save} noValidate>
        <a className="table-link" href={`/api/documents/sign/${documentTypeId}/blank-form`} target="_blank" rel="noreferrer">Read the full form</a>
        <fieldset className="absent-fields document-review-fields" disabled={saving}>
          {groupSignFields(form.fields).map((group) => (group.kind === 'question'
            ? <div className="absent-field document-note" key={group.key} role="group" aria-label={group.label}>
                <span className="absent-field-label">{group.label}</span>
                <div className="document-type-actions">
                  {[[group.yes, 'Yes', group.no], [group.no, 'No', group.yes]].map(([field, label, other]) => <label key={field.name} className="document-type-toggle">
                    <input type="radio" name={group.key} checked={values[field.name] === 'true'}
                      onChange={() => setValues((current) => ({ ...current, [field.name]: 'true', [other.name]: 'false' }))} />
                    <span>{label}</span>
                  </label>)}
                </div>
              </div>
            : group.field.isCheckbox
              ? <label key={group.key} className="document-type-toggle document-note">
                  <input type="checkbox" checked={values[group.field.name] === 'true'}
                    onChange={(event) => setValues((current) => ({ ...current, [group.field.name]: String(event.target.checked) }))} />
                  <span>{group.field.label}</span>
                </label>
              : <div className="absent-field" key={group.key}>
                  <label htmlFor={`sign-${group.field.name}`}>{group.field.label}{group.field.required ? '' : ' (optional)'}</label>
                  <input id={`sign-${group.field.name}`} value={values[group.field.name] ?? ''} readOnly={group.field.readOnly}
                    onChange={(event) => setValues((current) => ({ ...current, [group.field.name]: event.target.value }))} />
                </div>))}
        </fieldset>
        <SignaturePad ref={padRef} disabled={saving} onChange={setSigned} />
        <button type="submit" disabled={!signed || saving}>{saving ? 'Saving…' : 'Sign and save'}</button>
      </form>}
    </section>
  </div>
}
