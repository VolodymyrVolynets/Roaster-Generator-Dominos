const number = (value) => Number(value ?? 0)
const money = (value) => Math.round((value + Number.EPSILON) * 100) / 100
const decimal = (value, digits = 2) => {
  if (value == null || !Number.isFinite(Number(value))) return null
  const factor = 10 ** digits
  return Math.round((Number(value) + Number.EPSILON) * factor) / factor
}
const optionalNumber = (value) => {
  if (value == null || value === '') return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}
const staffRequired = (count, rate) => {
  if (count == null || count === '' || !Number.isFinite(Number(rate)) || Number(rate) <= 0) return null
  const ratio = Number(count) / Number(rate)
  return Math.max(0, Math.ceil(ratio - Number.EPSILON * Math.max(1, ratio) * 4))
}

export function recalculateDemandValue(value, plan, fields = ['demand']) {
  const updated = { ...value }
  if (plan?.demandKind === 'inside') {
    if (fields.includes('insideDemand')) {
      updated.insideDemand = value.isOpen === false ? null : staffRequired(value.pizzas, plan.pizzasPerInsideHour ?? 20)
    }
  } else if (fields.includes('demand')) {
    updated.demand = value.isOpen === false ? null : staffRequired(value.deliveries, plan.deliveriesPerDriverHour ?? 2.7)
  }
  return updated
}

export function recalculateDemandPlan(plan, fields) {
  return { ...plan, rows: plan.rows.map((row) => ({
    ...row, values: row.values.map((value) => recalculateDemandValue(value, plan, fields)),
  })) }
}

export function calculateDemandSummary(plan) {
  const inside = plan?.demandKind === 'inside'
  const demandField = inside ? 'insideDemand' : 'demand'
  const workloadField = inside ? 'pizzas' : 'deliveries'
  const days = (plan?.columns || []).map((column) => {
    const values = plan.rows.map((row) => row.values.find((value) => value.position === column.position)).filter((value) => value && value.isOpen !== false)
    const requiredStaffHours = values.reduce((sum, value) => sum + number(value[demandField]), 0)
    const targetSales = number(column.targetSales)
    return { ...column, targetSales, requiredStaffHours,
      workload: decimal(values.reduce((sum, value) => sum + number(value[workloadField]), 0), 4),
    }
  })
  const total = (field) => decimal(days.reduce((sum, day) => sum + number(day[field]), 0), 4)
  const targetSales = total('targetSales')
  return { days, targetSales, staffHours: total('requiredStaffHours'), workload: total('workload') }
}

export function calculateDemandLabour(plan, employees = []) {
  const summary = calculateDemandSummary(plan)
  const inside = plan?.demandKind === 'inside'
  const productivity = optionalNumber(inside ? plan?.pizzasPerInsideHour : plan?.deliveriesPerDriverHour)
  const hasProductivity = productivity != null && productivity > 0
  const eligibleEmployees = employees.filter((employee) => {
    const roles = (employee.roles || []).map((role) => String(role).toLowerCase())
    const eligibleRole = inside
      ? (roles.includes('instore') || roles.includes('manager'))
      : roles.includes('driver') && !roles.includes('instore') && !roles.includes('manager')
    return employee.isActive && eligibleRole &&
      Number.isFinite(Number(employee.hourlyRate)) && Number(employee.hourlyRate) >= 0
  })
  const automaticEstimate = inside ? null : plan?.labourEstimate
  const usesApproximateHours = automaticEstimate != null
  const averageHourlyRate = usesApproximateHours
    ? automaticEstimate.isAvailable && Number.isFinite(Number(automaticEstimate.weightedAverageHourlyRate))
      ? Number(automaticEstimate.weightedAverageHourlyRate) : null
    : eligibleEmployees.length > 0
      ? eligibleEmployees.reduce((sum, employee) => sum + Number(employee.hourlyRate), 0) / eligibleEmployees.length
      : null
  const workloadField = inside ? 'pizzas' : 'deliveries'
  const demandField = inside ? 'insideDemand' : 'demand'

  const days = summary.days.map((day) => {
    const entries = (plan?.rows || []).map((row) => ({
      hour: Number(row.hour),
      value: row.values.find((value) => value.position === day.position),
    })).filter((entry) => entry.value && entry.value.isOpen !== false)
    const isComplete = averageHourlyRate != null && entries.every((entry) =>
      entry.value[demandField] != null && Number.isFinite(Number(entry.value[demandField])) && Number(entry.value[demandField]) >= 0)
    const hourly = entries.map((entry) => {
      const workload = optionalNumber(entry.value[workloadField])
      const enteredStaff = optionalNumber(entry.value[demandField])
      const calendarDayPosition = (day.position + (entry.hour < 6 ? 1 : 0)) % 7
      const isSundayPremium = calendarDayPosition === 6
      const payMultiplier = isSundayPremium ? 1.25 : 1
      const idealStaffRaw = workload != null && hasProductivity ? workload / productivity : null
      const wholeStaff = idealStaffRaw == null
        ? null
        : Math.max(0, Math.ceil(idealStaffRaw - Number.EPSILON * Math.max(1, idealStaffRaw) * 4))
      const workloadCapacityRaw = enteredStaff != null && hasProductivity
        ? enteredStaff * productivity : null
      const demandLabourCostRaw = enteredStaff != null && averageHourlyRate != null
        ? enteredStaff * averageHourlyRate * payMultiplier : null
      const idealLabourCostRaw = idealStaffRaw != null && averageHourlyRate != null
        ? idealStaffRaw * averageHourlyRate * payMultiplier : null
      let staffingStatus = 'unknown'
      if (wholeStaff != null && enteredStaff != null) {
        staffingStatus = enteredStaff < wholeStaff
          ? 'under' : enteredStaff > wholeStaff ? 'above' : 'matched'
      }

      return {
        hour: entry.hour,
        isSundayPremium,
        workload,
        enteredStaff,
        idealStaff: decimal(idealStaffRaw),
        wholeStaff,
        staffDifference: wholeStaff == null || enteredStaff == null
          ? null : decimal(enteredStaff - wholeStaff),
        workloadCapacity: decimal(workloadCapacityRaw),
        unusedWorkloadCapacity: workloadCapacityRaw == null || workload == null
          ? null : decimal(workloadCapacityRaw - workload),
        capacityUtilization: workloadCapacityRaw > 0 && workload != null
          ? decimal(workload / workloadCapacityRaw * 100) : null,
        workloadPerEnteredEmployee: enteredStaff > 0 && workload != null
          ? decimal(workload / enteredStaff) : null,
        idealLabourCost: idealLabourCostRaw == null ? null : money(idealLabourCostRaw),
        demandLabourCost: demandLabourCostRaw == null ? null : money(demandLabourCostRaw),
        labourCostDifference: demandLabourCostRaw == null || idealLabourCostRaw == null
          ? null : money(demandLabourCostRaw - idealLabourCostRaw),
        labourCostPerUnit: demandLabourCostRaw == null || workload == null || workload <= 0
          ? null : money(demandLabourCostRaw / workload),
        staffingStatus,
        idealStaffRaw,
        workloadCapacityRaw,
        demandLabourCostRaw,
        idealLabourCostRaw,
      }
    })
    const sundayPremiumHours = hourly.reduce((sum, entry) =>
      sum + (entry.isSundayPremium ? number(entry.enteredStaff) : 0), 0)
    const regularHours = day.requiredStaffHours - sundayPremiumHours
    const labourCost = isComplete
      ? money((regularHours + sundayPremiumHours * 1.25) * averageHourlyRate)
      : null
    const idealComplete = averageHourlyRate != null && hasProductivity &&
      hourly.every((entry) => entry.idealStaffRaw != null)
    const idealStaffHoursRaw = idealComplete
      ? hourly.reduce((sum, entry) => sum + entry.idealStaffRaw, 0) : null
    const idealLabourCost = idealComplete
      ? money(hourly.reduce((sum, entry) => sum + entry.idealLabourCostRaw, 0)) : null
    const workloadCapacity = hasProductivity && isComplete
      ? day.requiredStaffHours * productivity : null
    const staffingCount = (status) => hourly.filter((entry) => entry.staffingStatus === status).length
    return {
      ...day,
      sundayPremiumHours,
      labourCost,
      labourPercentage: labourCost != null && day.targetSales > 0
        ? money(labourCost / day.targetSales * 100) : null,
      openHours: hourly.length,
      idealStaffHours: decimal(idealStaffHoursRaw),
      wholeStaffHours: hourly.every((entry) => entry.wholeStaff != null)
        ? hourly.reduce((sum, entry) => sum + entry.wholeStaff, 0) : null,
      staffHourDifference: idealStaffHoursRaw == null
        ? null : decimal(day.requiredStaffHours - idealStaffHoursRaw),
      workloadCapacity: decimal(workloadCapacity),
      unusedWorkloadCapacity: workloadCapacity == null
        ? null : decimal(workloadCapacity - day.workload),
      capacityUtilization: workloadCapacity > 0
        ? decimal(day.workload / workloadCapacity * 100) : null,
      workloadPerEnteredStaffHour: day.requiredStaffHours > 0
        ? decimal(day.workload / day.requiredStaffHours) : null,
      idealLabourCost,
      labourCostDifference: labourCost == null || idealLabourCost == null
        ? null : money(labourCost - idealLabourCost),
      labourCostPerUnit: labourCost == null || day.workload <= 0
        ? null : money(labourCost / day.workload),
      understaffedHours: staffingCount('under'),
      matchedHours: staffingCount('matched'),
      aboveMinimumHours: staffingCount('above'),
      unknownHours: staffingCount('unknown'),
      hourly: hourly.map(({ idealStaffRaw, workloadCapacityRaw, demandLabourCostRaw, idealLabourCostRaw, ...entry }) => entry),
      isComplete,
      idealComplete,
    }
  })
  const isComplete = days.length > 0 && days.every((day) => day.isComplete)
  const idealComplete = days.length > 0 && days.every((day) => day.idealComplete)
  const labourCost = isComplete ? money(days.reduce((sum, day) => sum + day.labourCost, 0)) : null
  const idealLabourCost = idealComplete
    ? money(days.reduce((sum, day) => sum + day.idealLabourCost, 0)) : null
  const idealStaffHours = idealComplete
    ? decimal(days.reduce((sum, day) => sum + day.idealStaffHours, 0)) : null
  const workloadCapacity = isComplete && hasProductivity
    ? decimal(summary.staffHours * productivity) : null

  return {
    ...summary,
    days,
    eligibleEmployeeCount: usesApproximateHours
      ? (automaticEstimate.drivers || []).length : eligibleEmployees.length,
    averageHourlyRate: averageHourlyRate == null ? null : money(averageHourlyRate),
    usesApproximateHours,
    approximateHoursCurrent: plan?.labourEstimateCurrent !== false,
    approximateHours: usesApproximateHours ? number(automaticEstimate.totalApproximateHours) : null,
    unallocatedDemandHours: usesApproximateHours ? number(automaticEstimate.unallocatedDemandHours) : null,
    labourEstimateMessage: usesApproximateHours ? automaticEstimate.message : null,
    approximateEmployees: usesApproximateHours ? (automaticEstimate.drivers || []) : [],
    productivity: hasProductivity ? productivity : null,
    idealStaffHours,
    wholeStaffHours: days.every((day) => day.wholeStaffHours != null)
      ? days.reduce((sum, day) => sum + day.wholeStaffHours, 0) : null,
    staffHourDifference: idealStaffHours == null
      ? null : decimal(summary.staffHours - idealStaffHours),
    workloadCapacity,
    unusedWorkloadCapacity: workloadCapacity == null
      ? null : decimal(workloadCapacity - summary.workload),
    capacityUtilization: workloadCapacity > 0
      ? decimal(summary.workload / workloadCapacity * 100) : null,
    workloadPerEnteredStaffHour: summary.staffHours > 0
      ? decimal(summary.workload / summary.staffHours) : null,
    labourCost,
    idealLabourCost,
    labourCostDifference: labourCost == null || idealLabourCost == null
      ? null : money(labourCost - idealLabourCost),
    labourCostPerUnit: labourCost == null || summary.workload <= 0
      ? null : money(labourCost / summary.workload),
    labourPercentage: labourCost != null && summary.targetSales > 0
      ? money(labourCost / summary.targetSales * 100) : null,
    idealLabourPercentage: idealLabourCost != null && summary.targetSales > 0
      ? money(idealLabourCost / summary.targetSales * 100) : null,
    understaffedHours: days.reduce((sum, day) => sum + day.understaffedHours, 0),
    matchedHours: days.reduce((sum, day) => sum + day.matchedHours, 0),
    aboveMinimumHours: days.reduce((sum, day) => sum + day.aboveMinimumHours, 0),
    unknownHours: days.reduce((sum, day) => sum + day.unknownHours, 0),
    isComplete,
    idealComplete,
  }
}
