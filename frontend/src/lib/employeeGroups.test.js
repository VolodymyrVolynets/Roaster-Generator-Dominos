import test from 'node:test'
import assert from 'node:assert/strict'
import { driverRosterOnly, employeeMatchesRosterKind, getRosterRoleGroup, rosterForKind, rosterRoleGroups } from './employeeGroups.js'

test('saved roster groups put managers before in-store employees and keep drivers separate', () => {
  assert.deepEqual(rosterRoleGroups.map((group) => group.label), ['Managers', 'In-store', 'Drivers'])
  assert.equal(getRosterRoleGroup({ roles: ['Driver'] }).id, 'drivers')
  assert.equal(getRosterRoleGroup({ roles: ['InStore'] }).id, 'instore')
  assert.equal(getRosterRoleGroup({ roles: ['Manager'] }).id, 'managers')
  assert.equal(getRosterRoleGroup({ roles: ['Admin'] }), null)
})

test('inside roles take precedence over driver roles', () => {
  assert.equal(getRosterRoleGroup({ roles: ['Driver', 'Manager'] }).id, 'managers')
  assert.equal(getRosterRoleGroup({ roles: ['Driver', 'InStore'] }).id, 'instore')
  assert.equal(getRosterRoleGroup({ roles: ['driver', 'instore', 'manager'] }).id, 'managers')
  assert.equal(employeeMatchesRosterKind({ roles: ['Driver', 'Manager'] }, 'drivers'), false)
  assert.equal(employeeMatchesRosterKind({ roles: ['Driver', 'Manager'] }, 'inside'), true)
})

test('legacy roster employees without role snapshots remain in the driver group', () => {
  assert.equal(getRosterRoleGroup({ employeeName: 'Legacy driver' }).id, 'drivers')
})

test('roster payloads stay isolated by selected kind', () => {
  assert.equal(driverRosterOnly({ rosterKind: 'inside', employees: [{ employeeName: 'Hidden manager' }] }), null)
  assert.equal(rosterForKind({ rosterKind: 'drivers', employees: [] }, 'inside'), null)
  assert.equal(driverRosterOnly(null), null)
})

test('saved roster data excludes explicit inside snapshots and preserves older drivers', () => {
  const source = { employees: [
    { employeeName: 'Driver', roles: ['Driver'] },
    { employeeName: 'Legacy driver' },
    { employeeName: 'Manager', roles: ['Manager'] },
    { employeeName: 'Shop worker', roles: ['InStore'] },
  ] }
  assert.deepEqual(driverRosterOnly(source).employees.map((employee) => employee.employeeName), ['Driver', 'Legacy driver'])
  assert.deepEqual(rosterForKind({ ...source, rosterKind: 'inside' }, 'inside').employees.map((employee) => employee.employeeName), ['Manager', 'Shop worker'])
  assert.equal(source.employees.length, 4)
})
