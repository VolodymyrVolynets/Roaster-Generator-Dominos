export async function fetchJson(url, options) {
  const response = await fetch(url, { ...options, credentials: 'include' })
  const body = await response.text()
  let payload = null
  const requestPath = url.split('?')[0]

  try {
    payload = body ? JSON.parse(body) : null
  } catch {
    payload = null
  }

  if (!response.ok) {
    const rawFieldErrors = payload?.errors && typeof payload.errors === 'object'
      ? payload.errors
      : {}
    const fieldErrors = Object.fromEntries(
      Object.entries(rawFieldErrors).map(([field, messages]) => [
        field.length > 0 ? `${field[0].toLowerCase()}${field.slice(1)}` : field,
        (Array.isArray(messages) ? messages : [messages])
          .filter((message) => message !== null && message !== undefined && String(message).trim())
          .map((message) => String(message)),
      ]),
    )
    const validationMessages = Object.values(fieldErrors).flat()
    const messages = [payload?.message, payload?.detail, payload?.title, ...validationMessages]
      .filter(Boolean)
      .filter((message, index, allMessages) => allMessages.indexOf(message) === index)

    if (response.status === 401 &&
        requestPath !== '/api/auth/login' &&
        requestPath !== '/api/auth/me' &&
        requestPath !== '/api/auth/logout') {
      window.dispatchEvent(new CustomEvent('auth-expired', {
        detail: { showMessage: requestPath !== '/api/auth/me' },
      }))
    }

    const message = response.status === 401 && requestPath !== '/api/auth/login'
      ? 'Your session has expired. Please sign in again.'
      : response.status === 403
        ? messages.join('\n') || 'You do not have permission to perform this action.'
        : messages.join('\n') || body || 'The API request failed.'
    const error = new Error(message)
    error.status = response.status
    error.fieldErrors = fieldErrors
    throw error
  }

  return payload
}
