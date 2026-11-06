import { useEffect, useState } from 'react'
import { fetchJson } from '../../api/client'
import { DatePicker } from '../../components/DatePicker'
import { confirmAction } from '../../components/ConfirmDialog'
import { auditActionLabels, fieldLabels, fieldsFor, toFormFields, toSubmitRequest } from '../../lib/documents'
import { formatHolidayDate } from '../../lib/format'

const dateFields = new Set(['expiryDate', 'signingDate'])
const fileUrl = (documentId, fileId) => `/api/admin/documents/${documentId}/files/${fileId}`

export function AdminDocumentDetail({ documentId, onClose, onChanged }) {
  const [document, setDocument] = useState(null)
  const [fields, setFields] = useState(toFormFields())
  const [state, setState] = useState({ status: 'loading', message: '' })
  const busy = state.status === 'saving'

  useEffect(() => {
    let cancelled = false
    fetchJson(`/api/admin/documents/${documentId}`, { cache: 'no-store' })
      .then((loaded) => {
        if (cancelled) return
        setDocument(loaded)
        setFields(toFormFields(loaded.fields))
        setState({ status: 'idle', message: '' })
      })
      .catch((error) => {
        if (!cancelled) setState({ status: 'error', message: error.message })
      })
    return () => { cancelled = true }
  }, [documentId])

  async function save(event) {
    event.preventDefault()
    setState({ status: 'saving', message: '' })
    try {
      const { documentTypeId: _ignored, note: _alsoIgnored, ...values } = toSubmitRequest(fields, null, '')
      const updated = await fetchJson(`/api/admin/documents/${documentId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...values, revision: document.revision }),
      })
      setDocument(updated)
      setFields(toFormFields(updated.fields))
      setState({ status: 'idle', message: 'Saved.' })
      onChanged()
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  async function requestSignature() {
    setState({ status: 'saving', message: '' })
    try {
      setDocument(await fetchJson(`/api/admin/documents/${documentId}/request-signature`, { method: 'POST' }))
      setState({ status: 'idle', message: 'The employee will be asked to sign again.' })
      onChanged()
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  async function remove() {
    if (!(await confirmAction({
      title: 'Delete document?',
      message: `The ${document.documentType.name} for ${document.employeeName} and its files will be permanently deleted.`,
      confirmLabel: 'Delete document',
      destructive: true,
    }))) return
    setState({ status: 'saving', message: '' })
    try {
      await fetchJson(`/api/admin/documents/${documentId}`, { method: 'DELETE' })
      onChanged()
      onClose()
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  return <div className="sheet-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy) onClose() }}>
    <section className="sheet document-upload-sheet" role="dialog" aria-modal="true" aria-labelledby="document-detail-title">
      <div className="section-heading">
        <div>
          <span className="eyebrow">{document?.employeeName ?? 'Document'}</span>
          <h2 id="document-detail-title">{document?.documentType.name ?? 'Loading…'}</h2>
        </div>
        <button type="button" className="secondary-button" onClick={onClose} disabled={busy}>Close</button>
      </div>
      {state.status === 'error' && <p className="message error-message" role="alert">{state.message}</p>}
      {state.status === 'loading' && <p className="message info-message" role="status">Loading document…</p>}
      {document && <form className="document-review" onSubmit={save}>
        {document.needsReview && <div className="message info-message document-warnings">
          <strong>Needs review</strong>
          {document.reviewReasons.length > 0 && <ul>{document.reviewReasons.map((reason) => <li key={reason}>{reason}</li>)}</ul>}
        </div>}
        {document.employeeNote && <p className="holiday-help"><strong>Note from the employee:</strong> {document.employeeNote}</p>}
        <ul className="document-previews">
          {document.files.map((file) => <FilePreview key={file.id} documentId={documentId} file={file} />)}
        </ul>
        <fieldset className="absent-fields document-review-fields" disabled={busy}>
          {fieldsFor(document.documentType).map((field) => <div className="absent-field" key={field}>
            <label htmlFor={`document-detail-${field}`}>{fieldLabels[field]}</label>
            {dateFields.has(field)
              ? <DatePicker id={`document-detail-${field}`} value={fields[field]} clearable disabled={busy}
                  onChange={(value) => setFields((current) => ({ ...current, [field]: value ?? '' }))} />
              : <input id={`document-detail-${field}`} value={fields[field]} type={field === 'penaltyPoints' ? 'number' : 'text'}
                  min={field === 'penaltyPoints' ? 0 : undefined} max={field === 'penaltyPoints' ? 100 : undefined}
                  maxLength={field === 'issuingCountry' ? 2 : undefined}
                  onChange={(event) => setFields((current) => ({ ...current, [field]: event.target.value }))} />}
          </div>)}
        </fieldset>
        {document.documentType.expiryRule === 'PenaltyPoints' && document.fields.expiryDate && <p className="holiday-help">
          Due date {document.fields.expiryDate}, calculated from the signing date and points.
        </p>}
        {state.message && <p className="save-message success" role="status">{state.message}</p>}
        <div className="document-sheet-actions document-type-actions">
          <button type="submit" disabled={busy}>{busy ? 'Saving…' : document.needsReview ? 'Save and mark reviewed' : 'Save changes'}</button>
          {document.documentType.kind === 'Sign' && <button type="button" className="secondary-button" onClick={requestSignature}
            disabled={busy || document.newSignatureRequested}>{document.newSignatureRequested ? 'Signature requested' : 'Ask to sign again'}</button>}
          <button type="button" className="danger-button" onClick={remove} disabled={busy}>Delete</button>
        </div>
        <div className="document-audit">
          <strong>History</strong>
          <ul>
            {document.audit.map((entry, index) => <li key={`${entry.occurredAtUtc}-${index}`}>
              {auditActionLabels[entry.action] ?? entry.action} by {entry.actorName}, {formatHolidayDate(entry.occurredAtUtc)}
            </li>)}
          </ul>
        </div>
      </form>}
    </section>
  </div>
}

function FilePreview({ documentId, file }) {
  const [url, setUrl] = useState(null)
  const [failed, setFailed] = useState(false)
  const isImage = file.contentType.startsWith('image/')

  useEffect(() => {
    if (!isImage) return undefined
    let objectUrl = null
    let cancelled = false
    fetch(`${fileUrl(documentId, file.id)}?preview=true`, { credentials: 'include', cache: 'no-store' })
      .then((response) => {
        if (!response.ok) throw new Error('The file could not be loaded.')
        return response.blob()
      })
      .then((blob) => {
        if (cancelled) return
        objectUrl = URL.createObjectURL(blob)
        setUrl(objectUrl)
      })
      .catch(() => {
        if (!cancelled) setFailed(true)
      })
    return () => {
      cancelled = true
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [documentId, file.id, isImage])

  return <li>
    {url && <img src={url} alt={`Page ${file.pageIndex + 1} of the document`} />}
    {!url && <span>{failed ? 'Preview unavailable' : isImage ? 'Loading…' : 'PDF document'}</span>}
    <a className="secondary-button document-link-button" href={fileUrl(documentId, file.id)}>Download page {file.pageIndex + 1}</a>
  </li>
}
