const number = (value) => Number(value ?? 0)
const money = (value) => Math.round((value + Number.EPSILON) * 100) / 100
const staffRequired = (count, rate) => {
  if (count == null || count === '' || !Number.isFinite(Number(rate)) || Number(rate) <= 0) return null
  const ratio = Number(count) / Number(rate)
  return Math.max(0, Math.ceil(ratio - Number.EPSILON * Math.max(1, ratio) * 4))
}

export function recalculateDemandValue(value, plan, fields = ['demand']) {
  const updated = { ...value }
  if (fields.includes('demand')) updated.demand = value.isOpen === false ? null : staffRequired(value.deliveries, plan.deliveriesPerDriverHour ?? 2.7)
  return updated
}

export function recalculateDemandPlan(plan, fields) {
  return { ...plan, rows: plan.rows.map((row) => ({
    ...row, values: row.values.map((value) => recalculateDemandValue(value, plan, fields)),
  })) }
}

export function calculateDemandSummary(plan) {
  const days = (plan?.columns || []).map((column) => {
    const values = plan.rows.map((row) => row.values.find((value) => value.position === column.position)).filter((value) => value && value.isOpen !== false)
    const requiredDriverHours = values.reduce((sum, value) => sum + number(value.demand), 0)
    const targetSales = number(column.targetSales)
    return { ...column, targetSales, requiredDriverHours,
      deliveries: values.reduce((sum, value) => sum + number(value.deliveries), 0),
    }
  })
  const total = (field) => days.reduce((sum, day) => sum + number(day[field]), 0)
  const targetSales = total('targetSales')
  return { days, targetSales,
    driverHours: total('requiredDriverHours'), deliveries: total('deliveries'),
  }
}

export function calculateDemandLabour(plan, employees = []) {
  const summary = calculateDemandSummary(plan)
  const eligibleDrivers = employees.filter((employee) => {
    const roles = (employee.roles || []).map((role) => String(role).toLowerCase())
    return employee.isActive && employee.targetHours != null && roles.includes('driver') &&
      !roles.includes('instore') && !roles.includes('manager') &&
      Number.isFinite(Number(employee.hourlyRate)) && Number(employee.hourlyRate) >= 0
  })
  const driversWithTargets = eligibleDrivers.filter((employee) => Number(employee.targetHours) > 0)
  const weightedDrivers = driversWithTargets.length > 0 ? driversWithTargets : eligibleDrivers
  const totalWeight = weightedDrivers.reduce((sum, employee) =>
    sum + (driversWithTargets.length > 0 ? Number(employee.targetHours) : 1), 0)
  const averageHourlyRate = totalWeight > 0
    ? weightedDrivers.reduce((sum, employee) => sum + Number(employee.hourlyRate) *
      (driversWithTargets.length > 0 ? Number(employee.targetHours) : 1), 0) / totalWeight
    : null

  const days = summary.days.map((day) => {
    const entries = (plan?.rows || []).map((row) => ({
      hour: Number(row.hour),
      value: row.values.find((value) => value.position === day.position),
    })).filter((entry) => entry.value && entry.value.isOpen !== false)
    const isComplete = averageHourlyRate != null && entries.every((entry) =>
      entry.value.demand != null && Number.isFinite(Number(entry.value.demand)) && Number(entry.value.demand) >= 0)
    const sundayPremiumHours = entries.reduce((sum, entry) => {
      const calendarDayPosition = (day.position + (entry.hour < 6 ? 1 : 0)) % 7
      return sum + (calendarDayPosition === 6 ? number(entry.value.demand) : 0)
    }, 0)
    const regularHours = day.requiredDriverHours - sundayPremiumHours
    const labourCost = isComplete
      ? money((regularHours + sundayPremiumHours * 1.25) * averageHourlyRate)
      : null
    return {
      ...day,
      sundayPremiumHours,
      labourCost,
      labourPercentage: labourCost != null && day.targetSales > 0
        ? money(labourCost / day.targetSales * 100) : null,
      isComplete,
    }
  })
  const isComplete = days.length > 0 && days.every((day) => day.isComplete)
  const labourCost = isComplete ? money(days.reduce((sum, day) => sum + day.labourCost, 0)) : null

  return {
    ...summary,
    days,
    eligibleDriverCount: eligibleDrivers.length,
    averageHourlyRate: averageHourlyRate == null ? null : money(averageHourlyRate),
    labourCost,
    labourPercentage: labourCost != null && summary.targetSales > 0
      ? money(labourCost / summary.targetSales * 100) : null,
    isComplete,
  }
}
