import { useState } from 'react'

export function PasswordChangePrompt({ fetchJson, onChanged, onSignOut }) {
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [state, setState] = useState({ busy: false, message: '' })

  async function submit(event) {
    event.preventDefault()
    if (state.busy) return
    if (newPassword !== confirmPassword) {
      setState({ busy: false, message: 'Passwords must match.' })
      return
    }
    setState({ busy: true, message: '' })
    try {
      await fetchJson('/api/auth/change-initial-password', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ newPassword, confirmPassword }),
      })
      await onChanged()
    } catch (error) {
      setState({ busy: false, message: error.message })
    }
  }

  return <main className="page-shell centered-page">
    <section className="holiday-panel role-details" role="dialog" aria-modal="true" aria-labelledby="password-change-title">
      <h1 id="password-change-title">Change your initial password</h1>
      <p>Please choose your own password. This prompt will appear each time you sign in until you change it.</p>
      <form onSubmit={submit}>
        <fieldset className="absent-fields" disabled={state.busy}>
          <div className="absent-field absent-wide">
            <label htmlFor="new-password">New password</label>
            <input id="new-password" type="password" autoComplete="new-password" autoFocus required minLength={5} maxLength={128}
              value={newPassword} onChange={(event) => setNewPassword(event.target.value)} />
          </div>
          <div className="absent-field absent-wide">
            <label htmlFor="confirm-password">Confirm password</label>
            <input id="confirm-password" type="password" autoComplete="new-password" required minLength={5} maxLength={128}
              value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} />
          </div>
        </fieldset>
        {state.message && <p className="message error-message" role="alert">{state.message}</p>}
        <div className="employee-form-actions">
          <button type="submit" disabled={state.busy}>{state.busy ? 'Saving…' : 'Change password'}</button>
          <button type="button" className="secondary-button" disabled={state.busy} onClick={onSignOut}>Sign out</button>
        </div>
      </form>
    </section>
  </main>
}
