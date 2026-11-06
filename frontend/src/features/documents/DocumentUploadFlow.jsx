import { useEffect, useMemo, useRef, useState } from 'react'
import { fetchJson } from '../../api/client'
import { DatePicker } from '../../components/DatePicker'
import { fieldLabels, fieldsChanged, fieldsFor, maxDocumentFiles, toFormFields, toSubmitRequest } from '../../lib/documents'
import { prepareDocumentImage } from '../../lib/prepareDocumentImage'

const dateFields = new Set(['expiryDate', 'signingDate'])
const supportsCamera = () => navigator.maxTouchPoints > 0

export function DocumentUploadFlow({ documentTypes, requestedTypeId, onFinish, onVehicleChanged, basePath = '/api/documents', enforceBothSides = true }) {
  const [step, setStep] = useState('choose')
  const [files, setFiles] = useState([])
  const [error, setError] = useState('')
  const [analysis, setAnalysis] = useState(null)
  const [fields, setFields] = useState(toFormFields())
  const [typeId, setTypeId] = useState(requestedTypeId ?? '')
  const [note, setNote] = useState('')
  const [vehiclePrompt, setVehiclePrompt] = useState(null)
  const cameraRef = useRef(null)
  const imageRef = useRef(null)
  const pdfRef = useRef(null)
  const addRef = useRef(null)
  const draftRef = useRef(null)

  const previews = useMemo(() => files.map((file) => ({
    name: file.name,
    url: file.type === 'application/pdf' ? null : URL.createObjectURL(file),
  })), [files])
  useEffect(() => () => previews.forEach((preview) => preview.url && URL.revokeObjectURL(preview.url)), [previews])

  const selectedType = documentTypes.find((type) => type.id === typeId)
  const original = useMemo(() => toFormFields(analysis?.fields), [analysis])
  const edited = Boolean(analysis?.isAnalysed) &&
    (fieldsChanged(original, fields) || (analysis.documentTypeId ?? '') !== typeId)
  const flagged = edited || analysis?.warnings.length > 0
  const missingSide = enforceBothSides && Boolean(analysis?.isAnalysed) && Boolean(selectedType?.requiresBothSides) && !analysis.hasBothSides
  const canAddPage = files.length < maxDocumentFiles && files[0]?.type !== 'application/pdf'

  async function analyse(nextFiles) {
    setStep('checking')
    setError('')
    const body = new FormData()
    nextFiles.forEach((file) => body.append('files', file))
    if (requestedTypeId) body.append('documentTypeId', requestedTypeId)
    try {
      const result = await fetchJson(`${basePath}/analyses`, { method: 'POST', body })
      draftRef.current = result.draftId
      setFields(toFormFields(result.fields))
      setTypeId(result.documentTypeId ?? '')
      setAnalysis(result)
      setVehiclePrompt(result.vehiclePrompt)
      setStep('review')
    } catch (requestError) {
      setError(requestError.message)
      setStep('pages')
    }
  }

  async function select(event) {
    const chosen = event.target.files?.[0]
    event.target.value = ''
    if (!chosen) return
    setError('')
    try {
      if (chosen.type === 'application/pdf') {
        setFiles([chosen])
        await analyse([chosen])
      } else {
        const prepared = await prepareDocumentImage(chosen)
        setFiles((current) => [...current, prepared])
        setStep('pages')
      }
    } catch (selectionError) {
      setError(selectionError.message)
    }
  }

  function removePage(index) {
    const remaining = files.filter((_, position) => position !== index)
    setFiles(remaining)
    if (remaining.length === 0) setStep('choose')
  }

  async function confirmNewCar() {
    setError('')
    try {
      await fetchJson(`${basePath}/vehicle-change`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ registration: vehiclePrompt.detectedRegistration }),
      })
      setVehiclePrompt(null)
      onVehicleChanged?.()
    } catch (requestError) {
      setError(requestError.message)
    }
  }

  async function discard() {
    const draftId = draftRef.current
    draftRef.current = null
    onFinish()
    if (draftId) {
      try {
        await fetchJson(`${basePath}/analyses/${draftId}`, { method: 'DELETE' })
      } catch {
        // The server removes unconfirmed drafts within the hour.
      }
    }
  }

  async function submit(event) {
    event.preventDefault()
    setStep('saving')
    setError('')
    try {
      const saved = await fetchJson(`${basePath}/analyses/${analysis.draftId}/submit`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(toSubmitRequest(fields, typeId, note)),
      })
      onFinish(saved.needsReview ? 'Document uploaded. An administrator will review it.' : 'Document uploaded.')
    } catch (requestError) {
      setError(requestError.message)
      setStep('review')
    }
  }

  const busy = step === 'checking' || step === 'saving'

  return <div className="sheet-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy) void discard() }}>
    <section className="sheet document-upload-sheet" role="dialog" aria-modal="true" aria-labelledby="document-upload-title">
      <div className="section-heading">
        <div><span className="eyebrow">Upload</span><h2 id="document-upload-title">{step === 'review' || step === 'saving' ? 'Check your document' : 'Upload document'}</h2></div>
        <button type="button" className="secondary-button" onClick={discard} disabled={busy}>Cancel</button>
      </div>
      <input ref={cameraRef} type="file" accept="image/*" capture="environment" hidden onChange={select} />
      <input ref={imageRef} type="file" accept="image/*" hidden onChange={select} />
      <input ref={pdfRef} type="file" accept="application/pdf" hidden onChange={select} />
      <input ref={addRef} type="file" accept="image/*" hidden onChange={select} />
      {error && <p className="message error-message" role="alert">{error}</p>}

      {step === 'choose' && <div className="document-source-list">
        {supportsCamera() && <button type="button" onClick={() => cameraRef.current.click()}>Take a photo</button>}
        <button type="button" className="secondary-button" onClick={() => imageRef.current.click()}>Choose an image</button>
        <button type="button" className="secondary-button" onClick={() => pdfRef.current.click()}>Upload a PDF</button>
        <p className="holiday-help">For two-sided cards such as your IRP or driving licence, add the other side after the first photo. Your document is checked by AI before you upload it.</p>
      </div>}

      {step === 'pages' && <div className="document-review">
        <ul className="document-previews">
          {previews.map((preview, index) => <li key={`${preview.name}-${index}`}>
            {preview.url ? <img src={preview.url} alt={`Page ${index + 1} of your document`} /> : <span>{preview.name}</span>}
            <button type="button" className="secondary-button" onClick={() => removePage(index)}>Remove</button>
          </li>)}
        </ul>
        {canAddPage && <button type="button" className="secondary-button" onClick={() => addRef.current.click()}>Add another side or page</button>}
        <p className="holiday-help">Add every side or page first, then check the document.</p>
        <button type="button" onClick={() => analyse(files)} disabled={files.length === 0}>Check document</button>
      </div>}

      {step === 'checking' && <div className="document-checking" role="status">
        <span className="document-spinner" aria-hidden="true" />
        <strong>Checking your document…</strong>
        <span>This can take up to a minute. Keep this page open.</span>
      </div>}

      {(step === 'review' || step === 'saving') && analysis && <form className="document-review" onSubmit={submit}>
        <ul className="document-previews">
          {previews.map((preview, index) => <li key={`${preview.name}-${index}`}>
            {preview.url ? <img src={preview.url} alt={`Page ${index + 1} of your document`} /> : <span>{preview.name}</span>}
          </li>)}
        </ul>
        {analysis.summary && <p className="holiday-help">{analysis.summary}</p>}
        {vehiclePrompt && <div className="message info-message document-warnings" role="status">
          <strong>This shows a different car</strong>
          <span>This document says {vehiclePrompt.detectedRegistration}, but the current car is {vehiclePrompt.currentRegistration}. Did you change your car?</span>
          <div className="document-type-actions">
            <button type="button" onClick={confirmNewCar} disabled={busy}>Yes, I changed my car</button>
            <button type="button" className="secondary-button" onClick={() => setVehiclePrompt(null)} disabled={busy}>No</button>
          </div>
        </div>}
        {analysis.warnings.length > 0 && <div className="message info-message document-warnings" role="status">
          <strong>Please check</strong>
          <ul>{analysis.warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul>
          <span>You can retake it, or upload it anyway and an administrator will review it.</span>
        </div>}
        {missingSide && <div className="message info-message document-warnings" role="alert">
          <strong>{selectedType.name} needs both sides</strong>
          <span>Add a photo of the other side, then check the document again.</span>
          <div className="document-type-actions">
            <button type="button" onClick={() => setStep('pages')} disabled={busy}>Add the other side</button>
          </div>
        </div>}
        <fieldset className="absent-fields document-review-fields" disabled={busy}>
          <div className="absent-field">
            <label htmlFor="document-type">Document type</label>
            <select id="document-type" value={typeId} required onChange={(event) => setTypeId(event.target.value)}>
              <option value="">Choose a type</option>
              {documentTypes.map((type) => <option key={type.id} value={type.id}>{type.name}</option>)}
            </select>
          </div>
          {fieldsFor(selectedType).map((field) => <div className="absent-field" key={field}>
            <label htmlFor={`document-${field}`}>{fieldLabels[field]}</label>
            {dateFields.has(field)
              ? <DatePicker id={`document-${field}`} value={fields[field]} clearable disabled={busy}
                  onChange={(value) => setFields((current) => ({ ...current, [field]: value ?? '' }))} />
              : <input id={`document-${field}`} value={fields[field]} type={field === 'penaltyPoints' ? 'number' : 'text'}
                  min={field === 'penaltyPoints' ? 0 : undefined} max={field === 'penaltyPoints' ? 100 : undefined}
                  maxLength={field === 'issuingCountry' ? 2 : undefined}
                  onChange={(event) => setFields((current) => ({ ...current, [field]: event.target.value }))} />}
          </div>)}
          {flagged && <div className="absent-field document-note">
            <label htmlFor="document-note">Note for the administrator (optional)</label>
            <textarea id="document-note" value={note} maxLength={500} rows={2} onChange={(event) => setNote(event.target.value)} />
          </div>}
        </fieldset>
        {edited && <p className="holiday-help">Because you changed something, an administrator will review this document.</p>}
        <div className="document-type-actions">
          <button type="submit" disabled={busy || !typeId || missingSide}>{step === 'saving' ? 'Uploading…' : 'Upload'}</button>
        </div>
      </form>}
    </section>
  </div>
}
