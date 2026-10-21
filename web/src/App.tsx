import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, CLIENT_ID, api, toApiError } from './api'
import { NewTransferForm } from './components/NewTransferForm'
import { StockPanel } from './components/StockPanel'
import { TransferList } from './components/TransferList'
import { TransferGlyph } from './components/icons'
import { ErrorBanner, Toast, type ToastTone } from './components/ui'
import type { Branch, Product, Transfer, TransferAction, TransferStatus } from './types'
import { useLiveUpdates, type LiveState, type TransferChanged } from './useLiveUpdates'

const ACTION_MESSAGES: Record<TransferAction, (t: Transfer) => string> = {
  dispatch: (t) => `Transfer #${t.id} dispatched from ${t.fromBranch.name}`,
  receive: (t) => `Transfer #${t.id} received at ${t.toBranch.name}`,
  cancel: (t) => `Transfer #${t.id} cancelled`,
}

// When the server reports a replay, the action had already happened (often by another person): say so honestly.
const REPLAY_MESSAGES: Record<TransferAction, (t: Transfer) => string> = {
  dispatch: (t) => `Transfer #${t.id} was already dispatched. No stock moved again.`,
  receive: (t) => `Transfer #${t.id} was already received. No stock moved again.`,
  cancel: (t) => `Transfer #${t.id} was already cancelled.`,
}

// Changes made by someone else, announced as they happen.
const REMOTE_MESSAGES: Record<TransferStatus, (e: TransferChanged) => string> = {
  Requested: (e) => `New request #${e.transferId}: ${e.fromBranch.name} → ${e.toBranch.name}`,
  Dispatched: (e) => `Transfer #${e.transferId} dispatched: ${e.fromBranch.name} → ${e.toBranch.name}`,
  Received: (e) => `Transfer #${e.transferId} received at ${e.toBranch.name}`,
  Cancelled: (e) => `Transfer #${e.transferId} was cancelled`,
}

const LIVE_LABELS: Record<LiveState, { label: string; title: string }> = {
  connecting: { label: 'Connecting…', title: 'Connecting to live updates' },
  live: { label: 'Live', title: "Changes made by other people appear here as they happen" },
  reconnecting: { label: 'Reconnecting…', title: 'Live updates paused. Reconnecting; anything missed will be fetched.' },
  offline: { label: 'Offline', title: 'Live updates are unavailable. Refresh the page to try again.' },
}

function LiveIndicator({ state }: { state: LiveState }) {
  const { label, title } = LIVE_LABELS[state]
  return (
    <span className={`live live-${state}`} title={title} role="status" aria-live="polite">
      <span className="live-dot" aria-hidden="true" />
      {label}
    </span>
  )
}

export default function App() {
  const [reference, setReference] = useState<{ branches: Branch[]; products: Product[] } | null>(null)
  const [loadError, setLoadError] = useState<ApiError | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [version, setVersion] = useState(0)
  const [toast, setToast] = useState<{ id: number; message: string; tone: ToastTone } | null>(null)

  useEffect(() => {
    let ignore = false
    Promise.all([api.branches(), api.products()])
      .then(([branches, products]) => {
        if (!ignore) {
          setReference({ branches, products })
          setLoadError(null)
        }
      })
      .catch((err) => { if (!ignore) setLoadError(toApiError(err)) })
    return () => { ignore = true }
  }, [attempt])

  useEffect(() => {
    if (!toast) return
    const timer = setTimeout(() => setToast(null), 3200)
    return () => clearTimeout(timer)
  }, [toast])

  // Any change refreshes every view (list, stock panel, availability hints).
  const notify = useCallback((message: string, tone: ToastTone = 'success') => {
    setVersion((v) => v + 1)
    setToast({ id: Date.now(), message, tone })
  }, [])

  // Live updates from other people. A burst of events (e.g. a dispatch then a receive) refreshes once.
  const [remoteHighlight, setRemoteHighlight] = useState<number | null>(null)
  const refreshTimer = useRef<number | undefined>(undefined)
  const scheduleRefresh = useCallback(() => {
    window.clearTimeout(refreshTimer.current)
    refreshTimer.current = window.setTimeout(() => setVersion((v) => v + 1), 150)
  }, [])

  const live = useLiveUpdates(
    (event) => {
      if (event.origin === CLIENT_ID) return // our own change: already refreshed and announced
      scheduleRefresh()
      setRemoteHighlight(event.transferId)
      setToast({ id: Date.now(), message: REMOTE_MESSAGES[event.status](event), tone: 'info' })
    },
    scheduleRefresh,
  )

  return (
    <>
      <header className="app-header">
        <div className="app-header-inner">
          <span className="app-icon"><TransferGlyph /></span>
          <div>
            <h1 className="app-title">Stock Transfers</h1>
            <p className="app-subtitle">Mihira Stores · Warehouse, Colombo, Kandy and Galle</p>
          </div>
          <LiveIndicator state={live} />
        </div>
      </header>

      {reference === null ? (
        <main className="layout" style={{ display: 'block', maxWidth: 560 }}>
          {loadError ? (
            <section className="card">
              <ErrorBanner error={loadError} onDismiss={() => setLoadError(null)} />
              <button type="button" className="btn btn-filled" onClick={() => setAttempt((n) => n + 1)}>Try Again</button>
            </section>
          ) : (
            <div className="loading"><span className="spinner large" aria-label="Loading" /></div>
          )}
        </main>
      ) : (
        <main className="layout">
          <div className="column column-side">
            <NewTransferForm
              branches={reference.branches}
              products={reference.products}
              version={version}
              onCreated={(t, replayed) => replayed
                ? notify(`Transfer #${t.id} was already requested. No duplicate was created.`, 'info')
                : notify(`Transfer #${t.id} requested: ${t.fromBranch.name} → ${t.toBranch.name}`)}
            />
            <StockPanel branches={reference.branches} version={version} />
          </div>
          <div className="column">
            <TransferList
              branches={reference.branches}
              version={version}
              highlightId={remoteHighlight}
              onChanged={(t, action, replayed) => replayed
                ? notify(REPLAY_MESSAGES[action](t), 'info')
                : notify(ACTION_MESSAGES[action](t))}
            />
          </div>
        </main>
      )}

      {toast && <Toast key={toast.id} message={toast.message} tone={toast.tone} />}
    </>
  )
}
