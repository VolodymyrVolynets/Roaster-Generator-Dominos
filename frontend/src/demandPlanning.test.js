import test from 'node:test'
import assert from 'node:assert/strict'
import { calculateDemandSummary, recalculateDemandPlan, recalculateDemandValue } from './demandPlanning.js'

const settings = { deliveriesPerDriverHour: 2.7 }

test('driver productivity rounds up without floating-point extra staffing', () => {
  assert.deepEqual(recalculateDemandValue({ isOpen: true, deliveries: 8.1 }, settings), {
    isOpen: true, deliveries: 8.1, demand: 3,
  })
  const quiet = recalculateDemandValue({ isOpen: true, deliveries: 0 }, settings)
  assert.equal(quiet.demand, 0)
  assert.equal(recalculateDemandValue({ deliveries: 2.1 }, { ...settings, deliveriesPerDriverHour: 0.3 }).demand, 7)
})

test('missing deliveries stay unknown and closed hours do not create staffing', () => {
  const missing = recalculateDemandValue({ isOpen: true, deliveries: null }, settings)
  assert.equal(missing.demand, null)
  const closed = recalculateDemandValue({ isOpen: false, deliveries: 10 }, settings)
  assert.equal(closed.demand, null)
})

test('editing deliveries updates driver staffing without mutating source data', () => {
  const value = { isOpen: true, deliveries: 9, demand: 2 }
  const edited = recalculateDemandValue(value, settings, ['demand'])
  assert.equal(edited.demand, 4)
  assert.equal(value.demand, 2)
})

test('productivity changes recalculate all row previews without mutating source data', () => {
  const original = { ...settings, rows: [{ hour: 12, values: [{ position: 0, isOpen: true, deliveries: 12, demand: 5 }] }] }
  const preview = recalculateDemandPlan({ ...original, deliveriesPerDriverHour: 6 })
  assert.equal(preview.rows[0].values[0].demand, 2)
  assert.equal(original.rows[0].values[0].demand, 5)
})

test('daily and weekly staffing use driver demand and sales without legacy pizza or inside totals', () => {
  const plan = {
    weekStart: '2026-09-14',
    columns: [{ position: 0, label: 'Monday', targetSales: 1000, totalHours: 99 }, { position: 6, label: 'Sunday', targetSales: 500 }],
    rows: [{ hour: 12, values: [
      { position: 0, isOpen: true, deliveries: 6, pizzas: 40, demand: 2, insideDemand: 3 },
      { position: 6, isOpen: true, deliveries: 9, pizzas: 20, demand: 4, insideDemand: 2 },
    ] }],
  }
  const summary = calculateDemandSummary(plan)
  assert.equal(summary.days[0].requiredDriverHours, 2)
  assert.equal(summary.days[1].requiredDriverHours, 4)
  assert.equal(summary.driverHours, 6)
  assert.equal(summary.targetSales, 1500)
  assert.equal(summary.deliveries, 15)
  assert.equal('pizzas' in summary, false)
  assert.equal('insideHours' in summary, false)
  assert.equal('requiredInsideHours' in summary.days[0], false)
})

test('missing demand plans and closed hours show no driver hours', () => {
  assert.deepEqual(calculateDemandSummary(null), { days: [], targetSales: 0, driverHours: 0, deliveries: 0 })
  const summary = calculateDemandSummary({ weekStart: '2026-09-14',
    columns: [{ position: 0, label: 'Monday', targetSales: 0 }],
    rows: [{ hour: 12, values: [{ position: 0, isOpen: false, demand: 1, deliveries: 5 }] }],
  })
  assert.equal(summary.driverHours, 0)
  assert.equal(summary.targetSales, 0)
  assert.equal(summary.deliveries, 0)
})
