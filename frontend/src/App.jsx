import { useEffect, useState } from 'react'

function App() {
  const [apiState, setApiState] = useState({ status: 'loading' })

  useEffect(() => {
    fetch('/api/hello')
      .then(async (response) => {
        const payload = await response.json()

        if (!response.ok) {
          throw new Error(payload.title || 'The API request failed.')
        }

        return payload
      })
      .then((payload) => setApiState({ status: 'success', payload }))
      .catch((error) => setApiState({ status: 'error', message: error.message }))
  }, [])

  return (
    <main className="page-shell">
      <section className="hero-card">
        <span className="eyebrow">Roaster Generator</span>
        <h1>Hello World</h1>
        <p className="lead">
          This React page is served by the frontend container through Traefik.
        </p>

        <div className={`status-card ${apiState.status}`}>
          <div className="status-dot" aria-hidden="true" />
          {apiState.status === 'loading' && <span>Connecting to the API…</span>}
          {apiState.status === 'error' && <span>API error: {apiState.message}</span>}
          {apiState.status === 'success' && (
            <div>
              <strong>{apiState.payload.message}</strong>
              <span>
                PostgreSQL: {apiState.payload.database} · server time:{' '}
                {new Date(apiState.payload.databaseTime).toLocaleString()}
              </span>
            </div>
          )}
        </div>
      </section>
    </main>
  )
}

export default App
