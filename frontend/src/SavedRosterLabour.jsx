import { useEffect, useState } from 'react'
import { formatLabourMoney, labourDisplay } from './rosterLabour'

const groups = [
  { key: 'drivers', label: 'Drivers' },
]

export default function SavedRosterLabour({ fetchJson, query, refreshKey, editing = false }) {
  const [state, setState] = useState({ status: 'loading', data: null, error: '' })
  const [retryKey, setRetryKey] = useState(0)

  useEffect(() => {
    let active = true
    setState({ status: 'loading', data: null, error: '' })
    fetchJson(`/api/admin/roster/labour?${query}`, { cache: 'no-store' })
      .then((data) => { if (active) setState({ status: 'success', data, error: '' }) })
      .catch((error) => { if (active) setState({ status: 'error', data: null, error: error.message }) })
    return () => { active = false }
  }, [fetchJson, query, refreshKey, retryKey])

  const data = state.data
  const display = labourDisplay

  return <section className="saved-roster-labour" aria-labelledby="saved-roster-labour-heading">
    <div className="section-heading">
      <div><span className="eyebrow">Actual saved shifts</span><h3 id="saved-roster-labour-heading">Roster labour</h3></div>
    </div>
    <p className="demand-help">
      Costs use each driver’s current hourly pay rate, with an extra 25% for hours worked on Sunday.
      Overnight shifts use the rate for each calendar hour and appear under their shift’s business day.
      Percentages compare labour cost with that day’s target sales from Demand; weekly percentages use weekly totals.
    </p>
    {editing && <p className="message info-message" role="status">Labour shows the saved roster. Save your shift changes to update these values.</p>}
    {state.status === 'loading' && <p className="message info-message" role="status">Loading actual roster labour…</p>}
    {state.status === 'error' && <div className="roster-labour-error" role="alert">
      <p className="message error-message">Could not load roster labour: {state.error || 'The server did not return a valid response.'}</p>
      <button type="button" className="secondary-button" onClick={() => setRetryKey((value) => value + 1)}>Retry labour calculation</button>
    </div>}
    {data && <>
      {data.warnings?.length > 0 && <div className="roster-labour-warning" role="status">
        <strong>Labour calculation notes</strong>
        <ul>{data.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul>
      </div>}
      <p className="roster-footnote">Week starting {data.weekStart} · Weekly target sales: <strong>{formatLabourMoney(data.targetSales)}</strong></p>
      <div className="roster-labour-cards">
        {groups.map(({ key, label }) => {
          const value = display(data[key], key)
          return <article className={`roster-labour-card roster-labour-card-${key}`} key={key}>
            <h4>{label}</h4>
            <strong className="roster-labour-cost">{value.cost}</strong>
            <span>{value.hours} scheduled · {value.percentage} of target sales</span>
            <small>{value.status}</small>
          </article>
        })}
      </div>
      <div className="demand-table-wrapper">
        <table className="saved-roster-table roster-labour-table">
          <caption className="visually-hidden">Daily labour from saved driver rosters</caption>
          <thead>
            <tr><th rowSpan="2" scope="col">Day</th><th rowSpan="2" scope="col">Target sales</th>
              {groups.map(({ key, label }) => <th key={key} colSpan="3" scope="colgroup">{label}</th>)}
            </tr>
            <tr>{groups.flatMap(({ key }) => [
              <th key={`${key}-hours`} scope="col">Hours</th>,
              <th key={`${key}-cost`} scope="col">Cost</th>,
              <th key={`${key}-percentage`} scope="col">Labour %</th>,
            ])}</tr>
          </thead>
          <tbody>{data.days.map((day) => <tr key={day.date}>
            <th scope="row">{day.label}<small>{day.date}</small></th>
            <td>{formatLabourMoney(day.targetSales)}</td>
            {groups.flatMap(({ key }) => {
              const value = display(day[key], key)
              return [
                <td key={`${key}-hours`}>{value.hours}</td>,
                <td key={`${key}-cost`}>{value.cost}{!day[key]?.isComplete && <small>{value.status}</small>}</td>,
                <td key={`${key}-percentage`}>{value.percentage}</td>,
              ]
            })}
          </tr>)}</tbody>
          <tfoot><tr><th scope="row">Week total</th><td>{formatLabourMoney(data.targetSales)}</td>
            {groups.flatMap(({ key }) => {
              const value = display(data[key], key)
              return [<td key={`${key}-hours`}>{value.hours}</td>, <td key={`${key}-cost`}>{value.cost}</td>, <td key={`${key}-percentage`}>{value.percentage}</td>]
            })}
          </tr></tfoot>
        </table>
      </div>
      <p className="roster-footnote">A dash means no saved driver roster or no positive sales target.</p>
    </>}
  </section>
}
