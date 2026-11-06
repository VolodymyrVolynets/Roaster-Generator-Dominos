import test from 'node:test'
import assert from 'node:assert/strict'
import { countAvailabilityDays, getAvailabilityLimitErrors, getAvailabilityDemandError, isAvailabilityTimeAllowed } from './availabilityLimits.js'

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

test('hours outside shop opening times are blocked, including after-midnight closing', () => {
  const shopDay = (startTime, finishTime, closingTime = '01:00') =>
    ({ ...day(startTime, finishTime), openingTime: '12:00', closingTime })
  assert.equal(isAvailabilityTimeAllowed(shopDay(null, null), 'startTime', '11:00', 10), false)
  assert.equal(isAvailabilityTimeAllowed(shopDay(null, null), 'startTime', '12:00', 10), true)
  assert.equal(isAvailabilityTimeAllowed(shopDay(null, null), 'startTime', '00:00', 10), false)
  assert.equal(isAvailabilityTimeAllowed(shopDay('16:00', null), 'finishTime', '01:00', 10), true)
  assert.equal(isAvailabilityTimeAllowed(shopDay('16:00', null), 'finishTime', '02:00', 10), false)
  assert.equal(isAvailabilityTimeAllowed(shopDay('18:00', null, '04:00'), 'finishTime', '04:00', 10), true)
  assert.equal(isAvailabilityTimeAllowed(shopDay(null, null), 'finishTime', '02:00', 10), false)
  assert.equal(isAvailabilityTimeAllowed(day(null, null), 'startTime', '11:00', 10), true)
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

test('busy-day requirement rejects other days, partial entries, and clearing previously saved busy days', () => {
  const schedule = {
    requiredBusyDates: ['2026-10-23', '2026-10-24'],
    days: [
      { ...day('14:00', '20:00'), date: '2026-10-19' },
      { ...day(null, null), date: '2026-10-23', dayOfWeek: 'Friday' },
      { ...day(null, null), date: '2026-10-24', dayOfWeek: 'Saturday' },
    ],
  }
  assert.match(getAvailabilityDemandError(schedule), /Friday.*or Saturday/)
  schedule.days[1].startTime = '14:00'
  assert.match(getAvailabilityDemandError(schedule), /at least one/)
  schedule.days[1].finishTime = '14:00'
  assert.match(getAvailabilityDemandError(schedule), /at least one/)
  schedule.days[1].finishTime = '20:00'
  assert.equal(getAvailabilityDemandError(schedule), '')
  schedule.days[1] = { ...schedule.days[1], startTime: null, finishTime: null }
  assert.match(getAvailabilityDemandError(schedule), /at least one/)
  schedule.days[2] = { ...schedule.days[2], startTime: '20:00', finishTime: '01:00' }
  assert.equal(getAvailabilityDemandError(schedule), '')
})

test('admin, inside and explicitly zero-demand responses have no busy-day requirement', () => {
  assert.equal(getAvailabilityDemandError(null), '')
  assert.equal(getAvailabilityDemandError({ days: [] }), '')
  assert.equal(getAvailabilityDemandError({ days: [], requiredBusyDates: [] }), '')
})

test('drivers cannot submit before demand is entered even if availability is already filled', () => {
  const reason = 'Outside driver demand has not been entered for this week.'
  const schedule = { availabilitySaveBlockedReason: reason, requiredBusyDates: [], days: [day('14:00', '20:00')] }
  assert.equal(getAvailabilityDemandError(schedule), reason)
  assert.equal(getAvailabilityDemandError({ ...schedule, availabilitySaveBlockedReason: null }), '')
})
