import { useId, useState } from 'react'
import {
  availabilityHeatmapDetail,
  availabilityHeatmapStatus,
  buildAvailabilityHeatmapDays,
  buildAvailabilityHeatmapGrid,
} from './availabilityHeatmap.js'

const parseDate = (date) => new Date(`${date}T12:00:00`)
const dateFormatter = new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short' })
const dayFormatter = new Intl.DateTimeFormat(undefined, { weekday: 'long', day: 'numeric', month: 'short' })
const hourLabel = (hour) => `${String(hour % 24).padStart(2, '0')}:00`

export function AvailabilityHeatmap({ heatmap, weekStart }) {
  const id = useId()
  const [selectedDayIndex, setSelectedDayIndex] = useState(0)

  if (!heatmap) return null
  if (!heatmap.demandPlanExists || !heatmap.slots?.length || !weekStart) {
    return <p className="message info-message availability-heatmap-message">{heatmap.message}</p>
  }

  const grid = buildAvailabilityHeatmapGrid(heatmap, weekStart)
  const days = buildAvailabilityHeatmapDays(grid)
  const selectedDay = days[selectedDayIndex]
  const explanation = `${heatmap.message} Counts include current saved availability and refresh after saving.`

  return (
    <section className="availability-heatmap" aria-labelledby={`${id}-heading`}>
      <div className="availability-heatmap-heading">
        <div>
          <span className="eyebrow">Best times to offer</span>
          <h3 id={`${id}-heading`}>Driver availability heatmap</h3>
          <p className="heatmap-desktop-description">{explanation}</p>
          <p className="heatmap-mobile-intro">Choose a day to see where more drivers are needed.</p>
        </div>
        <div className="availability-heatmap-legend" aria-label="Heatmap legend">
          <span className="heatmap-legend-shortage">Needs drivers</span>
          <span className="heatmap-legend-tight">No spare</span>
          <span className="heatmap-legend-limited">One spare</span>
          <span className="heatmap-legend-covered">Covered</span>
        </div>
      </div>

      <div className="heatmap-mobile-view">
        <div className="heatmap-day-picker" role="group" aria-label="Heatmap day">
          {days.map((day, index) => {
            const date = parseDate(day.date)
            const detail = day.shortageHours > 0
              ? `${day.shortageHours} ${day.shortageHours === 1 ? 'hour needs' : 'hours need'} drivers`
              : day.slots.length ? 'No driver shortages' : 'No drivers required'
            return <button type="button" key={day.date}
              className={`heatmap-day-button${index === selectedDayIndex ? ' is-selected' : ''}`}
              aria-pressed={index === selectedDayIndex}
              aria-controls={`${id}-day`}
              aria-label={`${dayFormatter.format(date)}. ${detail}`}
              onClick={() => setSelectedDayIndex(index)}>
              <span>{date.toLocaleDateString(undefined, { weekday: 'short' }).slice(0, 2)}</span>
              <strong>{date.getDate()}</strong>
              <i className={`heatmap-day-dot heatmap-${day.level}`} aria-hidden="true" />
            </button>
          })}
        </div>

        <div id={`${id}-day`} className="heatmap-day-content">
          <div className="heatmap-day-summary" role="status" aria-live="polite" aria-atomic="true">
            <h4>{dayFormatter.format(parseDate(selectedDay.date))}</h4>
            <p className={selectedDay.shortageHours > 0 ? 'has-shortage' : ''}>
              {selectedDay.shortageHours > 0
                ? `${selectedDay.shortageHours} ${selectedDay.shortageHours === 1 ? 'hour needs' : 'hours need'} more drivers`
                : selectedDay.slots.length ? 'No driver shortages' : 'No drivers required'}
            </p>
          </div>
          {selectedDay.slots.length ? (
            <ul className="heatmap-hour-cards" aria-label="Hourly driver availability">
              {selectedDay.slots.map((slot) => (
                <li className={`heatmap-hour-card heatmap-${slot.level}`} key={slot.hour}>
                  <div className="heatmap-card-time">
                    <strong>{slot.startTime || hourLabel(slot.hour)}</strong>
                    {slot.hour >= 24 && <small>Next day</small>}
                  </div>
                  <strong className="heatmap-card-status">{availabilityHeatmapStatus(slot)}</strong>
                  <span className="heatmap-card-count">{slot.availableDrivers} available · {slot.requiredDrivers} needed</span>
                </li>
              ))}
            </ul>
          ) : <p className="heatmap-day-empty">No required driver hours to show for this day. Try another day.</p>}
        </div>
        <details className="heatmap-help">
          <summary>How to read this heatmap</summary>
          <p>Each card represents one hour. After-midnight hours belong to the previous business day. {explanation}</p>
        </details>
      </div>

      <div className="availability-heatmap-scroll">
        <table>
          <caption className="visually-hidden">Required drivers compared with saved driver availability by hour</caption>
          <thead><tr>
            <th scope="col">Time</th>
            {grid.dates.map((date) => <th scope="col" key={date}>
              <span>{parseDate(date).toLocaleDateString(undefined, { weekday: 'short' })}</span>
              <small>{dateFormatter.format(parseDate(date))}</small>
            </th>)}
          </tr></thead>
          <tbody>{grid.hours.map((hour) => <tr key={hour}>
            <th scope="row">{hourLabel(hour)}{hour >= 24 && <small>+1 day</small>}</th>
            {grid.dates.map((date) => {
              const slot = grid.slots.get(`${date}:${hour}`)
              const detail = availabilityHeatmapDetail(slot)
              return <td key={date} className={slot ? `heatmap-${slot.level}` : 'heatmap-empty'} title={detail}>
                {slot ? <><strong>{slot.availableDrivers}/{slot.requiredDrivers}</strong><small>{slot.shortageDrivers > 0 ? `+${slot.shortageDrivers} needed` : 'available / need'}</small></> : <span>—</span>}
                <span className="visually-hidden">{detail}</span>
              </td>
            })}
          </tr>)}</tbody>
        </table>
      </div>
    </section>
  )
}
