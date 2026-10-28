import { useEffect, useRef } from 'react'
import { Icon } from './Icon'
import { usePresence } from '../hooks/usePresence'

export function ErrorPopup({ message, onClose }) {
  const { mounted, closing } = usePresence(Boolean(message), 160)
  // Keep showing the last message while the toast animates out.
  const lastMessage = useRef(message)
  if (message) lastMessage.current = message

  useEffect(() => {
    if (!message) return undefined
    const closeOnEscape = (event) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [message, onClose])

  if (!mounted) {
    return null
  }

  return (
    <div className={`error-popup${closing ? ' is-closing' : ''}`} role="alert" aria-live="assertive">
      <span className="error-popup-icon" aria-hidden="true"><Icon name="alert" size={18} /></span>
      <div className="error-popup-body">
        <div className="error-popup-header">
          <strong>Something went wrong</strong>
          <button
            type="button"
            className="error-popup-close"
            aria-label="Close error message"
            onClick={onClose}
          >
            <Icon name="close" size={16} />
          </button>
        </div>
        <p>{lastMessage.current}</p>
      </div>
    </div>
  )
}
