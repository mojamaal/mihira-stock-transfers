import { useEffect, useRef, useState } from 'react'
import type { TransferStatus } from './types'

/** Mirrors the API's TransferChanged event (src/MihiraStores.Api/Live/TransferEvents.cs). */
export type TransferChanged = {
  transferId: number
  status: TransferStatus
  fromBranch: { id: number; name: string }
  toBranch: { id: number; name: string }
  /** The client that made the change, so a tab can ignore its own echo. */
  origin: string | null
  at: string
}

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline'

/**
 * Subscribes to the API's server-sent events. The browser's EventSource reconnects on its own; after every
 * reconnect the server sends "ready" and we call `onResync`, so anything missed while disconnected is refetched.
 */
export function useLiveUpdates(onChange: (event: TransferChanged) => void, onResync: () => void): LiveState {
  const [state, setState] = useState<LiveState>('connecting')
  const handlers = useRef({ onChange, onResync })

  // Keep the latest callbacks without tearing down the connection when they change.
  useEffect(() => {
    handlers.current = { onChange, onResync }
  })

  useEffect(() => {
    const source = new EventSource('/api/events')
    let connectedBefore = false

    source.addEventListener('ready', () => {
      setState('live')
      if (connectedBefore) handlers.current.onResync()
      connectedBefore = true
    })
    source.addEventListener('transfer', (e) => {
      handlers.current.onChange(JSON.parse((e as MessageEvent<string>).data) as TransferChanged)
    })
    source.onerror = () => setState(source.readyState === EventSource.CLOSED ? 'offline' : 'reconnecting')

    return () => source.close()
  }, [])

  return state
}
