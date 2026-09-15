import test from 'node:test'
import assert from 'node:assert/strict'
import {
  applyApplicationChangedEvent,
  initialApplicationEventVersions,
  reconnectApplicationEvents,
} from './applicationEvents.js'

test('application changes refresh only their own domain', () => {
  const next = applyApplicationChangedEvent(initialApplicationEventVersions, {
    type: 'holidayChanged', entityId: 'holiday',
  }, 'employee')

  assert.equal(next.holiday, 1)
  assert.equal(next.demand, 0)
  assert.equal(next.roster, 0)
})

test('inside and driver demand invalidations remain separate', () => {
  const inside = applyApplicationChangedEvent(initialApplicationEventVersions, {
    type: 'demandChanged', rosterKind: 'inside',
  })
  const drivers = applyApplicationChangedEvent(initialApplicationEventVersions, {
    type: 'demandChanged', rosterKind: 'drivers',
  })

  assert.equal(inside.demandInside, 1)
  assert.equal(inside.demandDrivers, 0)
  assert.equal(drivers.demandInside, 0)
  assert.equal(drivers.demandDrivers, 1)
})

test('an affected employee refreshes authentication after their profile changes', () => {
  const own = applyApplicationChangedEvent(initialApplicationEventVersions, {
    type: 'employeeChanged', entityId: 'employee-one',
  }, 'employee-one')
  const another = applyApplicationChangedEvent(initialApplicationEventVersions, {
    type: 'employeeChanged', entityId: 'employee-two',
  }, 'employee-one')

  assert.equal(own.auth, 1)
  assert.equal(own.employees, 1)
  assert.equal(another.auth, 0)
  assert.equal(another.employees, 1)
})

test('scheduling sick leave refreshes availability-derived data', () => {
  const next = applyApplicationChangedEvent(initialApplicationEventVersions, {
    type: 'sickLeaveChanged', rosterKind: 'drivers',
  })

  assert.equal(next.sickLeave, 1)
  assert.equal(next.availability, 1)
})

test('reconnect invalidates every domain to recover missed events', () => {
  const next = reconnectApplicationEvents(initialApplicationEventVersions)
  for (const value of Object.values(next)) assert.equal(value, 1)
})
