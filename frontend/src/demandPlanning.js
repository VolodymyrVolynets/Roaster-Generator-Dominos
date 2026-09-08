const money = (value) => Math.round((value + Number.EPSILON) * 100) / 100
const number = (value) => Number(value ?? 0)
const staffRequired = (count, rate) => {
  if (count == null || count === '' || !Number.isFinite(Number(rate)) || Number(rate) <= 0) return null
  const ratio = Number(count) / Number(rate)
  return Math.max(0, Math.ceil(ratio - Number.EPSILON * Math.max(1, ratio) * 4))
}

export function recalculateDemandValue(value, plan, fields = ['demand', 'insideDemand']) {
  const updated = { ...value }
  if (fields.includes('demand')) updated.demand = value.isOpen === false ? null : staffRequired(value.deliveries, plan.deliveriesPerDriverHour ?? 2.7)
  if (fields.includes('insideDemand')) {
    const required = staffRequired(value.pizzas, plan.pizzasPerInsideHour ?? 20)
    updated.insideDemand = value.isOpen === false || required == null ? null : Math.max(1, required)
  }
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
    const requiredInsideHours = values.reduce((sum, value) => sum + number(value.insideDemand), 0)
    const isSunday = (new Date(`${plan.weekStart}T12:00:00`).getDay() + column.position) % 7 === 0
    const appliedHourlyRate = money(number(plan.hourlyRate) * (isSunday ? 1.25 : 1))
    const appliedInsideHourlyRate = money(number(plan.insideHourlyRate) * (isSunday ? 1.25 : 1))
    const driverLabourCost = money(requiredDriverHours * appliedHourlyRate)
    const insideLabourCost = money(requiredInsideHours * appliedInsideHourlyRate)
    const labourCost = money(driverLabourCost + insideLabourCost)
    const targetSales = number(column.targetSales)
    return { ...column, targetSales, requiredDriverHours, requiredInsideHours,
      deliveries: values.reduce((sum, value) => sum + number(value.deliveries), 0),
      pizzas: values.reduce((sum, value) => sum + number(value.pizzas), 0),
      missingInsideHours: values.filter((value) => value.isOpen && (value.pizzas == null || value.insideDemand == null)).length,
      isSunday, appliedHourlyRate, appliedInsideHourlyRate, driverLabourCost, insideLabourCost, labourCost,
      labourPercentage: targetSales > 0 ? money(labourCost / targetSales * 100) : null,
    }
  })
  const total = (field) => days.reduce((sum, day) => sum + number(day[field]), 0)
  const targetSales = total('targetSales')
  const labourCost = money(total('labourCost'))
  return { days, targetSales, labourCost,
    driverHours: total('requiredDriverHours'), insideHours: total('requiredInsideHours'),
    deliveries: total('deliveries'), pizzas: total('pizzas'),
    missingInsideHours: total('missingInsideHours'),
    driverLabourCost: money(total('driverLabourCost')), insideLabourCost: money(total('insideLabourCost')),
    labourPercentage: targetSales > 0 ? money(labourCost / targetSales * 100) : null,
  }
}
