import test from 'node:test'
import assert from 'node:assert/strict'
import { addDays, initialPickerDate, isDateAllowed, monthStartOf, todayValue } from './datePicker.js'

test('addDays crosses month and year boundaries', () => {
  assert.equal(addDays('2026-10-31', 1), '2026-11-01')
  assert.equal(addDays('2026-12-31', 1), '2027-01-01')
  assert.equal(addDays('2026-03-01', -1), '2026-02-28')
  assert.equal(addDays('2026-11-02', 7), '2026-11-09')
})

test('monthStartOf returns the first day of the month in UTC', () => {
  assert.equal(monthStartOf('2026-11-18').toISOString().slice(0, 10), '2026-11-01')
})

test('isDateAllowed respects optional inclusive limits', () => {
  assert.equal(isDateAllowed('2026-11-05'), true)
  assert.equal(isDateAllowed('2026-11-05', '2026-11-05'), true)
  assert.equal(isDateAllowed('2026-11-04', '2026-11-05'), false)
  assert.equal(isDateAllowed('2026-11-06', undefined, '2026-11-05'), false)
})

test('the calendar opens on the value, today, or the nearest limit', () => {
  assert.equal(initialPickerDate('2026-11-10', null, null, '2026-10-28'), '2026-11-10')
  assert.equal(initialPickerDate('', null, null, '2026-10-28'), '2026-10-28')
  assert.equal(initialPickerDate('', '2026-11-02', null, '2026-10-28'), '2026-11-02')
  assert.equal(initialPickerDate('', null, '2026-10-01', '2026-10-28'), '2026-10-01')
})

test('todayValue uses the local calendar date', () => {
  assert.equal(todayValue(new Date(2026, 0, 5, 23, 30)), '2026-01-05')
})
