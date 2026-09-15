import { useEffect, useState } from 'react'
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'
import {
  applyApplicationChangedEvent,
  initialApplicationEventVersions,
  reconnectApplicationEvents,
} from './applicationEvents'

export function useApplicationEvents(authenticated, employeeId) {
  const [versions, setVersions] = useState(initialApplicationEventVersions)

  useEffect(() => {
    if (!authenticated) return undefined

    let active = true
    let retryTimer
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/application-events', {
        transport: HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) =>
          Math.min(30000, 1000 * 2 ** Math.min(context.previousRetryCount, 5)),
      })
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('applicationChanged', (event) => {
      if (active) {
        setVersions((current) => applyApplicationChangedEvent(current, event, employeeId))
      }
    })
    connection.onreconnected(() => {
      if (active) setVersions(reconnectApplicationEvents)
    })
    connection.onclose(() => {
      if (active) retryTimer = window.setTimeout(connect, 5000)
    })

    async function connect() {
      try {
        await connection.start()
      } catch {
        if (active) retryTimer = window.setTimeout(connect, 5000)
      }
    }

    void connect()
    const refreshOnFocus = () => setVersions(reconnectApplicationEvents)
    window.addEventListener('focus', refreshOnFocus)

    return () => {
      active = false
      window.clearTimeout(retryTimer)
      window.removeEventListener('focus', refreshOnFocus)
      void connection.stop()
    }
  }, [authenticated, employeeId])

  return versions
}
