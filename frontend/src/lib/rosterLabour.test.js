import test from 'node:test'
import assert from 'node:assert/strict'
import { formatLabourMoney, labourDisplay } from './rosterLabour.js'

test('missing roster is shown as not generated instead of zero labour', () => {
  assert.deepEqual(labourDisplay({ isComplete: false, scheduledHours: 0, labourCost: 0, labourPercentage: null }), {
    hours: '—', cost: '—', percentage: '—', status: 'Not generated',
  })
})

test('incomplete driver cost is unavailable rather than a partial subtotal', () => {
  const display = labourDisplay({ isComplete: false, scheduledHours: 8, labourCost: 116, labourPercentage: 50 })
  assert.equal(display.hours, '—')
  assert.equal(display.cost, '—')
  assert.equal(display.percentage, '—')
  assert.equal(display.status, 'Not generated')
})

test('complete saved week uses API cost and percentage without estimating from demand', () => {
  assert.deepEqual(labourDisplay({ isComplete: true, scheduledHours: 7.5, labourCost: 108.75, labourPercentage: 12.345 }), {
    hours: '7.5h', cost: '€108.75', percentage: '12.35%', status: 'Saved shifts',
  })
  assert.equal(labourDisplay({ isComplete: true, scheduledHours: 0, labourCost: 0, labourPercentage: 0 }).cost, '€0.00')
})

test('missing target sales and invalid amounts stay unavailable', () => {
  assert.equal(labourDisplay({ isComplete: true, scheduledHours: 5, labourCost: 72.5, labourPercentage: null }).percentage, '—')
  assert.equal(formatLabourMoney(null), '—')
  assert.equal(formatLabourMoney(undefined), '—')
  assert.equal(formatLabourMoney(Number.NaN), '—')
  assert.equal(formatLabourMoney(0), '€0.00')
})
