import { useEffect, useRef, useState } from 'react'
import { ApiError, api, toApiError } from '../api'
import { TRANSFER_STATUSES, type Branch, type Transfer, type TransferAction, type TransferStatus } from '../types'
import { ArrowRight, Boxes, Refresh } from './icons'
import { ErrorBanner, Segmented, Sheet, StatusBadge } from './ui'

const dateFormat = new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' })
const formatDate = (iso: string) => dateFormat.format(new Date(iso))

const ACTION_LABELS: Record<TransferAction, { idle: string; busy: string; className: string }> = {
  dispatch: { idle: 'Dispatch', busy: 'Dispatching…', className: 'btn-filled' },
  receive: { idle: 'Receive', busy: 'Receiving…', className: 'btn-success' },
  cancel: { idle: 'Cancel', busy: 'Cancelling…', className: 'btn-destructive' },
}

// Buttons appear in this order regardless of the order the server lists them.
const ACTION_ORDER: TransferAction[] = ['cancel', 'receive', 'dispatch']

type Props = {
  branches: Branch[]
  /** Bumped by the parent after any change, so the list refetches. */
  version: number
  onChanged: (transfer: Transfer, action: TransferAction) => void
}

export function TransferList({ branches, version, onChanged }: Props) {
  const [branchId, setBranchId] = useState<number | null>(null)
  const [status, setStatus] = useState<TransferStatus | null>(null)
  const [transfers, setTransfers] = useState<Transfer[] | null>(null)
  const [settledKey, setSettledKey] = useState<string | null>(null)
  const [loadError, setLoadError] = useState<ApiError | null>(null)
  const [actionError, setActionError] = useState<ApiError | null>(null)
  const [busy, setBusy] = useState<Record<number, TransferAction>>({})
  const [confirmCancel, setConfirmCancel] = useState<Transfer | null>(null)
  const [reloadTick, setReloadTick] = useState(0)
  const [recentlyUpdated, setRecentlyUpdated] = useState<number | null>(null)

  // Synchronous guard against double-clicks landing before React re-renders the disabled button.
  // (The server is idempotent anyway; this just avoids pointless duplicate requests.)
  const inFlight = useRef(new Set<number>())

  // Loading is derived: the list is loading until the request for the current filters/version has settled.
  const requestKey = `${branchId}|${status}|${version}|${reloadTick}`
  const loading = settledKey !== requestKey

  useEffect(() => {
    let ignore = false
    api.transfers({ branchId, status })
      .then((data) => {
        if (ignore) return
        setTransfers(data)
        setLoadError(null)
      })
      .catch((err) => { if (!ignore) setLoadError(toApiError(err)) })
      .finally(() => { if (!ignore) setSettledKey(requestKey) })
    return () => { ignore = true }
  }, [branchId, status, version, reloadTick, requestKey])

  async function run(transfer: Transfer, action: TransferAction) {
    if (inFlight.current.has(transfer.id)) return
    inFlight.current.add(transfer.id)
    setBusy((b) => ({ ...b, [transfer.id]: action }))
    setActionError(null)

    try {
      const updated = await api.act(transfer.id, action)
      setTransfers((list) => list?.map((t) => (t.id === updated.id ? updated : t)) ?? list)
      setRecentlyUpdated(updated.id)
      onChanged(updated, action)
    } catch (err) {
      const error = toApiError(err)
      // Title the message by what the user tried, e.g. "Couldn't dispatch transfer #12"; the detail says why.
      setActionError(new ApiError(error.status, `Couldn't ${action} transfer #${transfer.id}`, error.message, error.fieldErrors))
      setReloadTick((n) => n + 1) // show the transfer's real state after a refusal
    } finally {
      inFlight.current.delete(transfer.id)
      setBusy((b) => {
        const next = { ...b }
        delete next[transfer.id]
        return next
      })
    }
  }

  const filtered = branchId !== null || status !== null

  return (
    <section className="card" aria-labelledby="transfers-title">
      <div className="card-header">
        <div>
          <h2 id="transfers-title" className="card-title">Transfers</h2>
          <p className="card-caption">Dispatch takes stock from the sender. Receive adds it to the receiver.</p>
        </div>
        <button
          type="button"
          className="icon-button"
          onClick={() => setReloadTick((n) => n + 1)}
          aria-label="Refresh transfers"
          title="Refresh"
        >
          {loading && transfers !== null ? <span className="spinner" aria-hidden="true" /> : <Refresh />}
        </button>
      </div>

      <div className="filters">
        <label className="visually-hidden" htmlFor="filter-branch">Filter by location</label>
        <select
          id="filter-branch"
          className="select"
          value={branchId ?? ''}
          onChange={(e) => setBranchId(e.target.value === '' ? null : Number(e.target.value))}
        >
          <option value="">All locations</option>
          {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
        </select>
        <Segmented
          label="Filter by status"
          value={status}
          onChange={setStatus}
          options={[
            { value: null, label: 'All' },
            ...TRANSFER_STATUSES.map((s) => ({ value: s, label: s === 'Dispatched' ? 'In transit' : s })),
          ]}
        />
      </div>

      {actionError && <ErrorBanner error={actionError} onDismiss={() => setActionError(null)} />}
      {loadError && <ErrorBanner error={loadError} onDismiss={() => setLoadError(null)} />}

      {transfers === null ? (
        loading && <div className="loading"><span className="spinner large" aria-label="Loading transfers" /></div>
      ) : transfers.length === 0 ? (
        <div className="empty">
          <span className="empty-icon"><Boxes /></span>
          <p className="empty-title">{filtered ? 'No matching transfers' : 'No transfers yet'}</p>
          <p className="empty-text">
            {filtered ? 'Try a different location or status.' : 'Requests you create will appear here.'}
          </p>
          {filtered && (
            <button type="button" className="btn btn-tinted" onClick={() => { setBranchId(null); setStatus(null) }}>
              Clear Filters
            </button>
          )}
        </div>
      ) : (
        <ul className="transfer-list" aria-busy={loading}>
          {transfers.map((t) => (
            <TransferRow
              key={t.id}
              transfer={t}
              busyAction={busy[t.id]}
              highlighted={recentlyUpdated === t.id}
              onAction={(action) => (action === 'cancel' ? setConfirmCancel(t) : run(t, action))}
            />
          ))}
        </ul>
      )}

      <Sheet
        open={confirmCancel !== null}
        title={`Cancel transfer #${confirmCancel?.id ?? ''}?`}
        message={confirmCancel && `${confirmCancel.fromBranch.name} → ${confirmCancel.toBranch.name}. No stock has moved yet, and this can't be undone.`}
        confirmLabel="Cancel Transfer"
        cancelLabel="Keep"
        tone="destructive"
        onClose={() => setConfirmCancel(null)}
        onConfirm={() => {
          if (confirmCancel) void run(confirmCancel, 'cancel')
          setConfirmCancel(null)
        }}
      />
    </section>
  )
}

type RowProps = {
  transfer: Transfer
  busyAction: TransferAction | undefined
  highlighted: boolean
  onAction: (action: TransferAction) => void
}

function TransferRow({ transfer: t, busyAction, highlighted, onAction }: RowProps) {
  const actions = ACTION_ORDER.filter((a) => t.allowedActions.includes(a))

  return (
    <li className={`transfer${highlighted ? ' is-updated' : ''}`}>
      <div className="transfer-top">
        <span className="transfer-id">#{t.id}</span>
        <span className="transfer-route">
          {t.fromBranch.name} <ArrowRight /> {t.toBranch.name}
        </span>
        <StatusBadge status={t.status} />
      </div>

      <div className="chips" aria-label="Products">
        {t.lines.map((l) => {
          const short = l.quantityReceived !== null && l.quantityReceived < l.quantity ? l.quantity - l.quantityReceived : 0
          return (
            <span key={l.productId} className="chip" title={l.sku}>
              {l.productName} <span className="chip-qty">× {l.quantity}</span>
              {short > 0 && <span className="chip-short">· {short} short</span>}
            </span>
          )
        })}
      </div>

      <div className="transfer-bottom">
        <ul className="timeline">
          <li>Requested <strong>{formatDate(t.createdAt)}</strong></li>
          {t.dispatchedAt && <li>Dispatched <strong>{formatDate(t.dispatchedAt)}</strong></li>}
          {t.receivedAt && <li>Received <strong>{formatDate(t.receivedAt)}</strong></li>}
          {t.cancelledAt && <li>Cancelled <strong>{formatDate(t.cancelledAt)}</strong></li>}
        </ul>

        {actions.length > 0 && (
          <div className="actions">
            {actions.map((action) => {
              const label = ACTION_LABELS[action]
              const isBusy = busyAction === action
              return (
                <button
                  key={action}
                  type="button"
                  className={`btn ${label.className}`}
                  disabled={busyAction !== undefined}
                  aria-busy={isBusy}
                  onClick={() => onAction(action)}
                >
                  {isBusy && <span className="spinner" aria-hidden="true" />}
                  {isBusy ? label.busy : label.idle}
                </button>
              )
            })}
          </div>
        )}
      </div>
    </li>
  )
}
