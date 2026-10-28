const moneyFormatter = new Intl.NumberFormat('en-IE', { style: 'currency', currency: 'EUR' })
const hoursFormatter = new Intl.NumberFormat('en-IE', { maximumFractionDigits: 2 })
const isNumber = (value) => typeof value === 'number' && Number.isFinite(value)

export const formatLabourMoney = (value) => isNumber(value) ? moneyFormatter.format(value) : '—'

export function labourDisplay(group) {
  const available = group?.isComplete
  if (!available) return { hours: '—', cost: '—', percentage: '—', status: 'Not generated' }
  return {
    hours: isNumber(group.scheduledHours) ? `${hoursFormatter.format(group.scheduledHours)}h` : '—',
    cost: formatLabourMoney(group.labourCost),
    percentage: group.isComplete && isNumber(group.labourPercentage) ? `${group.labourPercentage.toFixed(2)}%` : '—',
    status: 'Saved shifts',
  }
}
