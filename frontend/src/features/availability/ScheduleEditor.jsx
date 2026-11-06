import { countAvailabilityDays, getAvailabilityLimitErrors, isAvailabilityTimeAllowed } from '../../lib/availabilityLimits'
import { AvailabilityHeatmap } from './DriverAvailabilityHeatmap'
import { dateFormatter, getShiftDuration, hours, parseDate } from '../../lib/format'

export function TimeSelector({ id, label, value, onChange, disabled = false, isTimeAllowed }) {
  const selectedHour = value ? value.split(':')[0] : ''

  return (
    <div className="time-selector" id={id} aria-label={label}>
      <select
        id={id ? `${id}-select` : undefined}
        aria-label={`${label} (24-hour format)`}
        value={selectedHour}
        onChange={(event) =>
          onChange(event.target.value ? `${event.target.value}:00` : '')
        }
        disabled={disabled}
      >
        <option value="">--:--</option>
        {hours.map((hour) => (
          <option key={hour} value={hour} disabled={isTimeAllowed ? !isTimeAllowed(`${hour}:00`) : false}>
            {hour}:00
          </option>
        ))}
      </select>
    </div>
  )
}

export function ApproximateHoursPreview({ approximateHours, capacityHours, label = 'Your estimated roster hours', detail }) {
  const available = approximateHours != null
  return (
    <section className={`approximate-hours-preview ${available ? '' : 'pending'}`} aria-label={label}>
      <div className="approximate-hours-value">
        <span>{label}</span>
        <strong>{available ? `${Number(approximateHours).toFixed(1)}h` : 'Pending'}</strong>
      </div>
      <p>
        {detail || (available
          ? `Calculated from demand, useful availability and how scarce each hour is.${capacityHours != null ? ` Useful availability capacity: ${Number(capacityHours).toFixed(1)}h.` : ''} The roster generator uses this as a fairness target after maximizing demand coverage without overstaffing, while hard shift rules still apply.`
          : 'Complete outside demand and save availability to calculate this estimate. It updates before the roster is generated.')}
      </p>
    </section>
  )
}

export function ScheduleEditor({
  schedule,
  saveState,
  saveSchedule,
  updateDay,
  resetDay,
  eyebrow = 'Weekly availability',
  lockedMessage = 'Availability for next week is locked from Saturday. Select the week after next to make changes.',
  estimateLabel,
}) {
  const canEdit = schedule?.canEdit !== false && !schedule?.availabilitySaveBlockedReason

  if (!schedule) {
    return null
  }
  const enteredDayCount = countAvailabilityDays(schedule.days)
  const maximumDays = schedule.maximumAvailabilityDaysPerWeek
  const maximumHours = schedule.maximumAvailabilityHoursPerDay
  const requiredBusyDates = schedule.requiredBusyDates || []
  const limitErrors = getAvailabilityLimitErrors(schedule)
  const dayLimitReached = maximumDays != null && enteredDayCount >= maximumDays

  return (
    <form className="schedule-form" onSubmit={saveSchedule}>
      <div className="schedule-heading">
        <div>
          <span className="eyebrow">{eyebrow}</span>
          <h2>{schedule.employeeName}</h2>
        </div>
        {maximumDays != null && (
          <span className={`schedule-progress${dayLimitReached ? ' is-full' : ''}`} aria-live="polite">
            <strong>{enteredDayCount}</strong>/{maximumDays} days
          </span>
        )}
      </div>

      {schedule.canEdit === false && (
        <p className="message info-message" role="status">{lockedMessage}</p>
      )}

      {schedule.availabilitySaveBlockedReason && (
        <p className="message info-message" role="status">{schedule.availabilitySaveBlockedReason}</p>
      )}

      {maximumDays != null && maximumHours != null && (
        <div className="schedule-rules" role="note">
          <ul aria-label="Availability limits">
            <li>Up to {maximumHours} hours a day</li>
            <li>Up to {maximumDays} days a week</li>
          </ul>
          <p>
            Overnight hours count toward the day they start. To pick a different day once you reach the limit, clear one first.
            {' '}An administrator can make exceptions.
          </p>
        </div>
      )}
      {limitErrors.length > 0 && (
        <p className="message error-message" role="alert">{limitErrors.join(' ')}</p>
      )}
      {requiredBusyDates.length > 0 && (
        <p className="message busy-day-message">
          Include at least one busy day:{' '}
          <strong>{requiredBusyDates.map((date) => schedule.days.find((day) => day.date === date)?.dayOfWeek || date).join(' or ')}</strong>.
          {' '}These days have the highest driver demand this week, so this also applies when you change saved availability.
          {' '}An administrator can make exceptions.
        </p>
      )}

      <div className="schedule-table" role="table" aria-label="Weekly availability">
        <div className="schedule-row schedule-header" role="row">
          <span role="columnheader">Day</span>
          <span role="columnheader">Start</span>
          <span role="columnheader">Finish</span>
          <span role="columnheader"><span className="visually-hidden">Clear</span></span>
        </div>

        {schedule.days.map((day) => {
          const isBusy = requiredBusyDates.includes(day.date)
          const duration = getShiftDuration(day.startTime, day.finishTime)
          const hasTimes = Boolean(day.startTime || day.finishTime)
          const locked = !canEdit || (dayLimitReached && !hasTimes)
          return (
            <div className={`schedule-row${isBusy ? ' busy-day-row' : ''}${duration ? ' has-hours' : ''}`} role="row" key={day.date}>
              <div className="day-cell" role="cell">
                <div className="day-cell-title">
                  <strong>{day.dayOfWeek}</strong>
                  <span>{dateFormatter.format(parseDate(day.date))}</span>
                </div>
                <div className="day-cell-badges">
                  {isBusy && <span className="busy-day-badge">Busy day</span>}
                  {duration && <span className="shift-duration">{duration}</span>}
                </div>
              </div>
              <div className="time-cell" role="cell">
                <label className="time-cell-label" htmlFor={`${day.date}-start-select`}>Start</label>
                <TimeSelector
                  id={`${day.date}-start`}
                  label={`${day.dayOfWeek} start time`}
                  value={day.startTime || ''}
                  onChange={(value) => updateDay(day.date, 'startTime', value)}
                  disabled={locked}
                  isTimeAllowed={(value) => isAvailabilityTimeAllowed(day, 'startTime', value, maximumHours)}
                />
              </div>
              <div className="time-cell" role="cell">
                <label className="time-cell-label" htmlFor={`${day.date}-finish-select`}>Finish</label>
                <TimeSelector
                  id={`${day.date}-finish`}
                  label={`${day.dayOfWeek} finish time`}
                  value={day.finishTime || ''}
                  onChange={(value) => updateDay(day.date, 'finishTime', value)}
                  disabled={locked}
                  isTimeAllowed={(value) => isAvailabilityTimeAllowed(day, 'finishTime', value, maximumHours)}
                />
              </div>
              <div className="reset-cell" role="cell">
                <button
                  type="button"
                  className="reset-button"
                  onClick={() => resetDay(day.date)}
                  disabled={!canEdit || !hasTimes}
                  aria-label={`Clear ${day.dayOfWeek}`}
                >
                  Clear
                </button>
              </div>
            </div>
          )
        })}
      </div>

      <div className="form-footer schedule-footer">
        <span className={`save-message ${saveState.status}`} aria-live="polite">
          {saveState.message || 'Leave a day empty to take it off.'}
        </span>
        <button type="submit" disabled={!canEdit || limitErrors.length > 0 || saveState.status === 'saving'}>
          {saveState.status === 'saving' ? 'Saving…' : 'Save availability'}
        </button>
      </div>

      <div className="schedule-insights">
        {schedule.heatmap && (
          <ApproximateHoursPreview
            approximateHours={schedule.approximateHours}
            capacityHours={schedule.approximateCapacityHours}
            label={estimateLabel}
          />
        )}

        {schedule.driversWithoutAvailability != null && (
          <p className="message info-message" role="status">
            <strong>{schedule.driversWithoutAvailability}</strong>{' '}
            {schedule.driversWithoutAvailability === 1 ? 'driver has' : 'drivers have'} no saved availability for this week
            {' '}(including you, if your week is empty). Estimates change as more drivers save theirs.
          </p>
        )}
      </div>

      <AvailabilityHeatmap heatmap={schedule.heatmap} weekStart={schedule.weekStart} />
    </form>
  )
}
