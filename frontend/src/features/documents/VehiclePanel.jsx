import { useState } from 'react'
import { fetchJson } from '../../api/client'
import { confirmAction, promptAction } from '../../components/ConfirmDialog'
import { formatHolidayDate } from '../../lib/format'

export function VehiclePanel({ vehicle, basePath, onChanged }) {
  const [state, setState] = useState({ busy: false, message: '' })
  const change = vehicle.openChange

  async function run(action) {
    setState({ busy: true, message: '' })
    try {
      await action()
      setState({ busy: false, message: '' })
      onChanged()
    } catch (error) {
      setState({ busy: false, message: error.message })
    }
  }

  async function start() {
    const registration = await promptAction({
      title: change ? 'Correct the new registration' : 'Changed your car?',
      message: 'Your vehicle documents will then need updating for this car. This does not affect your shifts.',
      inputLabel: 'New registration',
      confirmLabel: change ? 'Save registration' : 'Start car change',
    })
    if (!registration?.trim()) return
    await run(() => fetchJson(`${basePath}/vehicle-change`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ registration }),
    }))
  }

  async function cancel() {
    if (!(await confirmAction({
      title: 'Cancel car change?',
      message: `The current car goes back to ${change.oldRegistration}.`,
      confirmLabel: 'Cancel car change',
      destructive: true,
    }))) return
    await run(() => fetchJson(`${basePath}/vehicle-change`, { method: 'DELETE' }))
  }

  if (!vehicle.currentRegistration) return null

  return <div className="document-vehicle" role="group" aria-label="Current vehicle">
    <div className="documents-row-text">
      {change
        ? <>
            <strong>Changing car to {change.newRegistration}</strong>
            <span>{change.documentsRemaining === 1 ? '1 document' : `${change.documentsRemaining} documents`} left to update.</span>
            <small>Previous car {change.oldRegistration} · Started {formatHolidayDate(change.startedAtUtc)}</small>
          </>
        : <>
            <small>Current vehicle</small>
            <strong>{vehicle.currentRegistration}</strong>
          </>}
    </div>
    <div className="document-type-actions">
      <button type="button" className="secondary-button" onClick={start} disabled={state.busy}>{change ? 'Correct' : 'I changed my car'}</button>
      {change && <button type="button" className="secondary-button" onClick={cancel} disabled={state.busy}>Cancel change</button>}
    </div>
    {state.message && <p className="message error-message" role="alert">{state.message}</p>}
  </div>
}
