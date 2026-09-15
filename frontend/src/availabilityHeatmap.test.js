import assert from 'node:assert/strict'
import test from 'node:test'
import { availabilityHeatmapDetail, buildAvailabilityHeatmapGrid } from './availabilityHeatmap.js'

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
