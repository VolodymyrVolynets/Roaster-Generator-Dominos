// Keep server timestamp precision: two jobs can start and finish in one millisecond.
function timestampTicks(event) {
  const timestamp = event?.timestampUtc || ''
  const milliseconds = Date.parse(timestamp)
  if (!Number.isFinite(milliseconds)) return 0n
  const fraction = timestamp.match(/\.(\d+)(?:Z|[+-]\d{2}:\d{2})$/)?.[1] || ''
  return BigInt(milliseconds) * 10000n + BigInt(fraction.slice(3, 7).padEnd(4, '0'))
}

function compareEvents(left, right) {
  const leftTime = timestampTicks(left)
  const rightTime = timestampTicks(right)
  return leftTime < rightTime ? -1 : leftTime > rightTime ? 1 : Number(left.sequence || 0) - Number(right.sequence || 0)
}

export function createRosterEventState() {
  return { generation: null, logs: [], retiredJobs: new Set() }
}

export function mergeRosterEvents(state, events, weekOffset) {
  let generation = state.generation
  let logs = [...state.logs]
  const retiredJobs = new Set(state.retiredJobs)
  const ordered = events.filter((entry) => entry && entry.weekOffset === weekOffset && entry.jobId).sort(compareEvents)

  for (const payload of ordered) {
    if (retiredJobs.has(payload.jobId)) continue
    if (generation?.jobId !== payload.jobId) {
      // A delayed HTTP response or replay never replaces a more recent job.
      if (generation && timestampTicks(payload) <= timestampTicks(generation)) continue
      if (generation) retiredJobs.add(generation.jobId)
      generation = payload
      logs = []
    }
    const logIndex = logs.findIndex((entry) => entry.sequence === payload.sequence)
    if (payload.sequence != null && logIndex < 0) logs.push(payload)
    else if (logIndex >= 0 && payload.severity) logs[logIndex] = payload
    if (Number(payload.sequence || 0) >= Number(generation.sequence || 0)) generation = payload
  }

  logs.sort((left, right) => Number(left.sequence || 0) - Number(right.sequence || 0))
  return { generation, logs, retiredJobs }
}
