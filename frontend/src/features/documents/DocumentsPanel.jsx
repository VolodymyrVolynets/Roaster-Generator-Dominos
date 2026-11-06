import { useCallback, useEffect, useState } from 'react'
import { fetchJson } from '../../api/client'
import { checklistSummary, stateDetails } from '../../lib/documents'
import { formatDateOnly, formatHolidayDate } from '../../lib/format'
import { DocumentUploadFlow } from './DocumentUploadFlow'

const uploadedStates = new Set(['Valid', 'ExpiringSoon', 'WaitingForReview', 'Expired'])

export function DocumentsPanel({ realtimeKey = 0 }) {
  const [state, setState] = useState({ status: 'loading', items: [], documentTypes: [], canAnalyse: false, message: '' })
  const [upload, setUpload] = useState(null)
  const [notice, setNotice] = useState('')
  const [retry, setRetry] = useState(0)

  useEffect(() => {
    let cancelled = false
    fetchJson('/api/documents', { cache: 'no-store' })
      .then((payload) => {
        if (!cancelled) setState({ status: 'success', message: '', ...payload })
      })
      .catch((error) => {
        if (!cancelled) setState((current) => ({ ...current, status: 'error', message: error.message }))
      })
    return () => { cancelled = true }
  }, [realtimeKey, retry])

  const finishUpload = useCallback((message) => {
    setUpload(null)
    if (message) {
      setNotice(message)
      setRetry((current) => current + 1)
    }
  }, [])

  const required = state.items.filter((item) => item.isRequired)
  const others = state.items.filter((item) => !item.isRequired)
  const { outstanding } = checklistSummary(state.items)
  const openUpload = (documentTypeId = null) => {
    setNotice('')
    setUpload({ documentTypeId })
  }

  return <section className="admin-tools role-details documents-panel">
    <div className="section-heading">
      <div>
        <span className="eyebrow">Documents</span>
        <h2>My documents</h2>
      </div>
      <button type="button" onClick={() => openUpload()} disabled={!state.canAnalyse}>Upload document</button>
    </div>
    {state.status === 'success' && <p className="holiday-help">
      {outstanding > 0 ? `${outstanding} of ${required.length} required documents still need attention.`
        : required.length > 0 ? 'All your required documents are uploaded.' : 'No documents are required from you yet.'}
    </p>}
    {state.status === 'success' && !state.canAnalyse && <p className="message info-message">Document uploads are not available yet.</p>}
    {state.status === 'loading' && <p className="message info-message" role="status">Loading your documents…</p>}
    {state.status === 'error' && <div className="message error-message" role="alert">
      {state.message} <button type="button" className="secondary-button" onClick={() => setRetry((current) => current + 1)}>Try again</button>
    </div>}
    {notice && <p className="save-message success" role="status">{notice}</p>}
    {required.length > 0 && <DocumentList items={required} canUpload={state.canAnalyse} onUpload={openUpload} />}
    {others.length > 0 && <>
      <h3 className="documents-subheading">Other documents</h3>
      <DocumentList items={others} canUpload={state.canAnalyse} onUpload={openUpload} />
    </>}
    {upload && <DocumentUploadFlow
      documentTypes={state.documentTypes}
      requestedTypeId={upload.documentTypeId}
      onFinish={finishUpload}
    />}
  </section>
}

function DocumentList({ items, canUpload, onUpload }) {
  return <ul className="documents-list">
    {items.map((item) => <DocumentRow key={`${item.documentTypeId}-${item.isRequired}`} item={item} canUpload={canUpload} onUpload={onUpload} />)}
  </ul>
}

function DocumentRow({ item, canUpload, onUpload }) {
  const state = stateDetails[item.state] ?? { label: item.state, tone: 'neutral' }
  const uploaded = uploadedStates.has(item.state) && item.documentId
  const details = [
    item.satisfiedByName && `Covered by ${item.satisfiedByName}`,
    item.referenceNumber && `Ref ${item.referenceNumber}`,
    item.expiryDate && `Expires ${formatDateOnly(item.expiryDate)}`,
    item.uploadedAtUtc && `Uploaded ${formatHolidayDate(item.uploadedAtUtc)}`,
  ].filter(Boolean)

  return <li>
    <div className="documents-row-text">
      <div className="documents-row-title">
        <strong>{item.documentTypeName}</strong>
        <span className={`document-state ${state.tone}`}>{state.label}</span>
      </div>
      {item.holderName && <span>{item.holderName}</span>}
      {details.length > 0 && <small>{details.join(' · ')}</small>}
      {item.state === 'DependsOnPassport' && <small>Upload your passport first. This applies to non-EU passports.</small>}
    </div>
    <button type="button" className="secondary-button" onClick={() => onUpload(item.documentTypeId)} disabled={!canUpload}
      aria-label={`${uploaded ? 'Replace' : 'Upload'} ${item.documentTypeName}`}>{uploaded ? 'Replace' : 'Upload'}</button>
  </li>
}
