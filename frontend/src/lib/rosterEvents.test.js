import test from 'node:test'
import assert from 'node:assert/strict'
import { createRosterEventState, mergeRosterEvents } from './rosterEvents.js'

const event = (jobId, sequence, timestampUtc, status = 'running', extra = {}) => ({
  jobId, sequence, timestampUtc, weekOffset: 1, status, message: `Event ${sequence}`, ...extra,
})
const merge = (state, ...events) => mergeRosterEvents(state, events, 1)

test('replay fills missed events without regressing a completed job', () => {
  const completed = event('a', 3, '2026-09-14T12:00:03Z', 'completed')
  let state = merge(createRosterEventState(), completed)
  state = merge(state,
    event('a', 2, '2026-09-14T12:00:02Z'),
    event('a', 1, '2026-09-14T12:00:01Z', 'started'),
    completed,
  )
  assert.equal(state.generation.status, 'completed')
  assert.deepEqual(state.logs.map((entry) => entry.sequence), [1, 2, 3])
})

test('late start acknowledgement cannot overwrite terminal live progress', () => {
  const completed = event('a', 5, '2026-09-14T12:00:05Z', 'completed')
  const state = merge(merge(createRosterEventState(), completed), event('a', 1, '2026-09-14T12:00:01Z', 'started'))
  assert.equal(state.generation, completed)
})

test('an old job replay cannot replace a new job or contaminate its logs', () => {
  let state = merge(createRosterEventState(), event('a', 4, '2026-09-14T12:00:04Z', 'completed'))
  state = merge(state, event('b', 1, '2026-09-14T12:00:05Z', 'started'))
  state = merge(state, event('a', 2, '2026-09-14T12:00:02Z'), event('a', 4, '2026-09-14T12:00:04Z', 'completed'))
  assert.equal(state.generation.jobId, 'b')
  assert.equal(state.logs.length, 1)
})

test('an unseen older job and delayed acknowledgement are rejected', () => {
  const newest = event('b', 1, '2026-09-14T12:00:05Z', 'started')
  const state = merge(merge(createRosterEventState(), newest), event('a', 1, '2026-09-14T12:00:01Z', 'started'))
  assert.equal(state.generation, newest)
  assert.equal(state.logs.length, 1)
})

test('jobs within the same millisecond retain their server timestamp order', () => {
  const old = event('a', 3, '2026-09-14T12:00:00.1234000+00:00', 'completed')
  const newest = event('b', 1, '2026-09-14T12:00:00.1235000+00:00', 'started')
  const state = merge(merge(createRosterEventState(), old), newest)
  assert.equal(state.generation, newest)
})

test('other weeks are ignored and duplicate events retain diagnostics', () => {
  const ack = event('a', 1, '2026-09-14T12:00:01Z', 'started')
  let state = merge(createRosterEventState(), ack)
  state = merge(state,
    event('b', 1, '2026-09-14T12:00:10Z', 'running', { weekOffset: 2 }),
    { ...ack, severity: 'warning', diagnostics: ['Sunday 13:00 needs one more driver.'] },
  )
  assert.equal(state.generation.jobId, 'a')
  assert.equal(state.logs.length, 1)
  assert.deepEqual(state.logs[0].diagnostics, ['Sunday 13:00 needs one more driver.'])
})

test('inside and driver jobs in the same week cannot overwrite each other', () => {
  const driver = event('driver-job', 1, '2026-09-14T12:00:01Z', 'started', { rosterKind: 'drivers' })
  const inside = event('inside-job', 2, '2026-09-14T12:00:10Z', 'completed', { rosterKind: 'inside' })
  const driverState = mergeRosterEvents(createRosterEventState(), [driver, inside], 1, 'drivers')
  const insideState = mergeRosterEvents(createRosterEventState(), [driver, inside], 1, 'inside')
  assert.equal(driverState.generation.jobId, 'driver-job')
  assert.deepEqual(driverState.logs, [driver])
  assert.equal(insideState.generation.jobId, 'inside-job')
  assert.deepEqual(insideState.logs, [inside])
})

test('legacy driver events are accepted for drivers and excluded from inside logs', () => {
  const legacy = event('legacy', 1, '2026-09-14T12:00:01Z')
  assert.equal(mergeRosterEvents(createRosterEventState(), [legacy], 1).generation.jobId, 'legacy')
  assert.equal(mergeRosterEvents(createRosterEventState(), [legacy], 1, 'inside').generation, null)
})

test('inside replay preserves terminal status and filters other weeks and roster types', () => {
  const completed = event('inside', 3, '2026-09-14T12:00:03Z', 'completed', { rosterKind: 'inside' })
  let state = mergeRosterEvents(createRosterEventState(), [completed], 1, 'inside')
  state = mergeRosterEvents(state, [
    event('inside', 1, '2026-09-14T12:00:01Z', 'started', { rosterKind: 'inside' }),
    event('other-week', 8, '2026-09-14T12:00:05Z', 'failed', { rosterKind: 'inside', weekOffset: 2 }),
    event('driver', 9, '2026-09-14T12:00:06Z', 'failed', { rosterKind: 'drivers' }),
  ], 1, 'inside')
  assert.equal(state.generation, completed)
  assert.deepEqual(state.logs.map((log) => log.sequence), [1, 3])
})
