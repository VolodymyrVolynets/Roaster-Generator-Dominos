import { useEffect, useRef, useState } from 'react'
import { AreaSelector } from '../../components/SelectionControls'
import { calculateDemandLabour, recalculateDemandPlan, recalculateDemandValue } from '../../lib/demandPlanning'
import { getWeekStartValue } from '../../lib/weekSelection'
import { fetchJson } from '../../api/client'
import { confirmAction } from '../../components/ConfirmDialog'

export function DemandManager({
  setErrorPopup,
  employees = [],
  canEdit = true,
  weekOffset,
  demandKind,
  onDemandKindChange,
  onWeekSelectionLockChange,
  realtimeKey = 0,
}) {
  const [plans, setPlans] = useState([])
  const [selectedPlanId, setSelectedPlanId] = useState('')
  const [plan, setPlan] = useState(null)
  const [selectedDemandDayPosition, setSelectedDemandDayPosition] = useState(0)
  const [name, setName] = useState('Weekly demand')
  const [pasteContent, setPasteContent] = useState('')
  const [status, setStatus] = useState({ status: 'idle', message: '' })
  const [dirty, setDirty] = useState(false)
  const loadedPlanIdRef = useRef('')
  const receivedPlan = (payload) => ({ ...payload, labourEstimateCurrent: true })
  const weekStart = getWeekStartValue(weekOffset)

  useEffect(() => {
    onWeekSelectionLockChange('demand', status.status === 'saving')
    return () => onWeekSelectionLockChange('demand', false)
  }, [onWeekSelectionLockChange, status.status])

  useEffect(() => {
    fetchJson('/api/admin/demand')
      .then(setPlans)
      .catch((error) => setErrorPopup(error.message))
  }, [setErrorPopup, realtimeKey])

  useEffect(() => {
    const selected = plans.find((item) => item.demandKind === demandKind && item.weekStart === weekStart)
    setSelectedPlanId(selected ? String(selected.id) : '')
  }, [demandKind, plans, weekStart])

  useEffect(() => {
    let active = true
    if (!selectedPlanId) {
      setPlan(null)
      setName('Weekly demand')
      setDirty(false)
      loadedPlanIdRef.current = ''
      setStatus({ status: 'idle', message: '' })
      return undefined
    }
    if (dirty && loadedPlanIdRef.current === selectedPlanId) {
      setStatus({
        status: 'warning',
        message: 'Planning inputs changed in another session. Save or reload before continuing.',
      })
      return undefined
    }

    fetchJson(`/api/admin/demand/${selectedPlanId}`)
      .then((payload) => {
        if (!active) return
        setPlan(receivedPlan(payload))
        loadedPlanIdRef.current = String(payload.id)
        setSelectedDemandDayPosition(payload.columns[0]?.position ?? 0)
        setName(payload.name)
        setDirty(false)
        setStatus({ status: 'idle', message: '' })
      })
      .catch((error) => { if (active) setErrorPopup(error.message) })
    return () => { active = false }
  }, [selectedPlanId, setErrorPopup, realtimeKey])

  function updateDemandValue(hour, position, field, rawValue) {
    const numericValue = rawValue === '' ? null : Number(rawValue)
    setPlan((current) => ({
      ...current,
      labourEstimateCurrent: false,
      rows: current.rows.map((row) => {
        if (row.hour !== hour) {
          return row
        }

        const existing = row.values.find((item) => item.position === position)
        let nextValue = { ...existing, position, [field]: numericValue }
        if (field === 'deliveries' || field === 'pizzas') {
          nextValue = recalculateDemandValue(nextValue, current,
            field === 'pizzas' ? ['insideDemand'] : ['demand'])
        }

        return {
          ...row,
          values: [
            ...row.values.filter((item) => item.position !== position),
            nextValue,
          ].sort((left, right) => left.position - right.position),
        }
      }),
    }))
    setDirty(true)
    setStatus({ status: 'idle', message: '' })
  }

  function updateTargetSales(position, rawValue) {
    const targetSales = rawValue === '' ? 0 : Number(rawValue)
    setPlan((current) => ({
      ...current,
      columns: current.columns.map((column) => (
        column.position === position ? { ...column, targetSales } : column
      )),
    }))
    setDirty(true)
    setStatus({ status: 'idle', message: '' })
  }

  function updatePlanningSetting(field, rawValue) {
    const value = rawValue === '' ? '' : Number(rawValue)
    setPlan((current) => {
      const updated = { ...current, [field]: value, labourEstimateCurrent: false }
      return field === 'deliveriesPerDriverHour'
        ? recalculateDemandPlan(updated, ['demand'])
        : field === 'pizzasPerInsideHour'
          ? recalculateDemandPlan(updated, ['insideDemand']) : updated
    })
    setDirty(true)
    setStatus({ status: 'idle', message: '' })
  }

  async function importPaste() {
    setStatus({ status: 'saving', message: '' })

    try {
      const payload = await fetchJson('/api/admin/demand/paste', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, weekStart, demandKind, content: pasteContent }),
      })
      setPlan(receivedPlan(payload))
      setSelectedDemandDayPosition(payload.columns[0]?.position ?? 0)
      setName(payload.name)
      setDirty(false)
      setSelectedPlanId(String(payload.id))
      setPasteContent('')
      setStatus({ status: 'success', message: 'Demand imported.' })
      await refreshPlans()
    } catch (error) {
      setStatus({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function importFile(event) {
    const file = event.target.files?.[0]
    event.target.value = ''

    if (!file) {
      return
    }

    setStatus({ status: 'saving', message: '' })
    const formData = new FormData()
    formData.append('file', file)
    formData.append('name', name)
    formData.append('weekStart', weekStart)
    formData.append('demandKind', demandKind)

    try {
      const payload = await fetchJson('/api/admin/demand/upload', {
        method: 'POST',
        body: formData,
      })
      setPlan(receivedPlan(payload))
      setSelectedDemandDayPosition(payload.columns[0]?.position ?? 0)
      setName(payload.name)
      setDirty(false)
      setSelectedPlanId(String(payload.id))
      setStatus({ status: 'success', message: 'Excel demand imported.' })
      await refreshPlans()
    } catch (error) {
      setStatus({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function refreshPlans() {
    const payload = await fetchJson('/api/admin/demand')
    setPlans(payload)
  }

  async function savePlan(event, recalculateDemand = false) {
    event?.preventDefault()
    setStatus({ status: 'saving', message: '' })

    try {
      const payload = await fetchJson(`/api/admin/demand/${selectedPlanId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name,
          weekStart,
          demandKind,
          deliveriesPerDriverHour: Number(plan.deliveriesPerDriverHour ?? 2.7),
          pizzasPerInsideHour: Number(plan.pizzasPerInsideHour ?? 20),
          recalculateDemand,
          columns: plan.columns.map((column) => ({
            position: column.position,
            label: column.label,
            targetSales: Number(column.targetSales ?? 0),
          })),
          rows: plan.rows.map((row) => ({
            hour: row.hour,
            values: row.values.map((value) => ({
              position: value.position,
              deliveries: value.deliveries ?? null,
              demand: value.demand,
              pizzas: value.pizzas ?? null,
              insideDemand: value.insideDemand,
            })),
          })),
        }),
      })
      setPlan(receivedPlan(payload))
      setDirty(false)
      setStatus({ status: 'success', message: recalculateDemand
        ? `${demandKind === 'inside' ? 'Inside' : 'Outside'} demand recalculated and saved.`
        : 'Demand changes saved.' })
      await refreshPlans()
    } catch (error) {
      setStatus({ status: 'error', message: error.message })
      setErrorPopup(error.message)
    }
  }

  async function deletePlan() {
    if (!selectedPlanId || !(await confirmAction({
      title: 'Delete demand plan?',
      message: 'This week’s demand, sales targets and productivity settings for this work area will be deleted.',
      confirmLabel: 'Delete plan',
      destructive: true,
    }))) {
      return
    }

    try {
      await fetchJson(`/api/admin/demand/${selectedPlanId}`, { method: 'DELETE' })
      const remainingPlans = plans.filter((item) => String(item.id) !== selectedPlanId)
      setPlans(remainingPlans)
      setSelectedPlanId('')
      setPlan(null)
      setDirty(false)
      setStatus({ status: 'success', message: 'Demand plan deleted.' })
    } catch (error) {
      setErrorPopup(error.message)
    }
  }

  const selectedDemandColumn = plan?.columns.find(
    (column) => column.position === selectedDemandDayPosition,
  )
  const demandSummary = calculateDemandLabour(plan, employees)
  const isInsideDemand = demandKind === 'inside'
  const workloadLabel = isInsideDemand ? 'Pizzas' : 'Deliveries'
  const staffLabel = isInsideDemand ? 'Inside employees' : 'Drivers'
  const productivityField = isInsideDemand ? 'pizzasPerInsideHour' : 'deliveriesPerDriverHour'
  const productivityLabel = isInsideDemand ? 'Pizzas per inside employee-hour' : 'Deliveries per hour per driver'
  const dailyStaffingRows = demandSummary.days
  const selectedDaySummary = dailyStaffingRows.find((day) => day.position === selectedDemandDayPosition)
  const selectedDayHourlyRows = selectedDaySummary?.hourly || []
  const formatMoney = (value) => value == null ? '—' : `€${Number(value).toFixed(2)}`
  const formatPercentage = (value) => value == null ? '—' : `${Number(value).toFixed(2)}%`
  const formatMetric = (value, digits = 2) => value == null ? '—' : Number(value).toFixed(digits)
  const formatSignedMoney = (value) => {
    if (value == null) return '—'
    const amount = Number(value)
    return `${amount > 0 ? '+' : amount < 0 ? '−' : ''}€${Math.abs(amount).toFixed(2)}`
  }
  const formatSignedMetric = (value) => {
    if (value == null) return '—'
    const amount = Number(value)
    return `${amount > 0 ? '+' : amount < 0 ? '−' : ''}${Math.abs(amount).toFixed(2)}`
  }
  const staffingStatus = (row) => {
    if (row.staffingStatus === 'under') return { label: `Under by ${Math.abs(row.driverDifference)}`, tone: 'under' }
    if (row.staffingStatus === 'above') return { label: `Above by ${row.driverDifference}`, tone: 'above' }
    if (row.staffingStatus === 'matched') return { label: 'Whole need met', tone: 'matched' }
    return { label: 'Missing data', tone: 'unknown' }
  }
  const demandFields = isInsideDemand
    ? [['pizzas', 'Pizzas'], ['insideDemand', 'Inside employees']]
    : [['deliveries', 'Deliveries'], ['demand', 'Drivers']]
  function demandCell(row, column, field, label) {
    const value = row.values.find((item) => item.position === column.position) || {}
    const missing = value.isOpen && value[field] == null
    const isDriverCount = field === 'demand'
    const isReadOnly = !canEdit || value.isOpen === false
    const maximum = field === 'demand' ? 100000000 : 1000000
    const numericValue = Number(value[field])
    const currentValue = Number.isFinite(numericValue) && value[field] != null ? numericValue : 0
    const hourLabel = `${column.label} ${String(row.hour).padStart(2, '0')}:00`
    const input = <input
      className={isDriverCount ? 'demand-stepper-input' : undefined}
      type="number" min="0" max={maximum}
      step={isInsideDemand || field === 'demand' ? '1' : '0.01'} value={value[field] ?? ''}
      onChange={(event) => updateDemandValue(row.hour, column.position, field, event.target.value)}
      readOnly={isReadOnly} aria-readonly={isReadOnly}
      placeholder={value.isOpen === false ? 'Closed' : '—'}
      aria-label={`${hourLabel} ${label}`}
      title={missing ? `${label} missing for this open hour` : undefined} />

    return <td key={`${row.hour}-${column.position}-${field}`} className={missing ? 'demand-missing-value' : undefined}>
      {isDriverCount && !isReadOnly ? <div className="demand-stepper" role="group" aria-label={`${hourLabel} drivers`}>
        <button type="button" className="demand-stepper-button demand-stepper-remove"
          disabled={currentValue <= 0}
          onClick={() => updateDemandValue(row.hour, column.position, field, String(Math.max(0, currentValue - 1)))}
          aria-label={`Remove one driver at ${hourLabel}`} title="Remove one driver">−</button>
        {input}
        <button type="button" className="demand-stepper-button demand-stepper-add"
          disabled={currentValue >= maximum}
          onClick={() => updateDemandValue(row.hour, column.position, field, String(Math.min(maximum, currentValue + 1)))}
          aria-label={`Add one driver at ${hourLabel}`} title="Add one driver">+</button>
      </div> : input}
    </td>
  }

  return (
    <section className="admin-tools demand-tools">
      <div className="section-heading">
        <div>
          <span className="eyebrow">Administration</span>
          <h2>Demand input</h2>
        </div>
        <div className="selection-toolbar demand-scenario-controls">
          <AreaSelector value={demandKind} onChange={onDemandKindChange} disabled={status.status === 'saving'} />
        </div>
      </div>

      {!canEdit && (
        <p className="message info-message" role="status">
          Read-only view for managers. Only administrators can create or change demand plans.
        </p>
      )}

      {plan && (
        <nav className="demand-jump-links" aria-label="Demand sections">
          <a href="#demand-summary">Summary</a>
          <a href="#demand-daily">By day</a>
          <a href="#demand-hourly-heading">By hour</a>
          <a href="#demand-edit">Edit demand</a>
        </nav>
      )}

      {canEdit && (
        <details className="demand-import" open={!plan}>
          <summary>{plan ? 'Import a new table' : 'Import demand'}</summary>
          <p className="demand-help">
            Paste or upload the weekly Excel/CSV/table template. {isInsideDemand
              ? 'Inside demand is pizzas ÷ pizzas per inside employee-hour, rounded up.'
              : 'Outside demand is deliveries ÷ deliveries per driver-hour, rounded up.'}
            {' '}Hours run from 06–23 followed by next-day 00–05. Imports keep saved sales targets and productivity settings.
          </p>
          <label className="demand-paste-label">
            Paste demand table
            <textarea
              value={pasteContent}
              onChange={(event) => setPasteContent(event.target.value)}
              placeholder="Paste rows from Excel, CSV, TSV, or a Markdown table…"
              rows={6}
            />
          </label>

          <div className="demand-actions">
            <button type="button" onClick={importPaste} disabled={!pasteContent || status.status === 'saving'}>
              {status.status === 'saving' ? 'Importing…' : 'Import pasted table'}
            </button>
            <label className="secondary-button file-button">
              Import Excel
              <input type="file" accept=".xlsx,.xlsm" onChange={importFile} />
            </label>
          </div>
        </details>
      )}

      {status.message && (
        <p className={`save-message ${status.status}`}>{status.message}</p>
      )}

      {!plan && (
        <p className="message info-message">No {isInsideDemand ? 'inside' : 'outside'} demand exists for the selected week. Import a table to create it.</p>
      )}

      {plan && (
        <form onSubmit={canEdit ? savePlan : (event) => event.preventDefault()}>
          <div className="demand-labour-settings">
            <div>
              <span className="eyebrow">Planned labour</span>
              <h3 id="demand-summary">Demand, sales targets and estimated labour</h3>
              <p>
                Changing productivity updates the preview right away; save to use it for roster generation.
                Higher productivity needs fewer staff.
              </p>
              <details className="inline-help">
                <summary>More about labour estimates</summary>
                <p>
                  Recalculation replaces manual staff counts, so save productivity changes before applying staffing overrides.
                  Planned labour {isInsideDemand
                    ? 'uses required staff-hours and the current average pay rate of eligible inside employees.'
                    : 'weights each driver’s pay rate by their automatically calculated approximate hours for this week.'}
                  {' '}“Actual demand labour” means the cost of the currently entered {staffLabel} values. Saved-roster labour
                  is separate and is calculated from the employees actually assigned.
                </p>
              </details>
            </div>
            <div className="demand-productivity-grid">
              {[
                [productivityField, productivityLabel, isInsideDemand ? 20 : 2.7, 0.01, 1000],
              ].map(([field, label, defaultValue, min, max]) => <label key={field}>{label}
                <input type="number" min={min} max={max} step="0.01" required value={plan[field] ?? defaultValue}
                  onChange={(event) => updatePlanningSetting(field, event.target.value)} readOnly={!canEdit} aria-readonly={!canEdit} />
              </label>)}
            </div>
          </div>

          {!isInsideDemand && demandSummary.usesApproximateHours && !demandSummary.approximateHoursCurrent && (
            <p className="message info-message" role="status">
              Demand has unsaved changes. The displayed pay mix still uses the last saved automatic-hours calculation; save demand to refresh it.
            </p>
          )}
          {!isInsideDemand && demandSummary.labourEstimateMessage && !demandSummary.isComplete && (
            <p className="message info-message" role="status">{demandSummary.labourEstimateMessage}</p>
          )}

          <div className="demand-overview-groups">
            <section className="demand-overview-group demand-overview-volume">
              <div className="demand-overview-group-heading">
                <span>1</span>
                <div><h4>{isInsideDemand ? 'Work to produce' : 'Work to deliver'}</h4><p>The expected workload and sales for the week.</p></div>
              </div>
              <dl>
                <div><dt>{workloadLabel}</dt><dd>{formatMetric(demandSummary.workload, 0)}</dd></div>
                <div><dt>Target sales</dt><dd>{formatMoney(demandSummary.targetSales)}</dd></div>
                <div><dt>{workloadLabel} per entered hour</dt><dd>{formatMetric(demandSummary.workloadPerEnteredStaffHour)}</dd></div>
              </dl>
            </section>
            <section className="demand-overview-group demand-overview-staffing">
              <div className="demand-overview-group-heading">
                <span>2</span>
                <div><h4>{staffLabel} required</h4><p>Compare the saved demand with the productivity calculation.</p></div>
              </div>
              <dl>
                <div><dt>Entered demand</dt><dd>{formatMetric(demandSummary.staffHours, 0)} hours</dd></div>
                <div><dt>Ideal workload</dt><dd>{formatMetric(demandSummary.idealStaffHours)} hours</dd></div>
                <div><dt>Whole-person need</dt><dd>{formatMetric(demandSummary.wholeStaffHours, 0)} hours</dd></div>
                <div><dt>Entered minus ideal</dt><dd>{formatSignedMetric(demandSummary.staffHourDifference)} hours</dd></div>
                <div><dt>Capacity used</dt><dd>{formatPercentage(demandSummary.capacityUtilization)}</dd></div>
              </dl>
            </section>
            <section className="demand-overview-group demand-overview-cost">
              <div className="demand-overview-group-heading">
                <span>3</span>
                <div><h4>Expected labour cost</h4><p>What the entered demand costs versus the fractional ideal.</p></div>
              </div>
              <dl>
                <div><dt>Entered-demand labour</dt><dd>{formatMoney(demandSummary.labourCost)}</dd></div>
                <div><dt>Ideal labour</dt><dd>{formatMoney(demandSummary.idealLabourCost)}</dd></div>
                <div><dt>Cost difference</dt><dd className={Number(demandSummary.labourCostDifference) > 0 ? 'metric-warning' : ''}>{formatSignedMoney(demandSummary.labourCostDifference)}</dd></div>
                <div><dt>Labour / sales</dt><dd>{formatPercentage(demandSummary.labourPercentage)}</dd></div>
                <div><dt>Cost per {isInsideDemand ? 'pizza' : 'delivery'}</dt><dd>{formatMoney(demandSummary.labourCostPerUnit)}</dd></div>
              </dl>
            </section>
          </div>

          <div className="demand-assumptions" aria-label="Labour calculation assumptions">
            <span><strong>{formatMetric(demandSummary.productivity)}</strong> {productivityLabel.toLowerCase()}</span>
            <span><strong>{formatMoney(demandSummary.averageHourlyRate)}</strong> {demandSummary.usesApproximateHours ? 'approximate-hours weighted pay' : 'average hourly pay'}</span>
            <span><strong>{demandSummary.eligibleEmployeeCount}</strong> eligible {isInsideDemand ? 'employees' : 'drivers'}</span>
            {demandSummary.usesApproximateHours && <span><strong>{formatMetric(demandSummary.approximateHours)}</strong> automatically allocated hours</span>}
          </div>

          {!isInsideDemand && demandSummary.approximateEmployees.length > 0 && (
            <details className="demand-metric-guide">
              <summary>Approximate-hours pay mix</summary>
              <div>
                <p>The estimate weights each current hourly rate by the automatic hours calculated from saved demand, useful availability and scarcity. These are planning estimates, not assigned shifts.</p>
                {demandSummary.unallocatedDemandHours > 0.001 && <p><strong>{formatMetric(demandSummary.unallocatedDemandHours)} demand-hours could not be allocated</strong> from current useful availability. Labour still prices all entered demand using the available weighted pay mix.</p>}
                <div className="demand-labour-table-wrapper">
                  <table className="demand-labour-table">
                    <thead><tr><th>Driver</th><th>Approximate hours</th><th>Useful capacity</th><th>Pay rate</th><th>Approximate base cost</th></tr></thead>
                    <tbody>{demandSummary.approximateEmployees.map((driver) => <tr key={driver.employeeId}>
                      <th>{driver.employeeName}</th>
                      <td>{formatMetric(driver.approximateHours)}h</td>
                      <td>{formatMetric(driver.capacityHours)}h</td>
                      <td>{formatMoney(driver.hourlyRate)}</td>
                      <td>{formatMoney(driver.approximateBaseCost)}</td>
                    </tr>)}</tbody>
                  </table>
                </div>
              </div>
            </details>
          )}

          <details className="demand-metric-guide">
            <summary>How these numbers are calculated</summary>
            <div>
              <p><strong>Ideal {isInsideDemand ? 'inside employees' : 'drivers'}</strong> = {workloadLabel.toLowerCase()} ÷ configured {productivityLabel.toLowerCase()}. This can be a decimal.</p>
              <p><strong>Whole-person need</strong> rounds ideal staffing up because a fraction of a person cannot be rostered.</p>
              <p><strong>Entered demand</strong> is the {staffLabel} value saved in the demand table. It may include minimum shop cover or manual changes.</p>
              <p><strong>Entered-demand labour</strong> uses entered demand and {demandSummary.usesApproximateHours ? 'the approximate-hours weighted driver pay rate' : 'average employee pay'}. It is not the named-employee cost from a saved roster.</p>
              <p>Sunday premium is applied to calendar-Sunday hours. A dash means required data is unavailable.</p>
            </div>
          </details>

          <div className="demand-labour-table-wrapper" id="demand-daily">
            <table className="demand-labour-table demand-daily-table">
              <thead>
                <tr>
                  <th>Day</th>
                  <th>Target sales</th>
                  <th>{workloadLabel}</th>
                  <th>{staffLabel} hours</th>
                  <th>{workloadLabel} capacity</th>
                  <th>Demand labour</th>
                  <th>Hourly checks</th>
                </tr>
              </thead>
              <tbody>
                {dailyStaffingRows.map((day) => (
                  <tr key={day.position}>
                    <th>{day.label}</th>
                    <td>
                      <input
                        type="number"
                        min="0"
                        step="0.01"
                        value={day.targetSales}
                        onChange={(event) => updateTargetSales(day.position, event.target.value)}
                        readOnly={!canEdit}
                        aria-readonly={!canEdit}
                        aria-label={`${day.label} target sales`}
                      />
                    </td>
                    <td className="demand-stacked-cell">
                      <strong>{formatMetric(day.workload, 0)}</strong>
                      <small>{day.openHours} open hours</small>
                    </td>
                    <td className="demand-stacked-cell">
                      <strong>{day.requiredStaffHours} entered</strong>
                      <small>{formatMetric(day.idealStaffHours)} ideal · {formatMetric(day.wholeStaffHours, 0)} whole need</small>
                    </td>
                    <td className="demand-stacked-cell">
                      <strong className={Number(day.capacityUtilization) > 100 ? 'metric-danger' : undefined}>
                        {formatPercentage(day.capacityUtilization)} used
                      </strong>
                      <small>{formatMetric(day.workloadCapacity)} capacity · {formatSignedMetric(day.unusedWorkloadCapacity)} spare</small>
                      <small>{formatMetric(day.workloadPerEnteredStaffHour)} {workloadLabel.toLowerCase()} per {isInsideDemand ? 'employee' : 'driver'}-hour</small>
                    </td>
                    <td className="demand-stacked-cell">
                      <strong>{formatMoney(day.labourCost)} entered</strong>
                      <small>{formatMoney(day.idealLabourCost)} ideal · <span className={Number(day.labourCostDifference) > 0 ? 'metric-warning' : undefined}>{formatSignedMoney(day.labourCostDifference)}</span> difference</small>
                      <small>{formatPercentage(day.labourPercentage)} of sales · {formatMoney(day.labourCostPerUnit)} per {isInsideDemand ? 'pizza' : 'delivery'}</small>
                      {day.sundayPremiumHours > 0 && <small>{day.sundayPremiumHours} Sunday-premium hours</small>}
                    </td>
                    <td className="demand-stacked-cell">
                      <span className={day.understaffedHours > 0 ? 'metric-danger' : 'metric-success'}>
                        {day.understaffedHours} under
                      </span>
                      <small>{day.matchedHours} match whole need</small>
                      <small>{day.aboveMinimumHours} above whole need</small>
                      {day.unknownHours > 0 && <small>{day.unknownHours} missing data</small>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <section className="demand-hourly-analysis" aria-labelledby="demand-hourly-heading">
            <div className="demand-hourly-heading">
              <div>
                <span className="eyebrow">Hourly breakdown</span>
                <h3 id="demand-hourly-heading">{workloadLabel} productivity and labour by hour</h3>
                <p>Use this to find under-entered demand, low capacity use, and the hourly cost of whole-person rounding.</p>
              </div>
              <label>
                Day
                <select
                  value={selectedDemandDayPosition}
                  onChange={(event) => setSelectedDemandDayPosition(Number(event.target.value))}
                >
                  {plan.columns.map((column) => (
                    <option key={column.position} value={column.position}>{column.label}</option>
                  ))}
                </select>
              </label>
            </div>

            {selectedDaySummary && (
              <div className="demand-hourly-summary">
                <div><span>{workloadLabel}</span><strong>{formatMetric(selectedDaySummary.workload, 0)}</strong></div>
                <div><span>Entered / ideal hours</span><strong>{selectedDaySummary.requiredStaffHours} / {formatMetric(selectedDaySummary.idealStaffHours)}</strong></div>
                <div><span>Capacity used</span><strong>{formatPercentage(selectedDaySummary.capacityUtilization)}</strong></div>
                <div><span>Actual / ideal labour</span><strong>{formatMoney(selectedDaySummary.labourCost)} / {formatMoney(selectedDaySummary.idealLabourCost)}</strong></div>
                <div><span>Cost per {isInsideDemand ? 'pizza' : 'delivery'}</span><strong>{formatMoney(selectedDaySummary.labourCostPerUnit)}</strong></div>
              </div>
            )}

            <div className="demand-labour-table-wrapper demand-hourly-table-wrapper">
              <table className="demand-labour-table demand-hourly-table">
                <thead>
                  <tr>
                    <th>Hour</th>
                    <th>{workloadLabel}</th>
                    <th>Entered {isInsideDemand ? 'employees' : 'drivers'}</th>
                    <th>Ideal need</th>
                    <th>Staffing check</th>
                    <th>Capacity</th>
                    <th>Labour / hour</th>
                    <th>Cost / {isInsideDemand ? 'pizza' : 'delivery'}</th>
                  </tr>
                </thead>
                <tbody>
                  {selectedDayHourlyRows.map((row) => {
                    const statusDetails = staffingStatus(row)
                    return (
                      <tr key={`${selectedDemandDayPosition}-${row.hour}`}>
                        <th>{String(row.hour).padStart(2, '0')}:00</th>
                        <td className="demand-stacked-cell">
                          <strong>{formatMetric(row.workload)}</strong>
                            <small>{formatMetric(row.workloadPerEnteredEmployee)} per entered {isInsideDemand ? 'employee' : 'driver'}</small>
                        </td>
                        <td><strong>{formatMetric(row.enteredStaff, 0)}</strong></td>
                        <td className="demand-stacked-cell">
                          <strong>{formatMetric(row.idealStaff)} {isInsideDemand ? 'employees' : 'drivers'}</strong>
                          <small>{formatMetric(row.wholeStaff, 0)} after rounding up</small>
                        </td>
                        <td><span className={`staffing-status staffing-status-${statusDetails.tone}`}>{statusDetails.label}</span></td>
                        <td className="demand-stacked-cell">
                          <strong className={Number(row.capacityUtilization) > 100 ? 'metric-danger' : undefined}>
                            {formatPercentage(row.capacityUtilization)} used
                          </strong>
                            <small>{formatMetric(row.workloadCapacity)} {workloadLabel.toLowerCase()} capacity</small>
                          <small className={Number(row.unusedWorkloadCapacity) < 0 ? 'metric-danger' : undefined}>
                            {formatSignedMetric(row.unusedWorkloadCapacity)} spare
                          </small>
                        </td>
                        <td className="demand-stacked-cell">
                          <strong>{formatMoney(row.demandLabourCost)} entered</strong>
                          <small>{formatMoney(row.idealLabourCost)} ideal</small>
                          <small className={Number(row.labourCostDifference) > 0 ? 'metric-warning' : undefined}>
                            {formatSignedMoney(row.labourCostDifference)} difference
                          </small>
                          <small>{formatMoney(demandSummary.averageHourlyRate)} {demandSummary.usesApproximateHours ? 'weighted ' : ''}pay{row.isSundayPremium ? ' × 1.25 Sunday' : ''}</small>
                        </td>
                        <td>{formatMoney(row.labourCostPerUnit)}</td>
                      </tr>
                    )
                  })}
                  {selectedDayHourlyRows.length === 0 && (
                    <tr><td colSpan="8" className="demand-empty-hourly">No open demand hours are available for this day.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>

          <div className="demand-edit-heading" id="demand-edit">
            <span className="eyebrow">Edit demand</span>
            <h3>{workloadLabel} and {isInsideDemand ? 'inside employees' : 'drivers'} by hour</h3>
            <p>{canEdit ? 'Adjust any hour, then save. Closed hours are greyed out.' : 'Read-only view of the saved demand.'}</p>
          </div>

          <div className="demand-mobile-controls">
            <label>
              Day
              <select
                value={selectedDemandDayPosition}
                onChange={(event) => setSelectedDemandDayPosition(Number(event.target.value))}
              >
                {plan.columns.map((column) => (
                  <option key={column.position} value={column.position}>
                    {column.label}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="demand-table-wrapper demand-desktop-view">
            <table className="demand-table demand-desktop-table">
              <thead>
                <tr>
                  <th rowSpan="2">Hour</th>
                  {plan.columns.map((column) => (
                    <th key={column.position} colSpan="2">
                      <span className="demand-day-label">{column.label}</span>
                      <small className="demand-day-hours">
                        {dailyStaffingRows.find((day) => day.position === column.position)?.requiredStaffHours ?? 0} {isInsideDemand ? 'employee' : 'driver'}-hours
                      </small>
                    </th>
                  ))}
                </tr>
                <tr>
                  {plan.columns.flatMap((column) => demandFields.map(([field, label]) => <th key={`${column.position}-${field}`}>{label}</th>))}
                </tr>
              </thead>
              <tbody>
                {plan.rows.map((row) => (
                  <tr key={row.hour}>
                    <th>{String(row.hour).padStart(2, '0')}:00</th>
                    {plan.columns.flatMap((column) => demandFields.map(([field, label]) => demandCell(row, column, field, label)))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="demand-mobile-view">
            <div className="demand-table-wrapper">
              <table className="demand-table demand-mobile-table">
                <thead>
                  <tr>
                    <th colSpan="3">
                      <span className="demand-day-label">{selectedDemandColumn?.label ?? 'Day'}</span>
                      <small className="demand-day-hours">
                        {selectedDaySummary?.requiredStaffHours ?? 0} {isInsideDemand ? 'employee' : 'driver'}-hours
                      </small>
                    </th>
                  </tr>
                  <tr>
                    <th>Hour</th>
                    {demandFields.map(([field, label]) => <th key={field}>{label}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {plan.rows.map((row) => <tr key={row.hour}>
                    <th>{String(row.hour).padStart(2, '0')}:00</th>
                    {selectedDemandColumn && demandFields.map(([field, label]) => demandCell(row, selectedDemandColumn, field, label))}
                  </tr>)}
                </tbody>
              </table>
            </div>
          </div>

          {canEdit && (
            <div className="demand-actions demand-save-bar">
              <button type="submit" disabled={status.status === 'saving'}>Save demand changes</button>
              <button type="button" className="secondary-button" disabled={status.status === 'saving'} onClick={(event) => {
                if (event.currentTarget.form.reportValidity()) void savePlan(null, true)
              }}>Recalculate and save demand</button>
              <button type="button" className="secondary-button" onClick={deletePlan}>Delete plan</button>
            </div>
          )}
        </form>
      )}
    </section>
  )
}
