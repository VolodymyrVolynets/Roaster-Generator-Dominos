import test from 'node:test'
import assert from 'node:assert/strict'
import { driverRosterOnly, getRosterRoleGroup, rosterRoleGroups } from './employeeGroups.js'

test('availability and saved rosters only display drivers', () => {
  assert.deepEqual(rosterRoleGroups.map((group) => group.label), ['Drivers'])
  assert.equal(getRosterRoleGroup({ roles: ['Driver'] }).id, 'drivers')
  assert.equal(getRosterRoleGroup({ roles: ['InStore'] }), null)
  assert.equal(getRosterRoleGroup({ roles: ['Manager'] }), null)
  assert.equal(getRosterRoleGroup({ roles: ['Admin'] }), null)
})

test('employees with in-store or manager roles stay out of driver rosters even if they also drive', () => {
  assert.equal(getRosterRoleGroup({ roles: ['Driver', 'Manager'] }), null)
  assert.equal(getRosterRoleGroup({ roles: ['Driver', 'InStore'] }), null)
  assert.equal(getRosterRoleGroup({ roles: ['driver', 'instore', 'manager'] }), null)
})

test('legacy roster employees without role snapshots remain in the driver group', () => {
  assert.equal(getRosterRoleGroup({ employeeName: 'Legacy driver' }).id, 'drivers')
})

test('legacy inside rosters never populate the driver saved roster, editor or export', () => {
  assert.equal(driverRosterOnly({ rosterKind: 'inside', employees: [{ employeeName: 'Hidden manager' }] }), null)
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
  assert.equal(source.employees.length, 4)
})
