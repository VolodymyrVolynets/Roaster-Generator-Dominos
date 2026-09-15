export function buildAvailabilityHeatmapGrid(heatmap, weekStart) {
  if (!heatmap?.slots?.length || !weekStart) return { dates: [], hours: [], slots: new Map() }

  const start = new Date(`${weekStart}T00:00:00Z`)
  const dates = Array.from({ length: 7 }, (_, offset) => {
    const date = new Date(start)
    date.setUTCDate(start.getUTCDate() + offset)
    return date.toISOString().slice(0, 10)
  })
  const hours = [...new Set(heatmap.slots.map((slot) => slot.hour))].sort((left, right) => left - right)
  const slots = new Map(heatmap.slots.map((slot) => [`${slot.date}:${slot.hour}`, slot]))
  return { dates, hours, slots }
}

export function availabilityHeatmapDetail(slot) {
  if (!slot) return 'No drivers required'
  const coverage = `${slot.availableDrivers} available for ${slot.requiredDrivers} required`
  if (slot.shortageDrivers > 0) return `${coverage} · need ${slot.shortageDrivers} more`
  const spare = slot.availableDrivers - slot.requiredDrivers
  return `${coverage} · ${spare === 0 ? 'no spare availability' : `${spare} spare`}`
}
