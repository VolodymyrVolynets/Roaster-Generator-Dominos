import { useCallback, useEffect, useState } from 'react'
import { fetchJson } from '../../api/client'
import { documentFilterStates, stateDetails, toDocumentListQuery } from '../../lib/documents'
import { formatDateOnly, formatHolidayDate } from '../../lib/format'
import { AdminDocumentDetail } from './AdminDocumentDetail'
import { DocumentUploadFlow } from './DocumentUploadFlow'
import { VehiclePanel } from './VehiclePanel'

const emptyFilters = { search: '', documentTypeId: '', state: '', includeInactive: false }
const searchDelayMs = 250

export function AdminDocumentsPanel({ realtimeKey = 0 }) {
  const [filters, setFilters] = useState(emptyFilters)
  const [query, setQuery] = useState('')
  const [state, setState] = useState({ status: 'loading', groups: [], documentTypes: [], canAnalyse: false, message: '' })
  const [openDocumentId, setOpenDocumentId] = useState(null)
  const [upload, setUpload] = useState(null)
  const [notice, setNotice] = useState('')
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    const timer = setTimeout(() => setQuery(toDocumentListQuery(filters)), searchDelayMs)
    return () => clearTimeout(timer)
  }, [filters])

  useEffect(() => {
    let cancelled = false
    fetchJson(`/api/admin/documents${query}`, { cache: 'no-store' })
      .then((payload) => {
        if (!cancelled) setState({ status: 'success', message: '', ...payload })
      })
      .catch((error) => {
        if (!cancelled) setState((current) => ({ ...current, status: 'error', message: error.message }))
      })
    return () => { cancelled = true }
  }, [query, realtimeKey, refresh])

  const update = (field, value) => setFilters((current) => ({ ...current, [field]: value }))
  const reload = useCallback(() => setRefresh((current) => current + 1), [])
  const finishUpload = useCallback((message) => {
    setUpload(null)
    if (message) {
      setNotice(message)
      reload()
    }
  }, [reload])
  const reviewCount = state.groups.reduce((total, group) => total + group.needingReview, 0)
  const filtered = toDocumentListQuery(filters) !== ''

  return <section className="admin-tools role-details documents-panel">
    <div className="section-heading">
      <div><span className="eyebrow">Compliance</span><h2>Employee documents</h2></div>
    </div>
    {reviewCount > 0 && !filtered && <button type="button" className="secondary-button document-review-shortcut"
      onClick={() => update('state', 'WaitingForReview')}>{reviewCount} waiting for review</button>}
    <div className="absent-fields document-filters">
      <div className="absent-field">
        <label htmlFor="document-search">Search</label>
        <input id="document-search" type="search" value={filters.search} placeholder="Name, number or reference"
          onChange={(event) => update('search', event.target.value)} />
      </div>
      <div className="absent-field">
        <label htmlFor="document-filter-type">Document type</label>
        <select id="document-filter-type" value={filters.documentTypeId} onChange={(event) => update('documentTypeId', event.target.value)}>
          <option value="">All types</option>
          {state.documentTypes.map((type) => <option key={type.id} value={type.id}>{type.name}</option>)}
        </select>
      </div>
      <div className="absent-field">
        <label htmlFor="document-filter-state">Status</label>
        <select id="document-filter-state" value={filters.state} onChange={(event) => update('state', event.target.value)}>
          <option value="">All statuses</option>
          {documentFilterStates.map((item) => <option key={item} value={item}>{stateDetails[item].label}</option>)}
        </select>
      </div>
      <label className="document-type-toggle">
        <input type="checkbox" checked={filters.includeInactive} onChange={(event) => update('includeInactive', event.target.checked)} />
        <span>Include inactive employees</span>
      </label>
    </div>
    {state.status === 'loading' && <p className="message info-message" role="status">Loading documents…</p>}
    {state.status === 'error' && <div className="message error-message" role="alert">
      {state.message} <button type="button" className="secondary-button" onClick={reload}>Try again</button>
    </div>}
    {notice && <p className="save-message success" role="status">{notice}</p>}
    {state.status === 'success' && state.groups.length === 0 && <p className="message info-message">
      No documents match these filters.
      {filtered && <> <button type="button" className="secondary-button" onClick={() => setFilters(emptyFilters)}>Clear filters</button></>}
    </p>}
    <div className="document-groups">
      {state.groups.map((group) => <details key={group.employeeId} className="document-group" open={filtered}>
        <summary>
          <span className="document-group-name">
            <strong>{group.employeeName}</strong>
            <small>#{group.employeeNumber}{group.isActive ? '' : ' · Inactive'}</small>
          </span>
          <span className="document-group-counts">
            {group.vehicle?.openChange && <span className="document-state warning">Changing car</span>}
            {group.needingReview > 0 && <span className="document-state neutral">{group.needingReview} to review</span>}
            {group.outstanding > 0
              ? <span className="document-state warning">{group.outstanding} outstanding</span>
              : <span className="document-state good">Complete</span>}
          </span>
        </summary>
        {group.vehicle && <VehiclePanel vehicle={group.vehicle} basePath={`/api/admin/employees/${group.employeeId}/documents`} onChanged={reload} />}
        <ul className="documents-list">
          {group.documents.map((row) => <AdminDocumentRow
            key={`${row.documentTypeId}-${row.isRequired}`}
            row={row}
            canUpload={state.canAnalyse}
            onOpen={() => { setNotice(''); setOpenDocumentId(row.documentId) }}
            onUpload={() => { setNotice(''); setUpload({ employeeId: group.employeeId, documentTypeId: row.documentTypeId }) }}
          />)}
        </ul>
      </details>)}
    </div>
    {openDocumentId && <AdminDocumentDetail documentId={openDocumentId} onClose={() => setOpenDocumentId(null)} onChanged={reload} />}
    {upload && <DocumentUploadFlow
      basePath={`/api/admin/employees/${upload.employeeId}/documents`}
      documentTypes={state.documentTypes}
      requestedTypeId={upload.documentTypeId}
      onFinish={finishUpload}
      onVehicleChanged={reload}
    />}
  </section>
}

function AdminDocumentRow({ row, canUpload, onOpen, onUpload }) {
  const state = stateDetails[row.state] ?? { label: row.state, tone: 'neutral' }
  const details = [
    row.satisfiedByName && `Covered by ${row.satisfiedByName}`,
    row.referenceNumber && `Ref ${row.referenceNumber}`,
    row.expiryDate && `Expires ${formatDateOnly(row.expiryDate)}`,
    row.uploadedAtUtc && `Uploaded ${formatHolidayDate(row.uploadedAtUtc)}`,
  ].filter(Boolean)

  return <li>
    <div className="documents-row-text">
      <div className="documents-row-title">
        <strong>{row.documentTypeName}</strong>
        <span className={`document-state ${state.tone}`}>{state.label}</span>
      </div>
      {row.holderName && <span>{row.holderName}</span>}
      {details.length > 0 && <small>{details.join(' · ')}</small>}
    </div>
    {row.documentId
      ? <button type="button" className="secondary-button" onClick={onOpen} aria-label={`Open ${row.documentTypeName}`}>Open</button>
      : <button type="button" className="secondary-button" onClick={onUpload} disabled={!canUpload} aria-label={`Upload ${row.documentTypeName}`}>Upload</button>}
  </li>
}
