import { useEffect, useState } from 'react'
import { fetchJson } from '../../api/client'
import { parseDate, personalRosterDateFormatter } from '../../lib/format'

export function PersonalRosterPanel({ weekOffset, setErrorPopup, realtimeKey = 0 }) {
  const [rosterState, setRosterState] = useState({ status: 'loading', roster: null, message: '' })

  useEffect(() => {
    let cancelled = false
    setRosterState({ status: 'loading', roster: null, message: '' })
    fetchJson(`/api/roster/me?weekOffset=${weekOffset}`, { cache: 'no-store' })
      .then((roster) => {
        if (!cancelled) setRosterState({ status: 'success', roster, message: '' })
      })
      .catch((error) => {
        if (cancelled) return
        setRosterState({ status: 'error', roster: null, message: error.message })
        setErrorPopup(error.message)
      })
    return () => { cancelled = true }
  }, [weekOffset, setErrorPopup, realtimeKey])

  const roster = rosterState.roster
  const days = roster ? Array.from({ length: 7 }, (_, dayOffset) => {
    const date = parseDate(roster.weekStart)
    date.setDate(date.getDate() + dayOffset)
    const dateValue = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
    return {
      date: dateValue,
      label: personalRosterDateFormatter.format(date),
      weekday: date.toLocaleDateString(undefined, { weekday: 'short' }),
      dayNumber: date.getDate(),
      shifts: roster.shifts.filter((shift) => shift.date === dateValue),
    }
  }) : []
  const shiftCount = days.reduce((total, day) => total + day.shifts.length, 0)

  return (
    <section className="personal-roster" aria-labelledby="personal-roster-heading">
      <div className="schedule-heading personal-roster-heading">
        <div>
          <span className="eyebrow">Your shifts</span>
          <h2 id="personal-roster-heading">My roster</h2>
          <p className="personal-roster-intro">Only your scheduled shifts are shown here.</p>
        </div>
      </div>

      {rosterState.status === 'loading' && <p className="message info-message" role="status">Loading your roster…</p>}
      {rosterState.status === 'error' && <p className="message error-message" role="alert">{rosterState.message}</p>}
      {rosterState.status === 'success' && !roster.hasPublishedRoster && (
        <div className="personal-roster-empty" role="status">
          <strong>No roster published yet</strong>
          <span>Your shifts will appear here when the roster for this week is ready.</span>
          {roster.approximateHours != null && <span>Approximate hours from your useful availability: {Number(roster.approximateHours).toFixed(1)}h</span>}
        </div>
      )}
      {rosterState.status === 'success' && roster.hasPublishedRoster && <>
        <div className="personal-roster-summary">
          <span>Scheduled this week</span>
          <strong>{roster.scheduledHours} {roster.scheduledHours === 1 ? 'hour' : 'hours'}</strong>
          <small>
            {shiftCount} {shiftCount === 1 ? 'shift' : 'shifts'}
            {roster.approximateHours != null && ` · approximate before scheduling ${Number(roster.approximateHours).toFixed(1)}h`}
          </small>
        </div>
        <ol className="my-week" aria-label="Your week">
          {days.map((day) => (
            <li className={`my-week-day${day.shifts.length ? ' is-working' : ''}`} key={day.date} aria-label={day.label}>
              <span className="my-week-date" aria-hidden="true">
                <small>{day.weekday}</small>
                <strong>{day.dayNumber}</strong>
              </span>
              <span className="my-week-detail">
                {day.shifts.length ? day.shifts.map((shift, index) => (
                  <span className="my-week-shift" key={`${shift.date}-${shift.startTime}-${index}`}>
                    <strong>
                      {shift.startTime}{shift.startDayOffset > 0 ? ' (+1)' : ''} – {shift.finishTime}{shift.finishDayOffset > 0 ? ' (+1)' : ''}
                    </strong>
                    <small>{shift.durationHours} {shift.durationHours === 1 ? 'hour' : 'hours'}</small>
                  </span>
                )) : <span className="my-week-off">Day off</span>}
              </span>
            </li>
          ))}
        </ol>
        {days.some((day) => day.shifts.some((shift) => shift.finishDayOffset > 0)) && (
          <p className="my-week-note">(+1) means the next day.</p>
        )}
      </>}
    </section>
  )
}
