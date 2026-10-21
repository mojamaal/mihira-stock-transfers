import { useEffect, useRef, useState } from 'react'
import { ApiError, api, toApiError } from '../api'
import {
  TRANSFER_STATUSES,
  type Branch,
  type Transfer,
  type TransferAction,
  type TransferDirection,
  type ShortfallReason,
  type TransferPage,
  type TransferStatus,
} from '../types'
import { ArrowRight, Boxes, ChevronLeft, ChevronRight, Refresh } from './icons'
import { ErrorBanner, Segmented, Sheet, StatusBadge, Stepper } from './ui'

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
  /** A transfer someone else just changed, briefly highlighted. */
  highlightId: number | null
  /** `replayed` is true when the action had already been done (e.g. by someone else) and nothing changed. */
  onChanged: (transfer: Transfer, action: TransferAction, replayed: boolean) => void
}

const PAGE_SIZE = 10

export function TransferList({ branches, version, highlightId, onChanged }: Props) {
  const [branchId, setBranchId] = useState<number | null>(null)
  const [direction, setDirection] = useState<TransferDirection>('Any')
  const [status, setStatus] = useState<TransferStatus | null>(null)
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<TransferPage | null>(null)
  const [settledKey, setSettledKey] = useState<string | null>(null)
  const [loadError, setLoadError] = useState<ApiError | null>(null)
  const [actionError, setActionError] = useState<ApiError | null>(null)
  const [busy, setBusy] = useState<Record<number, TransferAction>>({})
  const [confirmCancel, setConfirmCancel] = useState<Transfer | null>(null)
  const [receiving, setReceiving] = useState<Transfer | null>(null)
  const [reloadTick, setReloadTick] = useState(0)
  const [recentlyUpdated, setRecentlyUpdated] = useState<number | null>(null)

  // Synchronous guard against double-clicks landing before React re-renders the disabled button.
  // (The server is idempotent anyway; this just avoids pointless duplicate requests.)
  const inFlight = useRef(new Set<number>())

  // Loading is derived: the list is loading until the request for the current filters/version has settled.
  const requestKey = `${branchId}|${direction}|${status}|${page}|${version}|${reloadTick}`
  const loading = settledKey !== requestKey
  const transfers = result?.items ?? null

  useEffect(() => {
    let ignore = false
    api.transfers({ branchId, direction, status, page, pageSize: PAGE_SIZE })
      .then((data) => {
        if (ignore) return
        // If the current page emptied (e.g. its last transfer moved to another status), step back to a page that exists.
        if (data.items.length === 0 && page > data.totalPages) {
          setPage(data.totalPages)
          return
        }
        setResult(data)
        setLoadError(null)
      })
      .catch((err) => { if (!ignore) setLoadError(toApiError(err)) })
      .finally(() => { if (!ignore) setSettledKey(requestKey) })
    return () => { ignore = true }
  }, [branchId, direction, status, page, version, reloadTick, requestKey])

  // Any filter change starts again from the first page.
  const filterBy = {
    branch: (id: number | null) => { setBranchId(id); setDirection('Any'); setPage(1) },
    direction: (d: TransferDirection) => { setDirection(d); setPage(1) },
    status: (s: TransferStatus | null) => { setStatus(s); setPage(1) },
  }

  const openReceive = (transfer: Transfer) => setReceiving(transfer)

  async function run(transfer: Transfer, action: TransferAction, body?: unknown) {
    if (inFlight.current.has(transfer.id)) return
    inFlight.current.add(transfer.id)
    setBusy((b) => ({ ...b, [transfer.id]: action }))
    setActionError(null)

    try {
      const { transfer: updated, replayed } = await api.act(transfer.id, action, body)
      setResult((r) => r && { ...r, items: r.items.map((t) => (t.id === updated.id ? updated : t)) })
      setRecentlyUpdated(updated.id)
      onChanged(updated, action, replayed)
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
  const counts = result?.statusCounts
  const allCount = counts ? Object.values(counts).reduce((a, b) => a + b, 0) : null
  const withCount = (label: string, n: number | undefined | null) => (n == null ? label : `${label} ${n}`)
  const firstShown = result ? (result.page - 1) * result.pageSize + 1 : 0
  const lastShown = result ? firstShown + result.items.length - 1 : 0

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
          onChange={(e) => filterBy.branch(e.target.value === '' ? null : Number(e.target.value))}
        >
          <option value="">All locations</option>
          {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
        </select>
        {branchId !== null && (
          <Segmented
            label="Direction"
            value={direction}
            onChange={filterBy.direction}
            options={[
              { value: 'Any', label: 'In & out' },
              { value: 'Outgoing', label: 'Outgoing' },
              { value: 'Incoming', label: 'Incoming' },
            ]}
          />
        )}
        <Segmented
          label="Filter by status"
          value={status}
          onChange={filterBy.status}
          options={[
            { value: null, label: withCount('All', allCount) },
            ...TRANSFER_STATUSES.map((s) => ({ value: s, label: withCount(s === 'Dispatched' ? 'In transit' : s, counts?.[s]) })),
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
            <button type="button" className="btn btn-tinted" onClick={() => { filterBy.branch(null); filterBy.status(null) }}>
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
              highlighted={recentlyUpdated === t.id || highlightId === t.id}
              onAction={(action) => {
                if (action === 'cancel') setConfirmCancel(t)
                else if (action === 'receive') openReceive(t)
                else void run(t, action)
              }}
            />
          ))}
        </ul>
      )}

      {result && result.totalCount > 0 && (
        <nav className="pager" aria-label="Transfer pages">
          <span className="pager-summary">
            {firstShown}–{lastShown} of {result.totalCount}
          </span>
          <div className="pager-buttons">
            <button type="button" className="icon-button small" onClick={() => setPage((p) => p - 1)} disabled={result.page <= 1} aria-label="Previous page">
              <ChevronLeft />
            </button>
            <span className="pager-page">Page {result.page} of {result.totalPages}</span>
            <button type="button" className="icon-button small" onClick={() => setPage((p) => p + 1)} disabled={result.page >= result.totalPages} aria-label="Next page">
              <ChevronRight />
            </button>
          </div>
        </nav>
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

      <ReceiveSheet
        key={receiving?.id ?? 'none'}
        transfer={receiving}
        onClose={() => setReceiving(null)}
        onConfirm={(body) => {
          if (receiving) void run(receiving, 'receive', body)
          setReceiving(null)
        }}
      />
    </section>
  )
}

export type ReceiveBody = {
  lines: { productId: number; quantityReceived: number; reason: ShortfallReason | null }[]
  note: string | null
}

const REASONS: { value: ShortfallReason; label: string }[] = [
  { value: 'Damaged', label: 'Damaged' },
  { value: 'Missing', label: 'Missing' },
  { value: 'Other', label: 'Other' },
]

/**
 * Confirms what actually arrived. Prefilled with everything sent, so a full receipt is one tap. Lowering a
 * figure records a shortfall, which must say why (and "Other" must be explained in the note).
 */
function ReceiveSheet({ transfer, onClose, onConfirm }: {
  transfer: Transfer | null
  onClose: () => void
  onConfirm: (body: ReceiveBody) => void
}) {
  const lines = transfer?.lines ?? []
  const [quantities, setQuantities] = useState<Record<number, string>>(() =>
    Object.fromEntries(lines.map((l) => [l.productId, String(l.quantity)])))
  const [reasons, setReasons] = useState<Record<number, ShortfallReason | null>>({})
  const [note, setNote] = useState('')

  const parsed = lines.map((l) => {
    const text = quantities[l.productId] ?? ''
    const value = /^\d+$/.test(text.trim()) ? Number(text) : null
    const short = value !== null && value < l.quantity ? l.quantity - value : 0
    const reason = short > 0 ? reasons[l.productId] ?? null : null
    return { line: l, value, short, reason, valid: value !== null && value <= l.quantity && (short === 0 || reason !== null) }
  })
  const needsNote = parsed.some((p) => p.reason === 'Other')
  const noteMissing = needsNote && note.trim() === ''
  const canConfirm = parsed.every((p) => p.valid) && !noteMissing

  return (
    <Sheet
      open={transfer !== null}
      title={`Receive transfer #${transfer?.id ?? ''}`}
      message={transfer && `Confirm what arrived at ${transfer.toBranch.name}. Lower a quantity if goods were damaged or missing.`}
      confirmLabel="Receive"
      cancelLabel="Not now"
      confirmDisabled={!canConfirm}
      onClose={onClose}
      onConfirm={() => onConfirm({
        lines: parsed.map((p) => ({ productId: p.line.productId, quantityReceived: p.value ?? 0, reason: p.reason })),
        note: note.trim() === '' ? null : note.trim(),
      })}
    >
      <ul className="receive-lines">
        {parsed.map(({ line, value, short, reason }) => {
          const outOfRange = value === null || value > line.quantity
          return (
            <li key={line.productId} className="receive-line">
              <label htmlFor={`recv-${line.productId}`}>
                {line.productName}
                <span className="receive-sent">Sent {line.quantity}</span>
              </label>
              <Stepper
                id={`recv-${line.productId}`}
                label={`Received quantity of ${line.productName}`}
                value={quantities[line.productId] ?? ''}
                onChange={(v) => setQuantities((q) => ({ ...q, [line.productId]: v }))}
                min={0}
                max={line.quantity}
                invalid={outOfRange}
              />
              {outOfRange && <span className="receive-note error">Enter 0 to {line.quantity}.</span>}
              {short > 0 && (
                <div className="receive-reason">
                  <span className="receive-note">{short} short. Why?</span>
                  <Segmented
                    label={`Why ${line.productName} is short`}
                    value={reason}
                    onChange={(r) => setReasons((all) => ({ ...all, [line.productId]: r }))}
                    options={REASONS}
                  />
                </div>
              )}
            </li>
          )
        })}
      </ul>

      <label className="field receive-note-field">
        <span className="field-label">Note {needsNote ? '(required for "Other")' : '(optional)'}</span>
        <textarea
          className={`input textarea${noteMissing ? ' is-invalid' : ''}`}
          rows={2}
          maxLength={500}
          value={note}
          placeholder="e.g. Two bags split in transit"
          onChange={(e) => setNote(e.target.value)}
        />
      </label>
    </Sheet>
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
              {short > 0 && <span className="chip-short">· {short} short ({l.shortfallReason?.toLowerCase()})</span>}
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
          {t.receiptNote && <li className="timeline-note">“{t.receiptNote}”</li>}
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
