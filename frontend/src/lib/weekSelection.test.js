import test from 'node:test'
import assert from 'node:assert/strict'
import { getMonday, getRelativeWeekLabel, getWeekOffsetFromDate, getWeekStartValue } from './weekSelection.js'

test('week selection normalizes every date to Monday', () => {
  assert.equal(getMonday(new Date('2026-09-17T18:00:00Z')).toISOString().slice(0, 10), '2026-09-14')
})

test('week offsets and week starts round trip', () => {
  const today = new Date('2026-09-15T12:00:00Z')
  assert.equal(getWeekStartValue(1, today), '2026-09-21')
  assert.equal(getWeekOffsetFromDate('2026-09-27', today), 1)
  assert.equal(getWeekOffsetFromDate('2026-09-07', today), -1)
})

test('relative labels describe common and historical selections', () => {
  assert.equal(getRelativeWeekLabel(0), 'Current week')
  assert.equal(getRelativeWeekLabel(2), 'Week after next')
  assert.equal(getRelativeWeekLabel(-3), '3 weeks ago')
  assert.equal(getRelativeWeekLabel(5), '5 weeks ahead')
})
