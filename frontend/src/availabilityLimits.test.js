import test from 'node:test'
import assert from 'node:assert/strict'
import { countAvailabilityDays, getAvailabilityLimitErrors, isAvailabilityTimeAllowed } from './availabilityLimits.js'

const driverSchedule = (days) => ({ days, maximumAvailabilityHoursPerDay: 10, maximumAvailabilityDaysPerWeek: 6 })
const day = (startTime, finishTime) => ({ dayOfWeek: 'Monday', startTime, finishTime })

test('ten hours is allowed during the day and across midnight; eleven is blocked', () => {
  for (const [start, finish, allowed] of [
    ['12:00', '22:00', true], ['15:00', '01:00', true],
    ['12:00', '23:00', false], ['14:00', '01:00', false],
    ['18:00', '04:00', true], ['12:00', '04:00', false],
  ]) {
    assert.equal(isAvailabilityTimeAllowed(day(start, null), 'finishTime', finish, 10), allowed)
    assert.equal(isAvailabilityTimeAllowed(day(null, finish), 'startTime', start, 10), allowed)
    assert.equal(getAvailabilityLimitErrors(driverSchedule([day(start, finish)])).length, allowed ? 0 : 1)
  }
})

test('partial entries reserve one day and resetting frees that day', () => {
  const days = [day('12:00', null), day(null, '01:00'), day('15:00', '01:00'), day(null, null)]
  assert.equal(countAvailabilityDays(days), 3)
  assert.equal(countAvailabilityDays([day(null, null), ...days.slice(1)]), 2)
  assert.equal(isAvailabilityTimeAllowed(day('12:00', '01:00'), 'finishTime', '', 10), true)
  assert.equal(isAvailabilityTimeAllowed(day(null, null), 'startTime', '12:00', 10), true)
  assert.equal(isAvailabilityTimeAllowed(day('12:00', null), 'finishTime', '12:00', 10), false)
})

test('six entered days can be saved while seven are rejected', () => {
  const six = Array.from({ length: 6 }, () => day('15:00', '01:00'))
  assert.deepEqual(getAvailabilityLimitErrors(driverSchedule([...six, day(null, null)])), [])
  assert.match(getAvailabilityLimitErrors(driverSchedule([...six, day('12:00', '20:00')]))[0], /at most 6 days/)
})

test('admin and inside responses without limits permit longer and seven-day availability', () => {
  const days = Array.from({ length: 7 }, () => day('12:00', '01:00'))
  assert.deepEqual(getAvailabilityLimitErrors({ days }), [])
  assert.deepEqual(getAvailabilityLimitErrors({ days, maximumAvailabilityHoursPerDay: null, maximumAvailabilityDaysPerWeek: null }), [])
  assert.equal(isAvailabilityTimeAllowed(day('12:00', null), 'finishTime', '01:00', null), true)
})

test('existing over-limit availability is flagged without changing saved values', () => {
  const schedule = driverSchedule(Array.from({ length: 7 }, () => day('12:00', '01:00')))
  const before = structuredClone(schedule)
  assert.equal(getAvailabilityLimitErrors(schedule).length, 8)
  assert.deepEqual(schedule, before)
})
