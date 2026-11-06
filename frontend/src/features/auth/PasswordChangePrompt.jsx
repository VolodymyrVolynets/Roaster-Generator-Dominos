import { useState } from 'react'

export function PasswordChangePrompt({ fetchJson, onChanged, onSignOut }) {
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [state, setState] = useState({ busy: false, message: '' })
  const mismatch = confirmPassword.length > 0 && newPassword !== confirmPassword

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

  return <main className="page-shell centered-page login-page">
    <section className="login-card" role="dialog" aria-modal="true" aria-labelledby="password-change-title">
      <span className="shell-brand-mark login-mark" aria-hidden="true">RG</span>
      <header className="login-header">
        <h1 id="password-change-title">Choose a new password</h1>
        <p className="lead">Replace your initial password with one only you know. You will see this each time you sign in until you change it.</p>
      </header>
      <form className="login-form" onSubmit={submit}>
        <fieldset className="login-fieldset" disabled={state.busy}>
          <div className="form-group">
            <label htmlFor="new-password">New password</label>
            <input id="new-password" type="password" autoComplete="new-password" autoFocus required minLength={5} maxLength={128}
              aria-describedby="new-password-help"
              value={newPassword} onChange={(event) => setNewPassword(event.target.value)} />
            <small id="new-password-help" className="form-help">At least 5 characters.</small>
          </div>
          <div className="form-group">
            <label htmlFor="confirm-password">Confirm password</label>
            <input id="confirm-password" type="password" autoComplete="new-password" required minLength={5} maxLength={128}
              aria-invalid={mismatch || undefined}
              value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} />
            {mismatch && <small className="form-help form-help-error">Passwords don’t match yet.</small>}
          </div>
        </fieldset>
        {state.message && <p className="message error-message" role="alert">{state.message}</p>}
        <button type="submit" className="login-submit" disabled={state.busy}>{state.busy ? 'Saving…' : 'Change password'}</button>
        <button type="button" className="secondary-button login-secondary" disabled={state.busy} onClick={onSignOut}>Sign out</button>
      </form>
    </section>
  </main>
}
