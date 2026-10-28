import { useEffect, useState } from 'react'

const emptySettings = { storeId: '', storeName: '' }

export function StoreSettingsPanel({ fetchJson }) {
  const [settings, setSettings] = useState(null)
  const [savedSettings, setSavedSettings] = useState(null)
  const [state, setState] = useState({ status: 'loading', message: '' })
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    let cancelled = false
    setState({ status: 'loading', message: '' })
    fetchJson('/api/admin/settings/store', { cache: 'no-store' })
      .then((payload) => {
        if (cancelled) return
        const normalized = { ...emptySettings, ...payload }
        setSettings(normalized)
        setSavedSettings(normalized)
        setState({ status: 'success', message: '' })
      })
      .catch((error) => {
        if (!cancelled) setState({ status: 'error', message: error.message })
      })
    return () => { cancelled = true }
  }, [fetchJson, refresh])

  async function save(event) {
    event.preventDefault()
    if (!settings || state.status === 'saving') return
    setState({ status: 'saving', message: '' })
    try {
      const saved = await fetchJson('/api/admin/settings/store', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settings),
      })
      setSettings(saved)
      setSavedSettings(saved)
      setState({ status: 'success', message: 'Store settings saved.' })
    } catch (error) {
      setState({ status: 'error', message: error.message })
    }
  }

  const dirty = settings && savedSettings && JSON.stringify(settings) !== JSON.stringify(savedSettings)

  return <section className="admin-tools role-details store-settings-panel">
    <div className="section-heading">
      <div><span className="eyebrow">Settings</span><h2>Store details</h2></div>
      <button type="button" className="secondary-button" onClick={() => setRefresh((current) => current + 1)} disabled={state.status === 'loading' || state.status === 'saving'}>Refresh</button>
    </div>
    <p className="holiday-help">Store ID and store name are saved for this application and added to every downloaded absence PDF.</p>
    {state.status === 'loading' && <p className="message info-message" role="status">Loading store settings…</p>}
    {state.status === 'error' && <p className="message error-message" role="alert">{state.message}</p>}
    {settings && <form className="store-settings-form" onSubmit={save}>
      <fieldset className="absent-fields store-settings-fields" disabled={state.status === 'saving'}>
        <div className="absent-field">
          <label htmlFor="store-settings-id">Store ID</label>
          <input id="store-settings-id" value={settings.storeId} onChange={(event) => setSettings((current) => ({ ...current, storeId: event.target.value }))} maxLength={100} required />
        </div>
        <div className="absent-field">
          <label htmlFor="store-settings-name">Store name</label>
          <input id="store-settings-name" value={settings.storeName} onChange={(event) => setSettings((current) => ({ ...current, storeName: event.target.value }))} maxLength={200} required />
        </div>
      </fieldset>
      <div className="absent-form-footer">
        <span>{dirty ? 'Save your changes before downloading a new absence form.' : 'Store details are saved.'}</span>
        <button type="submit" disabled={!dirty || state.status === 'saving'}>
          {state.status === 'saving' ? 'Saving…' : 'Save store settings'}
        </button>
      </div>
      {state.message && state.status !== 'error' && <p className="save-message success" role="status">{state.message}</p>}
    </form>}
  </section>
}
