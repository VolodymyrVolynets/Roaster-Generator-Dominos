import { useCallback, useEffect, useState } from 'react'
import { confirmAction } from '../../components/ConfirmDialog'
import {
  documentCategories, draftsEqual, expiryRuleLabel, expiryRules, requirementConditions,
  setRequirement, toDrafts, toListRequest, visibleTypes,
} from '../../lib/documentSettings'

const jsonOptions = (method, body) => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
})

const emptyType = { name: '', expiryRule: 'Printed', hasReference: true, isActive: true }

export function DocumentSettingsPanel({ fetchJson }) {
  const [types, setTypes] = useState(null)
  const [saved, setSaved] = useState({})
  const [drafts, setDrafts] = useState({})
  const [category, setCategory] = useState(documentCategories[0].id)
  const [copySource, setCopySource] = useState('')
  const [state, setState] = useState({ status: 'loading', message: '' })
  const [listMessage, setListMessage] = useState('')
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    let cancelled = false
    setState({ status: 'loading', message: '' })
    Promise.all([
      fetchJson('/api/admin/document-types', { cache: 'no-store' }),
      fetchJson('/api/admin/document-requirements', { cache: 'no-store' }),
    ])
      .then(([loadedTypes, lists]) => {
        if (cancelled) return
        const loaded = toDrafts(lists)
        setTypes(loadedTypes)
        setSaved(loaded)
        setDrafts(loaded)
        setState({ status: 'success', message: '' })
      })
      .catch((error) => {
        if (!cancelled) setState({ status: 'error', message: error.message })
      })
    return () => { cancelled = true }
  }, [fetchJson, refresh])

  const draft = drafts[category] ?? {}
  const dirty = !draftsEqual(draft, saved[category])
  const saving = state.status === 'saving'
  const updateDraft = (next) => {
    setListMessage('')
    setDrafts((current) => ({ ...current, [category]: next }))
  }

  async function saveList() {
    setState({ status: 'saving', message: '' })
    setListMessage('')
    try {
      const response = await fetchJson(
        `/api/admin/document-requirements/${category}`,
        jsonOptions('PUT', toListRequest(draft, types)),
      )
      const stored = toDrafts([response])
      setSaved((current) => ({ ...current, ...stored }))
      setDrafts((current) => ({ ...current, ...stored }))
      setState({ status: 'success', message: '' })
      setListMessage('Required documents saved.')
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  function copyList() {
    if (!copySource) return
    updateDraft({ ...(drafts[copySource] ?? {}) })
    setCopySource('')
  }

  const onTypeSaved = useCallback((type) => {
    setTypes((current) => (current.some((item) => item.id === type.id)
      ? current.map((item) => (item.id === type.id ? type : item))
      : [...current, type]))
  }, [])
  const onTypeDeleted = useCallback((id) => {
    setTypes((current) => current.filter((item) => item.id !== id))
  }, [])

  return <section className="admin-tools role-details document-settings">
    <div className="section-heading">
      <div><span className="eyebrow">Documents</span><h2>Required documents</h2></div>
      <button type="button" className="secondary-button" onClick={() => setRefresh((current) => current + 1)}
        disabled={state.status === 'loading' || saving}>Refresh</button>
    </div>
    <p className="holiday-help">Choose what each type of employee must provide. Non-EU only items apply once a passport shows a non-EU country. An IRP can also be met by a labour market permission with a TRC.</p>
    {state.status === 'loading' && <p className="message info-message" role="status">Loading document settings…</p>}
    {state.status === 'error' && <p className="message error-message" role="alert">{state.message}</p>}
    {types && <>
      <div className="absent-fields document-list-controls">
        <div className="absent-field">
          <label htmlFor="document-category">Employee type</label>
          <select id="document-category" value={category} onChange={(event) => { setCategory(event.target.value); setListMessage('') }}>
            {documentCategories.map((item) => <option key={item.id} value={item.id}>
              {item.label}{draftsEqual(drafts[item.id], saved[item.id]) ? '' : ' (unsaved)'}
            </option>)}
          </select>
        </div>
        <div className="absent-field">
          <label htmlFor="document-copy-source">Copy from another list</label>
          <div className="document-copy-row">
            <select id="document-copy-source" value={copySource} onChange={(event) => setCopySource(event.target.value)}>
              <option value="">Choose a list</option>
              {documentCategories.filter((item) => item.id !== category).map((item) => <option key={item.id} value={item.id}>{item.label}</option>)}
            </select>
            <button type="button" className="secondary-button" onClick={copyList} disabled={!copySource || saving}>Copy</button>
          </div>
        </div>
      </div>
      <ul className="document-requirement-list">
        {visibleTypes(types, draft).map((type) => {
          const included = Boolean(draft[type.id])
          return <li key={type.id}>
            <label className="document-requirement-name">
              <input type="checkbox" checked={included} disabled={saving}
                onChange={(event) => updateDraft(setRequirement(draft, type.id, event.target.checked ? 'Always' : null))} />
              <span>{type.name}{type.isActive ? '' : ' (turned off)'}</span>
            </label>
            {included && <select aria-label={`When ${type.name} is required`} value={draft[type.id]} disabled={saving}
              onChange={(event) => updateDraft(setRequirement(draft, type.id, event.target.value))}>
              {requirementConditions.map((item) => <option key={item.id} value={item.id}>{item.label}</option>)}
            </select>}
          </li>
        })}
      </ul>
      <div className="absent-form-footer">
        <span role="status">{listMessage || (dirty ? 'You have unsaved changes to this list.' : `${Object.keys(draft).length} documents required.`)}</span>
        <button type="button" onClick={saveList} disabled={!dirty || saving}>{saving ? 'Saving…' : 'Save list'}</button>
      </div>
      <DocumentTypesEditor types={types} fetchJson={fetchJson} onSaved={onTypeSaved} onDeleted={onTypeDeleted} />
    </>}
  </section>
}

function DocumentTypesEditor({ types, fetchJson, onSaved, onDeleted }) {
  const [editing, setEditing] = useState(null)
  const [state, setState] = useState({ status: 'idle', message: '' })
  const busy = state.status === 'saving'
  const update = (field, value) => setEditing((current) => ({ ...current, [field]: value }))
  const open = (type) => {
    setState({ status: 'idle', message: '' })
    setEditing(type)
  }

  async function save(event) {
    event.preventDefault()
    setState({ status: 'saving', message: '' })
    try {
      const { id, name, expiryRule, hasReference, isActive } = editing
      const body = { name, expiryRule, hasReference, isActive }
      onSaved(await fetchJson(
        id ? `/api/admin/document-types/${id}` : '/api/admin/document-types',
        jsonOptions(id ? 'PUT' : 'POST', body),
      ))
      setEditing(null)
      setState({ status: 'idle', message: '' })
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  async function remove() {
    if (!(await confirmAction({
      title: 'Delete document type?',
      message: `${editing.name} will be removed from the catalogue.`,
      confirmLabel: 'Delete type',
      destructive: true,
    }))) return
    setState({ status: 'saving', message: '' })
    try {
      await fetchJson(`/api/admin/document-types/${editing.id}`, { method: 'DELETE' })
      onDeleted(editing.id)
      setEditing(null)
      setState({ status: 'idle', message: '' })
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  return <div className="document-types">
    <div className="section-heading">
      <div><span className="eyebrow">Catalogue</span><h2>Document types</h2></div>
      <button type="button" className="secondary-button" onClick={() => open(emptyType)} disabled={busy}>Add type</button>
    </div>
    {editing && <form className="store-settings-form document-type-form" onSubmit={save}>
      <fieldset className="absent-fields" disabled={busy}>
        <div className="absent-field">
          <label htmlFor="document-type-name">Name</label>
          <input id="document-type-name" value={editing.name} maxLength={100} required autoFocus onChange={(event) => update('name', event.target.value)} />
        </div>
        <div className="absent-field">
          <label htmlFor="document-type-expiry">Expiry</label>
          <select id="document-type-expiry" value={editing.expiryRule} onChange={(event) => update('expiryRule', event.target.value)}>
            {expiryRules.map((item) => <option key={item.id} value={item.id}>{item.label}</option>)}
          </select>
        </div>
        <label className="document-type-toggle">
          <input type="checkbox" checked={editing.hasReference} onChange={(event) => update('hasReference', event.target.checked)} />
          <span>Has a reference number</span>
        </label>
        <label className="document-type-toggle">
          <input type="checkbox" checked={editing.isActive} onChange={(event) => update('isActive', event.target.checked)} />
          <span>Turned on</span>
        </label>
      </fieldset>
      {state.status === 'error' && <p className="message error-message" role="alert">{state.message}</p>}
      <div className="document-type-actions">
        <button type="submit" disabled={busy}>{busy ? 'Saving…' : editing.id ? 'Save type' : 'Add type'}</button>
        <button type="button" className="secondary-button" onClick={() => setEditing(null)} disabled={busy}>Cancel</button>
        {editing.id && !editing.isBuiltIn && <button type="button" className="danger-button" onClick={remove} disabled={busy}>Delete</button>}
      </div>
    </form>}
    <ul className="document-type-list">
      {types.map((type) => <li key={type.id}>
        <div>
          <strong>{type.name}</strong>
          <small>{expiryRuleLabel(type.expiryRule)}{type.isActive ? '' : ' · Turned off'}</small>
        </div>
        <button type="button" className="secondary-button" onClick={() => open(type)} disabled={busy} aria-label={`Edit ${type.name}`}>Edit</button>
      </li>)}
    </ul>
  </div>
}
