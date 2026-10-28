import { useEffect, useId, useRef, useState } from 'react'
import { formatDateInput, getCalendarDays, parseDateInput } from '../lib/weekSelection'
import { Icon } from './Icon'
import { usePresence } from '../hooks/usePresence'

const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']
const triggerFormatter = new Intl.DateTimeFormat(undefined, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
const monthFormatter = new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric', timeZone: 'UTC' })
const dayLabelFormatter = new Intl.DateTimeFormat(undefined, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' })

function todayValue() {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

function addDays(value, days) {
  const date = parseDateInput(value)
  date.setUTCDate(date.getUTCDate() + days)
  return formatDateInput(date)
}

function monthStartOf(value) {
  const date = parseDateInput(value)
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1))
}

function isAllowed(value, min, max) {
  return (!min || value >= min) && (!max || value <= max)
}

/**
 * Calendar date field that replaces the browser's native date input.
 * Value and min/max use the same YYYY-MM-DD strings as <input type="date">.
 */
export function DatePicker({
  id,
  value,
  onChange,
  min,
  max,
  required = false,
  disabled = false,
  placeholder = 'Choose a date',
  clearable = false,
  describedBy,
}) {
  const generatedId = useId()
  const dialogTitleId = `${generatedId}-title`
  const [open, setOpen] = useState(false)
  const popover = usePresence(open, 150)
  const [visibleMonth, setVisibleMonth] = useState(() => monthStartOf(value || todayValue()))
  const [focusedValue, setFocusedValue] = useState(value || todayValue())
  const rootRef = useRef(null)
  const gridRef = useRef(null)
  const triggerRef = useRef(null)
  const today = todayValue()

  useEffect(() => {
    if (!open) return undefined
    const closeOnOutside = (event) => {
      if (!rootRef.current?.contains(event.target)) setOpen(false)
    }
    document.addEventListener('mousedown', closeOnOutside)
    document.addEventListener('touchstart', closeOnOutside)
    return () => {
      document.removeEventListener('mousedown', closeOnOutside)
      document.removeEventListener('touchstart', closeOnOutside)
    }
  }, [open])

  useEffect(() => {
    if (open) gridRef.current?.querySelector(`[data-date="${focusedValue}"]`)?.focus()
  }, [open, focusedValue, visibleMonth])

  function openPicker() {
    const start = value || (isAllowed(today, min, max) ? today : min || max || today)
    setFocusedValue(start)
    setVisibleMonth(monthStartOf(start))
    setOpen(true)
  }

  function close({ restoreFocus = true } = {}) {
    setOpen(false)
    if (restoreFocus) triggerRef.current?.focus()
  }

  function choose(nextValue) {
    if (!isAllowed(nextValue, min, max)) return
    onChange(nextValue)
    close()
  }

  function moveFocus(nextValue) {
    setFocusedValue(nextValue)
    const nextMonth = monthStartOf(nextValue)
    if (nextMonth.getTime() !== visibleMonth.getTime()) setVisibleMonth(nextMonth)
  }

  function shiftMonth(months) {
    const next = new Date(Date.UTC(visibleMonth.getUTCFullYear(), visibleMonth.getUTCMonth() + months, 1))
    setVisibleMonth(next)
    setFocusedValue(formatDateInput(next))
  }

  function onGridKeyDown(event) {
    const moves = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }
    if (moves[event.key] != null) {
      event.preventDefault()
      moveFocus(addDays(focusedValue, moves[event.key]))
    } else if (event.key === 'PageUp' || event.key === 'PageDown') {
      event.preventDefault()
      shiftMonth(event.key === 'PageUp' ? -1 : 1)
    }
  }

  const days = getCalendarDays(visibleMonth).map((date) => formatDateInput(date))
  const visibleMonthIndex = visibleMonth.getUTCMonth()
  const todayAllowed = isAllowed(today, min, max)

  return (
    <div className={`date-picker${popover.mounted ? ' is-open' : ''}${popover.closing ? ' is-closing' : ''}`} ref={rootRef}>
      <button
        type="button"
        id={id}
        ref={triggerRef}
        className={`date-picker-trigger${value ? '' : ' is-empty'}`}
        onClick={() => (open ? close() : openPicker())}
        disabled={disabled}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-describedby={describedBy}
      >
        <span>{value ? triggerFormatter.format(parseDateInput(value)) : placeholder}</span>
        <Icon name="calendar" size={18} />
      </button>
      {/* Lets the browser's built-in "required" check cover this field. */}
      <input
        className="date-picker-proxy"
        tabIndex={-1}
        aria-hidden="true"
        required={required}
        value={value || ''}
        onChange={() => {}}
        onFocus={() => triggerRef.current?.focus()}
      />

      {popover.mounted && (
        <div
          className="date-picker-popover"
          inert={popover.closing ? true : undefined}
          role="dialog"
          aria-modal="false"
          aria-labelledby={dialogTitleId}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.stopPropagation()
              close()
            }
          }}
        >
          <div className="date-picker-header">
            <button type="button" className="date-picker-nav" onClick={() => shiftMonth(-1)} aria-label="Previous month">‹</button>
            <strong id={dialogTitleId} aria-live="polite">{monthFormatter.format(visibleMonth)}</strong>
            <button type="button" className="date-picker-nav" onClick={() => shiftMonth(1)} aria-label="Next month">›</button>
          </div>

          <div className="date-picker-weekdays" aria-hidden="true">
            {weekdays.map((day) => <span key={day}>{day.slice(0, 2)}</span>)}
          </div>

          <div className="date-picker-grid" role="grid" ref={gridRef} onKeyDown={onGridKeyDown}>
            {days.map((day) => {
              const date = parseDateInput(day)
              const allowed = isAllowed(day, min, max)
              const classes = ['date-picker-day']
              if (date.getUTCMonth() !== visibleMonthIndex) classes.push('outside-month')
              if (day === today) classes.push('is-today')
              if (day === value) classes.push('is-selected')
              return (
                <button
                  type="button"
                  key={day}
                  data-date={day}
                  className={classes.join(' ')}
                  disabled={!allowed}
                  tabIndex={day === focusedValue ? 0 : -1}
                  aria-label={dayLabelFormatter.format(date)}
                  aria-pressed={day === value}
                  onClick={() => choose(day)}
                  onFocus={() => setFocusedValue(day)}
                >
                  {date.getUTCDate()}
                </button>
              )
            })}
          </div>

          <div className="date-picker-footer">
            <button type="button" className="secondary-button" onClick={() => choose(today)} disabled={!todayAllowed}>Today</button>
            {clearable && value && (
              <button type="button" className="secondary-button" onClick={() => { onChange(''); close() }}>Clear</button>
            )}
            <button type="button" className="secondary-button date-picker-done" onClick={() => close()}>Done</button>
          </div>
        </div>
      )}
    </div>
  )
}
