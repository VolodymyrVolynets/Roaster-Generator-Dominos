import test from 'node:test'
import assert from 'node:assert/strict'
import { calculateDemandSummary, recalculateDemandPlan, recalculateDemandValue } from './demandPlanning.js'

const settings = { deliveriesPerDriverHour: 2.7, pizzasPerInsideHour: 20 }

test('productivity rounds staff up and retains a manager for quiet open hours', () => {
  assert.deepEqual(recalculateDemandValue({ isOpen: true, deliveries: 8.1, pizzas: 41 }, settings), {
    isOpen: true, deliveries: 8.1, pizzas: 41, demand: 3, insideDemand: 3,
  })
  const quiet = recalculateDemandValue({ isOpen: true, deliveries: 0, pizzas: 0 }, settings)
  assert.equal(quiet.demand, 0)
  assert.equal(quiet.insideDemand, 1)
  assert.equal(recalculateDemandValue({ deliveries: 2.1 }, { ...settings, deliveriesPerDriverHour: 0.3 }).demand, 7)
})

test('missing pizza counts stay incomplete and closed hours never create inside staffing', () => {
  const missing = recalculateDemandValue({ isOpen: true, deliveries: 2, pizzas: null, insideDemand: 4 }, settings)
  assert.equal(missing.insideDemand, null)
  const closed = recalculateDemandValue({ isOpen: false, deliveries: 10, pizzas: 80 }, settings)
  assert.equal(closed.demand, null)
  assert.equal(closed.insideDemand, null)
})

test('changing one workload updates its demand without resetting the other manual demand', () => {
  const value = { isOpen: true, deliveries: 9, pizzas: 40, demand: 2, insideDemand: 5 }
  const edited = recalculateDemandValue(value, settings, ['demand'])
  assert.equal(edited.demand, 4)
  assert.equal(edited.insideDemand, 5)
  assert.equal(value.demand, 2)
})

test('productivity changes recalculate all row previews without mutating source data', () => {
  const original = { ...settings, rows: [{ hour: 12, values: [{ position: 0, isOpen: true, deliveries: 12, pizzas: 60, demand: 5, insideDemand: 3 }] }] }
  const preview = recalculateDemandPlan({ ...original, deliveriesPerDriverHour: 6, pizzasPerInsideHour: 30 })
  assert.equal(preview.rows[0].values[0].demand, 2)
  assert.equal(preview.rows[0].values[0].insideDemand, 2)
  assert.equal(original.rows[0].values[0].demand, 5)
})

test('daily and weekly staffing use edited demand and sales for both teams', () => {
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
  assert.equal(summary.days[0].requiredInsideHours, 3)
  assert.equal(summary.days[1].requiredDriverHours, 4)
  assert.equal(summary.days[1].requiredInsideHours, 2)
  assert.equal(summary.driverHours, 6)
  assert.equal(summary.insideHours, 5)
  assert.equal(summary.targetSales, 1500)
  assert.equal(summary.deliveries, 15)
  assert.equal(summary.pizzas, 60)
})

test('legacy missing inside demand stays visible with zero sales', () => {
  const summary = calculateDemandSummary({ weekStart: '2026-09-14',
    columns: [{ position: 0, label: 'Monday', targetSales: 0 }],
    rows: [{ hour: 12, values: [{ position: 0, isOpen: true, demand: 1, pizzas: null, insideDemand: null }] }],
  })
  assert.equal(summary.missingInsideHours, 1)
  assert.equal(summary.targetSales, 0)
  assert.equal(summary.days[0].requiredInsideHours, 0)
})
