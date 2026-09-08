import test from 'node:test'
import assert from 'node:assert/strict'
import { getAvailabilityEmployeeId } from './availabilityAccess.js'

for (const roles of [['Driver'], ['InStore'], ['Manager'], ['Driver', 'Manager']]) {
  test(`${roles.join(' + ')} can load personal availability without selecting an employee`, () => {
    assert.equal(getAvailabilityEmployeeId({ roles, employeeId: 'own-id' }, ''), 'own-id')
  })

  test(`${roles.join(' + ')} availability stays personal when another employee is selected`, () => {
    assert.equal(getAvailabilityEmployeeId({ roles, employeeId: 'own-id' }, 'other-id'), 'own-id')
  })
}

test('administrators can select any employee for availability editing', () => {
  assert.equal(getAvailabilityEmployeeId({ isAdmin: true, employeeId: 'own-id' }, 'other-id'), 'other-id')
  assert.equal(getAvailabilityEmployeeId({ isAdmin: true }, ''), '')
})

test('unlinked employees and signed-out sessions cannot load a selected employee schedule', () => {
  assert.equal(getAvailabilityEmployeeId({ roles: ['Manager'] }, 'other-id'), '')
  assert.equal(getAvailabilityEmployeeId(null, 'other-id'), '')
})
