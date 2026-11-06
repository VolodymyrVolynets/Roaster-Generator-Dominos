import { useEffect, useRef, useState } from 'react'
import { fetchJson } from '../../api/client'
import { answerAllHealthQuestions, groupSignFields, hasUnansweredHealthQuestion, missingSignFields } from '../../lib/documents'
import { SignaturePad } from './SignaturePad'

export function DocumentSignFlow({ documentTypeId, onFinish }) {
  const [form, setForm] = useState(null)
  const [values, setValues] = useState({})
  const [signed, setSigned] = useState(false)
  const [state, setState] = useState({ status: 'loading', message: '' })
  const [healthChoice, setHealthChoice] = useState('')
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

  function renderQuestion(group) {
    return <div className="document-question document-note" key={group.key} role="radiogroup" aria-label={group.label}>
      <span className="document-question-label">{group.label}</span>
      <div className="document-question-options">
        {[[group.yes, 'Yes', group.no], [group.no, 'No', group.yes]].map(([field, label, other]) => <label key={field.name} className="document-choice">
          <input type="radio" name={group.key} checked={values[field.name] === 'true'}
            onChange={() => setValues((current) => ({ ...current, [field.name]: 'true', [other.name]: 'false' }))} />
          <span>{label}</span>
        </label>)}
      </div>
    </div>
  }

  function renderField(field) {
    if (field.isCheckbox) return <label key={field.name} className="document-type-toggle document-note">
      <input type="checkbox" checked={values[field.name] === 'true'}
        onChange={(event) => setValues((current) => ({ ...current, [field.name]: String(event.target.checked) }))} />
      <span>{field.label}</span>
    </label>

    return <div className="absent-field" key={field.name}>
      <label htmlFor={`sign-${field.name}`}>{field.label}{field.required ? '' : ' (optional)'}</label>
      <input id={`sign-${field.name}`} value={values[field.name] ?? ''} readOnly={field.readOnly}
        onChange={(event) => setValues((current) => ({ ...current, [field.name]: event.target.value }))} />
    </div>
  }

  async function save(event) {
    event.preventDefault()
    if (hasUnansweredHealthQuestion(groupSignFields(form.fields), values)) {
      setState({ status: 'error', message: 'Please answer the health questions.' })
      return
    }
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
        <a className="secondary-button document-link-button" href={`/api/documents/sign/${documentTypeId}/blank-form`} target="_blank" rel="noreferrer">Read the full form</a>
        <fieldset className="absent-fields document-review-fields" disabled={saving}>
          {groupSignFields(form.fields).map((group) => {
            if (group.kind === 'section') return <details className="document-note document-section" key={group.key}>
              <summary>{group.title}</summary>
              <div className="document-section-fields">{group.fields.map(renderField)}</div>
            </details>
            if (group.kind === 'field') return renderField(group.field)
            if (group.kind === 'health') return <div className="document-health document-note" key={group.key}>
              <strong>Health questions</strong>
              <ul>{group.questions.map((question) => <li key={question.key}>{question.label}</li>)}</ul>
              <div className="document-question" role="radiogroup" aria-label="Do any of these apply to you?">
                <span className="document-question-label">Do any of these apply to you?</span>
                <div className="document-question-options">
                  {[['none', 'None apply'], ['some', 'Something applies']].map(([choice, label]) => <label key={choice} className="document-choice">
                    <input type="radio" name="health-choice" checked={healthChoice === choice} onChange={() => {
                      setHealthChoice(choice)
                      setValues((current) => ({ ...current, ...answerAllHealthQuestions(group, choice === 'none') }))
                    }} />
                    <span>{label}</span>
                  </label>)}
                </div>
              </div>
              {healthChoice === 'some' && <>
                <p className="holiday-help">Answer each question. A manager will read your answers.</p>
                {group.questions.map(renderQuestion)}
              </>}
            </div>
            return renderQuestion(group)
          })}
        </fieldset>
        <SignaturePad ref={padRef} disabled={saving} onChange={setSigned} />
        <div className="document-sheet-actions">
          <button type="submit" disabled={!signed || saving}>{saving ? 'Checking…' : 'Sign and save'}</button>
        </div>
      </form>}
    </section>
  </div>
}
