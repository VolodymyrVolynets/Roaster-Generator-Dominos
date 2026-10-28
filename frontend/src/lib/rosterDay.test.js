import test from 'node:test'
import assert from 'node:assert/strict'
import { getRosterDay } from './rosterDay.js'

const shift = (date, startTime, startDayOffset = 0) => ({ date, startTime, startDayOffset })
const employee = (employeeName, shifts) => ({ employeeId: employeeName, employeeName, shifts })

test('lists the day by start time, then name, with after-midnight starts last', () => {
  const employees = [
    employee('Cara', [shift('2026-11-02', '18:00')]),
    employee('Ann', [shift('2026-11-02', '18:00')]),
    employee('Ben', [shift('2026-11-02', '12:00')]),
    employee('Dan', [shift('2026-11-02', '00:00', 1)]),
  ]
  const { working, offCount } = getRosterDay(employees, '2026-11-02')
  assert.deepEqual(working.map((item) => item.employee.employeeName), ['Ben', 'Ann', 'Cara', 'Dan'])
  assert.equal(offCount, 0)
})

test('counts employees with no shift that day as off', () => {
  const employees = [
    employee('Ann', [shift('2026-11-03', '12:00')]),
    employee('Ben', []),
    employee('Cara', [shift('2026-11-02', '16:00')]),
  ]
  const { working, offCount } = getRosterDay(employees, '2026-11-02')
  assert.equal(working.length, 1)
  assert.equal(offCount, 2)
})

test('an employee with two shifts on one day appears twice', () => {
  const employees = [employee('Ann', [shift('2026-11-02', '12:00'), shift('2026-11-02', '19:00')])]
  assert.equal(getRosterDay(employees, '2026-11-02').working.length, 2)
})
