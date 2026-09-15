import { useEffect, useId, useRef, useState } from 'react'
import {
  formatDateInput,
  getCalendarDays,
  getCalendarMonthStart,
  getMonday,
  getRelativeWeekLabel,
  getWeekOffsetFromDate,
  getWeekStartValue,
  parseDateInput,
} from './weekSelection'

const dateFormatter = new Intl.DateTimeFormat(undefined, {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
})
const monthFormatter = new Intl.DateTimeFormat(undefined, {
  month: 'long',
  year: 'numeric',
  timeZone: 'UTC',
})
const dayFormatter = new Intl.DateTimeFormat(undefined, {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  timeZone: 'UTC',
})
const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']

export function WeekSelector({
  weekOffset,
  onChange,
  disabled = false,
  allowAllWeeks = false,
  minimumWeekOffset = 1,
  maximumWeekOffset = 3,
  highlightedWeekStarts = [],
  highlightLabel = '',
}) {
  const [pickerOpen, setPickerOpen] = useState(false)
  const [visibleMonth, setVisibleMonth] = useState(() => getCalendarMonthStart(weekOffset))
  const pickerDialogRef = useRef(null)
  const dialogTitleId = useId()
  const selectedWeekStart = parseDateInput(getWeekStartValue(weekOffset))
  const calendarDays = getCalendarDays(visibleMonth)
  const highlightedWeeks = new Set(highlightedWeekStarts)

  useEffect(() => {
    if (pickerOpen) pickerDialogRef.current?.focus()
  }, [pickerOpen])

  function canChoose(offset) {
    return allowAllWeeks || (offset >= minimumWeekOffset && offset <= maximumWeekOffset)
  }

  function openPicker() {
    setVisibleMonth(getCalendarMonthStart(weekOffset))
    setPickerOpen(true)
  }

  function chooseDay(day) {
    const offset = getWeekOffsetFromDate(formatDateInput(day))
    if (!canChoose(offset)) return
    onChange(offset)
    setPickerOpen(false)
  }

  function chooseOffset(offset) {
    if (!canChoose(offset)) return
    onChange(offset)
    setPickerOpen(false)
  }

  function changeMonth(months) {
    setVisibleMonth((current) => new Date(Date.UTC(
      current.getUTCFullYear(),
      current.getUTCMonth() + months,
      1,
    )))
  }

  return (
    <>
      <div className="week-selector">
        <span>Week starting</span>
        <button
          type="button"
          className="week-picker-trigger"
          onClick={openPicker}
          disabled={disabled}
          aria-haspopup="dialog"
          aria-expanded={pickerOpen}
        >
          <span className="week-picker-trigger-copy">
            <strong>{getRelativeWeekLabel(weekOffset)}</strong>
            <small>{dateFormatter.format(selectedWeekStart)}</small>
          </span>
          <span className="week-picker-calendar-icon" aria-hidden="true">▦</span>
        </button>
      </div>

      {pickerOpen && (
        <div
          className="week-picker-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) setPickerOpen(false)
          }}
        >
          <section
            ref={pickerDialogRef}
            className="week-picker-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby={dialogTitleId}
            tabIndex="-1"
            onKeyDown={(event) => {
              if (event.key === 'Escape') setPickerOpen(false)
            }}
          >
            <div className="week-picker-dialog-heading">
              <div>
                <span className="eyebrow">Choose week</span>
                <h3 id={dialogTitleId}>{monthFormatter.format(visibleMonth)}</h3>
              </div>
              <button type="button" className="secondary-button week-picker-close" onClick={() => setPickerOpen(false)} aria-label="Close week picker">×</button>
            </div>

            <div className="week-picker-shortcuts" aria-label="Upcoming week shortcuts">
              {[1, 2, 3].map((offset) => (
                <button
                  type="button"
                  className={Number(weekOffset) === offset ? 'active' : ''}
                  key={offset}
                  onClick={() => chooseOffset(offset)}
                  disabled={!canChoose(offset)}
                >
                  {getRelativeWeekLabel(offset)}
                </button>
              ))}
            </div>

            <div className="week-picker-month-nav">
              <button type="button" className="secondary-button week-picker-nav-button" onClick={() => changeMonth(-1)} aria-label="Previous month">←</button>
              <button type="button" className="secondary-button week-picker-current" onClick={() => chooseOffset(0)} disabled={!canChoose(0)}>Current week</button>
              <button type="button" className="secondary-button week-picker-nav-button" onClick={() => changeMonth(1)} aria-label="Next month">→</button>
            </div>

            <p className="week-picker-help">
              {allowAllWeeks
                ? 'Choose any day. The complete Monday–Sunday week will be shown everywhere.'
                : `Choose a week from ${getRelativeWeekLabel(minimumWeekOffset).toLowerCase()} through ${getRelativeWeekLabel(maximumWeekOffset).toLowerCase()}.`}
            </p>
            {highlightLabel && (
              <p className="week-picker-marker-legend"><span aria-hidden="true" />{highlightLabel}</p>
            )}

            <div className="week-picker-grid week-picker-weekdays" aria-hidden="true">
              {weekdays.map((day) => <span key={day}>{day}</span>)}
            </div>
            <div className="week-picker-grid" aria-label="Select a week">
              {calendarDays.map((day) => {
                const offset = getWeekOffsetFromDate(formatDateInput(day))
                const weekStart = formatDateInput(getMonday(day))
                const inVisibleMonth = day.getUTCMonth() === visibleMonth.getUTCMonth()
                const inSelectedWeek = getMonday(day).getTime() === selectedWeekStart.getTime()
                const highlighted = highlightedWeeks.has(weekStart)
                return (
                  <button
                    type="button"
                    className={`week-picker-day ${inVisibleMonth ? '' : 'outside-month'} ${inSelectedWeek ? 'selected-week' : ''} ${highlighted ? 'has-data' : ''}`}
                    key={formatDateInput(day)}
                    onClick={() => chooseDay(day)}
                    aria-label={dayFormatter.format(day)}
                    aria-pressed={inSelectedWeek}
                    disabled={!canChoose(offset)}
                  >
                    <span>{day.getUTCDate()}</span>
                    {highlighted && <i aria-hidden="true" />}
                  </button>
                )
              })}
            </div>
          </section>
        </div>
      )}
    </>
  )
}

export function AreaSelector({ value, onChange, outsideValue = 'outside', disabled = false }) {
  const options = [
    { value: outsideValue, name: 'Outside', detail: 'Drivers', badge: 'OUT' },
    { value: 'inside', name: 'Inside', detail: 'Managers & in-store', badge: 'IN' },
  ]

  return (
    <fieldset className="area-selector" disabled={disabled}>
      <legend>Work area</legend>
      <div className="area-selector-options" role="radiogroup" aria-label="Work area">
        {options.map((option) => {
          const selected = value === option.value
          return (
            <button
              type="button"
              role="radio"
              aria-checked={selected}
              className={selected ? 'selected' : ''}
              key={option.value}
              onClick={() => onChange(option.value)}
            >
              <span className="area-selector-badge" aria-hidden="true">{option.badge}</span>
              <span className="area-selector-copy"><strong>{option.name}</strong><small>{option.detail}</small></span>
              <span className="area-selector-check" aria-hidden="true">✓</span>
            </button>
          )
        })}
      </div>
    </fieldset>
  )
}
