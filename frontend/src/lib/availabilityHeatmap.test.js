import assert from 'node:assert/strict'
import test from 'node:test'
import {
  availabilityHeatmapDetail,
  availabilityHeatmapStatus,
  buildAvailabilityHeatmapDays,
  buildAvailabilityHeatmapGrid,
} from './availabilityHeatmap.js'

test('heatmap grid keeps business hours ordered through midnight and includes every weekday', () => {
  const heatmap = {
    slots: [
      { date: '2026-09-14', hour: 24, startTime: '00:00' },
      { date: '2026-09-15', hour: 12, startTime: '12:00' },
      { date: '2026-09-14', hour: 23, startTime: '23:00' },
    ],
  }

  const grid = buildAvailabilityHeatmapGrid(heatmap, '2026-09-14')

  assert.deepEqual(grid.hours, [12, 23, 24])
  assert.equal(grid.dates.length, 7)
  assert.equal(grid.dates[0], '2026-09-14')
  assert.equal(grid.dates[6], '2026-09-20')
  assert.equal(grid.slots.get('2026-09-14:24').startTime, '00:00')
})

test('heatmap details explain shortages and spare availability', () => {
  assert.equal(availabilityHeatmapDetail({ availableDrivers: 2, requiredDrivers: 4, shortageDrivers: 2 }),
    '2 available for 4 required · need 2 more')
  assert.equal(availabilityHeatmapDetail({ availableDrivers: 3, requiredDrivers: 3, shortageDrivers: 0 }),
    '3 available for 3 required · no spare availability')
  assert.equal(availabilityHeatmapDetail({ availableDrivers: 5, requiredDrivers: 3, shortageDrivers: 0 }),
    '5 available for 3 required · 2 spare')
  assert.equal(availabilityHeatmapDetail(null), 'No drivers required')
})

test('mobile days preserve overnight order, empty days and the most urgent status', () => {
  const slots = [
    { date: '2026-09-14', hour: 24, shortageDrivers: 1, level: 'shortage' },
    { date: '2026-09-14', hour: 12, shortageDrivers: 0, level: 'covered' },
    { date: '2026-09-14', hour: 23, shortageDrivers: 3, level: 'shortage' },
    { date: '2026-09-15', hour: 12, shortageDrivers: 0, level: 'limited' },
    { date: '2026-09-15', hour: 13, shortageDrivers: 0, level: 'tight' },
    { date: '2026-09-20', hour: 24, shortageDrivers: 0, level: 'covered' },
  ]
  const days = buildAvailabilityHeatmapDays(buildAvailabilityHeatmapGrid({ slots }, '2026-09-14'))

  assert.equal(days.length, 7)
  assert.deepEqual(days[0].slots.map((slot) => slot.hour), [12, 23, 24])
  assert.equal(days[0].shortageHours, 2) // Hours, not the sum of missing drivers.
  assert.equal(days[0].level, 'shortage')
  assert.equal(days[1].level, 'tight')
  assert.equal(days[1].shortageHours, 0)
  assert.deepEqual(days[2], { date: '2026-09-16', slots: [], shortageHours: 0, level: 'empty' })
  assert.equal(days[6].slots[0].date, '2026-09-20')
  assert.equal(days[6].slots[0].hour, 24)
})

test('mobile summaries follow the selected week and safely handle missing demand', () => {
  const heatmap = { slots: [{ date: '2026-09-21', hour: 12, shortageDrivers: 1, level: 'shortage' }] }
  const days = buildAvailabilityHeatmapDays(buildAvailabilityHeatmapGrid(heatmap, '2026-09-21'))
  assert.equal(days[0].date, '2026-09-21')
  assert.equal(days[6].date, '2026-09-27')
  assert.equal(days[0].shortageHours, 1)
  assert.deepEqual(buildAvailabilityHeatmapDays(buildAvailabilityHeatmapGrid(null, '2026-09-21')), [])
})

test('mobile cards explain every colour without relying on hover or colour alone', () => {
  assert.equal(availabilityHeatmapStatus({ availableDrivers: 0, requiredDrivers: 3, shortageDrivers: 3 }), 'Need 3 more')
  assert.equal(availabilityHeatmapStatus({ availableDrivers: 2, requiredDrivers: 3, shortageDrivers: 1 }), 'Need 1 more')
  assert.equal(availabilityHeatmapStatus({ availableDrivers: 3, requiredDrivers: 3, shortageDrivers: 0 }), 'No spare')
  assert.equal(availabilityHeatmapStatus({ availableDrivers: 4, requiredDrivers: 3, shortageDrivers: 0 }), '1 spare')
  assert.equal(availabilityHeatmapStatus({ availableDrivers: 6, requiredDrivers: 3, shortageDrivers: 0 }), '3 spare')
  assert.equal(availabilityHeatmapStatus(null), 'No drivers required')
})
