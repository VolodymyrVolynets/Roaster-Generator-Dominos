import { useEffect, useId, useRef, useState } from 'react'
import { usePresence } from '../hooks/usePresence'

// A single dialog host listens for requests, so any handler can await a confirmation
// the same way it used window.confirm / window.prompt.
let showRequest = null

function request(options) {
  if (!showRequest) {
    // Host not mounted (e.g. in tests): fall back to the browser dialogs.
    return Promise.resolve(options.input
      ? window.prompt(options.message, '')
      : window.confirm(options.message))
  }
  return new Promise((resolve) => showRequest({ ...options, resolve }))
}

/** Resolves to true when the user confirms. */
export function confirmAction({ title, message, confirmLabel = 'Confirm', destructive = false }) {
  return request({ title, message, confirmLabel, destructive })
}

/** Resolves to the entered text (possibly empty) when confirmed, or null when cancelled. */
export function promptAction({ title, message, inputLabel, confirmLabel = 'Confirm', destructive = false }) {
  return request({ title, message, confirmLabel, destructive, input: { label: inputLabel } })
}

export function ConfirmHost() {
  const [current, setCurrent] = useState(null)
  const [text, setText] = useState('')
  const presence = usePresence(Boolean(current), 160)
  const shown = useRef(null)
  const confirmRef = useRef(null)
  const cancelRef = useRef(null)
  const inputRef = useRef(null)
  const titleId = useId()
  const messageId = useId()
  if (current) shown.current = current

  useEffect(() => {
    showRequest = (next) => {
      setText('')
      setCurrent(next)
    }
    return () => { showRequest = null }
  }, [])

  const isShowing = Boolean(current) && presence.mounted

  useEffect(() => {
    if (!isShowing) return undefined
    const previousFocus = document.activeElement
    // The dialog is in the DOM now, so focus can move into it. Destructive actions
    // start on Cancel so pressing Enter never deletes by accident.
    const target = current.input ? inputRef : current.destructive ? cancelRef : confirmRef
    target.current?.focus()
    const onKey = (event) => {
      if (event.key === 'Escape') {
        // Close only the dialog, not a sheet underneath that also listens for Escape.
        event.stopImmediatePropagation()
        finish(false)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => {
      window.removeEventListener('keydown', onKey, true)
      previousFocus?.focus?.()
    }
    // finish only reads `current`, which this effect re-runs for.
  }, [isShowing, current])

  function finish(confirmed) {
    if (!current) return
    const { resolve, input } = current
    setCurrent(null)
    resolve(input ? (confirmed ? text.trim() : null) : confirmed)
  }

  if (!presence.mounted || !shown.current) return null
  const dialog = shown.current

  return (
    <div
      className={`confirm-backdrop${presence.closing ? ' is-closing' : ''}`}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) finish(false)
      }}
    >
      <form
        className="confirm-dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={messageId}
        onSubmit={(event) => {
          event.preventDefault()
          finish(true)
        }}
      >
        <h2 id={titleId}>{dialog.title}</h2>
        <p id={messageId}>{dialog.message}</p>
        {dialog.input && (
          <label className="confirm-input">
            <span>{dialog.input.label}</span>
            <input ref={inputRef} value={text} onChange={(event) => setText(event.target.value)} maxLength={500} />
          </label>
        )}
        <div className="confirm-actions">
          <button ref={cancelRef} type="button" className="secondary-button" onClick={() => finish(false)}>Cancel</button>
          <button ref={confirmRef} type="submit" className={dialog.destructive ? 'danger-button' : undefined}>
            {dialog.confirmLabel}
          </button>
        </div>
      </form>
    </div>
  )
}
