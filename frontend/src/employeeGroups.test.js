import test from 'node:test'
import assert from 'node:assert/strict'
import { getRosterRoleGroup, rosterRoleGroups } from './employeeGroups.js'

test('availability and saved rosters separate drivers from in-store staff and managers', () => {
  assert.deepEqual(rosterRoleGroups.map((group) => group.label), ['Drivers', 'In-store & managers'])
  assert.equal(getRosterRoleGroup({ roles: ['Driver'] }).id, 'drivers')
  assert.equal(getRosterRoleGroup({ roles: ['InStore'] }).id, 'inside')
  assert.equal(getRosterRoleGroup({ roles: ['Manager'] }).id, 'inside')
})

test('inside roles take priority for employees with multiple work roles', () => {
  assert.equal(getRosterRoleGroup({ roles: ['Driver', 'Manager'] }).id, 'inside')
  assert.equal(getRosterRoleGroup({ roles: ['Driver', 'InStore'] }).id, 'inside')
  assert.equal(getRosterRoleGroup({ roles: ['driver', 'instore', 'manager'] }).id, 'inside')
})

test('legacy roster employees without role snapshots remain in the driver group', () => {
  assert.equal(getRosterRoleGroup({ employeeName: 'Legacy driver' }).id, 'drivers')
})
