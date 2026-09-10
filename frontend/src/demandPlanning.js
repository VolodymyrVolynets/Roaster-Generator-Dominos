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
  if (fields.includes('demand')) updated.demand = value.isOpen === false ? null : staffRequired(value.deliveries, plan.deliveriesPerDriverHour ?? 2.7)
  if (fields.includes('insideDemand')) updated.insideDemand = value.isOpen === false ? null : staffRequired(value.deliveries, plan.deliveriesPerDriverHour ?? 2.7)
  return updated
}

export function recalculateDemandPlan(plan, fields) {
  return { ...plan, rows: plan.rows.map((row) => ({
    ...row, values: row.values.map((value) => recalculateDemandValue(value, plan, fields)),
  })) }
}

export function calculateDemandSummary(plan, demandField = 'demand') {
  const days = (plan?.columns || []).map((column) => {
    const values = plan.rows.map((row) => row.values.find((value) => value.position === column.position)).filter((value) => value && value.isOpen !== false)
    const requiredDriverHours = values.reduce((sum, value) => sum + number(value[demandField]), 0)
    const targetSales = number(column.targetSales)
    return { ...column, targetSales, requiredDriverHours,
      deliveries: decimal(values.reduce((sum, value) => sum + number(value.deliveries), 0), 4),
    }
  })
  const total = (field) => decimal(days.reduce((sum, day) => sum + number(day[field]), 0), 4)
  const targetSales = total('targetSales')
  return { days, targetSales,
    driverHours: total('requiredDriverHours'), deliveries: total('deliveries'),
  }
}

export function calculateDemandLabour(plan, employees = [], area = 'outside') {
  const isInside = area === 'inside'
  const demandField = isInside ? 'insideDemand' : 'demand'
  const targetHoursField = isInside ? 'insideTargetHours' : 'targetHours'
  const summary = calculateDemandSummary(plan, demandField)
  const productivity = optionalNumber(plan?.deliveriesPerDriverHour)
  const hasProductivity = productivity != null && productivity > 0
  const eligibleEmployees = employees.filter((employee) => {
    const roles = (employee.roles || []).map((role) => String(role).toLowerCase())
    const hasAreaRole = isInside
      ? roles.includes('instore') && !roles.includes('manager')
      : roles.includes('driver') && !roles.includes('instore') && !roles.includes('manager')
    return employee.isActive && employee[targetHoursField] != null && hasAreaRole &&
      Number.isFinite(Number(employee.hourlyRate)) && Number(employee.hourlyRate) >= 0
  })
  const employeesWithTargets = eligibleEmployees.filter((employee) => Number(employee[targetHoursField]) > 0)
  const weightedEmployees = employeesWithTargets.length > 0 ? employeesWithTargets : eligibleEmployees
  const totalWeight = weightedEmployees.reduce((sum, employee) =>
    sum + (employeesWithTargets.length > 0 ? Number(employee[targetHoursField]) : 1), 0)
  const averageHourlyRate = totalWeight > 0
    ? weightedEmployees.reduce((sum, employee) => sum + Number(employee.hourlyRate) *
      (employeesWithTargets.length > 0 ? Number(employee[targetHoursField]) : 1), 0) / totalWeight
    : null
  const totalTargetHours = eligibleEmployees.reduce((sum, employee) =>
    sum + Math.max(0, number(employee[targetHoursField])), 0)

  const days = summary.days.map((day) => {
    const entries = (plan?.rows || []).map((row) => ({
      hour: Number(row.hour),
      value: row.values.find((value) => value.position === day.position),
    })).filter((entry) => entry.value && entry.value.isOpen !== false)
    const isComplete = averageHourlyRate != null && entries.every((entry) =>
      entry.value[demandField] != null && Number.isFinite(Number(entry.value[demandField])) && Number(entry.value[demandField]) >= 0)
    const hourly = entries.map((entry) => {
      const deliveries = optionalNumber(entry.value.deliveries)
      const enteredDrivers = optionalNumber(entry.value[demandField])
      const calendarDayPosition = (day.position + (entry.hour < 6 ? 1 : 0)) % 7
      const isSundayPremium = calendarDayPosition === 6
      const payMultiplier = isSundayPremium ? 1.25 : 1
      const idealDriversRaw = deliveries != null && hasProductivity ? deliveries / productivity : null
      const wholeDrivers = idealDriversRaw == null
        ? null
        : Math.max(0, Math.ceil(idealDriversRaw - Number.EPSILON * Math.max(1, idealDriversRaw) * 4))
      const deliveryCapacityRaw = enteredDrivers != null && hasProductivity
        ? enteredDrivers * productivity : null
      const demandLabourCostRaw = enteredDrivers != null && averageHourlyRate != null
        ? enteredDrivers * averageHourlyRate * payMultiplier : null
      const idealLabourCostRaw = idealDriversRaw != null && averageHourlyRate != null
        ? idealDriversRaw * averageHourlyRate * payMultiplier : null
      let staffingStatus = 'unknown'
      if (wholeDrivers != null && enteredDrivers != null) {
        staffingStatus = enteredDrivers < wholeDrivers
          ? 'under' : enteredDrivers > wholeDrivers ? 'above' : 'matched'
      }

      return {
        hour: entry.hour,
        isSundayPremium,
        deliveries,
        enteredDrivers,
        idealDrivers: decimal(idealDriversRaw),
        wholeDrivers,
        driverDifference: wholeDrivers == null || enteredDrivers == null
          ? null : decimal(enteredDrivers - wholeDrivers),
        deliveryCapacity: decimal(deliveryCapacityRaw),
        unusedDeliveryCapacity: deliveryCapacityRaw == null || deliveries == null
          ? null : decimal(deliveryCapacityRaw - deliveries),
        capacityUtilization: deliveryCapacityRaw > 0 && deliveries != null
          ? decimal(deliveries / deliveryCapacityRaw * 100) : null,
        deliveriesPerEnteredDriver: enteredDrivers > 0 && deliveries != null
          ? decimal(deliveries / enteredDrivers) : null,
        idealLabourCost: idealLabourCostRaw == null ? null : money(idealLabourCostRaw),
        demandLabourCost: demandLabourCostRaw == null ? null : money(demandLabourCostRaw),
        labourCostDifference: demandLabourCostRaw == null || idealLabourCostRaw == null
          ? null : money(demandLabourCostRaw - idealLabourCostRaw),
        labourCostPerDelivery: demandLabourCostRaw == null || deliveries == null || deliveries <= 0
          ? null : money(demandLabourCostRaw / deliveries),
        staffingStatus,
        idealDriversRaw,
        deliveryCapacityRaw,
        demandLabourCostRaw,
        idealLabourCostRaw,
      }
    })
    const sundayPremiumHours = hourly.reduce((sum, entry) =>
      sum + (entry.isSundayPremium ? number(entry.enteredDrivers) : 0), 0)
    const regularHours = day.requiredDriverHours - sundayPremiumHours
    const labourCost = isComplete
      ? money((regularHours + sundayPremiumHours * 1.25) * averageHourlyRate)
      : null
    const idealComplete = averageHourlyRate != null && hasProductivity &&
      hourly.every((entry) => entry.idealDriversRaw != null)
    const idealDriverHoursRaw = idealComplete
      ? hourly.reduce((sum, entry) => sum + entry.idealDriversRaw, 0) : null
    const idealLabourCost = idealComplete
      ? money(hourly.reduce((sum, entry) => sum + entry.idealLabourCostRaw, 0)) : null
    const deliveryCapacity = hasProductivity && isComplete
      ? day.requiredDriverHours * productivity : null
    const staffingCount = (status) => hourly.filter((entry) => entry.staffingStatus === status).length
    return {
      ...day,
      sundayPremiumHours,
      labourCost,
      labourPercentage: labourCost != null && day.targetSales > 0
        ? money(labourCost / day.targetSales * 100) : null,
      openHours: hourly.length,
      idealDriverHours: decimal(idealDriverHoursRaw),
      wholeDriverHours: hourly.every((entry) => entry.wholeDrivers != null)
        ? hourly.reduce((sum, entry) => sum + entry.wholeDrivers, 0) : null,
      driverHourDifference: idealDriverHoursRaw == null
        ? null : decimal(day.requiredDriverHours - idealDriverHoursRaw),
      deliveryCapacity: decimal(deliveryCapacity),
      unusedDeliveryCapacity: deliveryCapacity == null
        ? null : decimal(deliveryCapacity - day.deliveries),
      capacityUtilization: deliveryCapacity > 0
        ? decimal(day.deliveries / deliveryCapacity * 100) : null,
      deliveriesPerEnteredDriverHour: day.requiredDriverHours > 0
        ? decimal(day.deliveries / day.requiredDriverHours) : null,
      idealLabourCost,
      labourCostDifference: labourCost == null || idealLabourCost == null
        ? null : money(labourCost - idealLabourCost),
      labourCostPerDelivery: labourCost == null || day.deliveries <= 0
        ? null : money(labourCost / day.deliveries),
      understaffedHours: staffingCount('under'),
      matchedHours: staffingCount('matched'),
      aboveMinimumHours: staffingCount('above'),
      unknownHours: staffingCount('unknown'),
      hourly: hourly.map(({ idealDriversRaw, deliveryCapacityRaw, demandLabourCostRaw, idealLabourCostRaw, ...entry }) => entry),
      isComplete,
      idealComplete,
    }
  })
  const isComplete = days.length > 0 && days.every((day) => day.isComplete)
  const idealComplete = days.length > 0 && days.every((day) => day.idealComplete)
  const labourCost = isComplete ? money(days.reduce((sum, day) => sum + day.labourCost, 0)) : null
  const idealLabourCost = idealComplete
    ? money(days.reduce((sum, day) => sum + day.idealLabourCost, 0)) : null
  const idealDriverHours = idealComplete
    ? decimal(days.reduce((sum, day) => sum + day.idealDriverHours, 0)) : null
  const deliveryCapacity = isComplete && hasProductivity
    ? decimal(summary.driverHours * productivity) : null

  return {
    ...summary,
    days,
    area,
    eligibleEmployeeCount: eligibleEmployees.length,
    eligibleDriverCount: eligibleEmployees.length,
    totalTargetHours: decimal(totalTargetHours),
    demandToTargetHoursPercentage: totalTargetHours > 0
      ? decimal(summary.driverHours / totalTargetHours * 100) : null,
    averageHourlyRate: averageHourlyRate == null ? null : money(averageHourlyRate),
    productivity: hasProductivity ? productivity : null,
    idealDriverHours,
    wholeDriverHours: days.every((day) => day.wholeDriverHours != null)
      ? days.reduce((sum, day) => sum + day.wholeDriverHours, 0) : null,
    driverHourDifference: idealDriverHours == null
      ? null : decimal(summary.driverHours - idealDriverHours),
    deliveryCapacity,
    unusedDeliveryCapacity: deliveryCapacity == null
      ? null : decimal(deliveryCapacity - summary.deliveries),
    capacityUtilization: deliveryCapacity > 0
      ? decimal(summary.deliveries / deliveryCapacity * 100) : null,
    deliveriesPerEnteredDriverHour: summary.driverHours > 0
      ? decimal(summary.deliveries / summary.driverHours) : null,
    labourCost,
    idealLabourCost,
    labourCostDifference: labourCost == null || idealLabourCost == null
      ? null : money(labourCost - idealLabourCost),
    labourCostPerDelivery: labourCost == null || summary.deliveries <= 0
      ? null : money(labourCost / summary.deliveries),
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
