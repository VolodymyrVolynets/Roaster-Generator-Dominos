import { useState } from 'react'

export function LoginView({ onLogin, error, isSubmitting }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)

  function submit(event) {
    event.preventDefault()
    onLogin(username.trim(), password)
  }

  return (
    <main className="page-shell centered-page login-page">
      <section className="login-card" aria-labelledby="login-heading">
        <span className="shell-brand-mark login-mark" aria-hidden="true">RG</span>
        <header className="login-header">
          <h1 id="login-heading">Roaster Generator</h1>
          <p className="lead">Sign in with your employee number or administrator account.</p>
        </header>

        <form className="login-form" onSubmit={submit} noValidate={false}>
          <div className="form-group">
            <label htmlFor="login-username">Username</label>
            <input
              id="login-username"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              autoComplete="username"
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck="false"
              placeholder="Employee number"
              required
              autoFocus
            />
          </div>
          <div className="form-group">
            <label htmlFor="login-password">Password</label>
            <div className="password-field">
              <input
                id="login-password"
                type={showPassword ? 'text' : 'password'}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                autoComplete="current-password"
                placeholder="Password"
                required
              />
              <button
                type="button"
                className="password-toggle"
                onClick={() => setShowPassword((current) => !current)}
                aria-pressed={showPassword}
                aria-controls="login-password"
              >
                {showPassword ? 'Hide' : 'Show'}
              </button>
            </div>
          </div>
          {error && <p className="message error-message" role="alert">{error}</p>}
          <button type="submit" className="login-submit" disabled={isSubmitting}>
            {isSubmitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
        <p className="login-footnote">Forgot your password? Ask your store manager to reset it.</p>
      </section>
    </main>
  )
}
