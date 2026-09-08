const number = (value) => Number(value ?? 0)
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
