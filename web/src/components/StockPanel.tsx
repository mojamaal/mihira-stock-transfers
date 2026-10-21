import { Fragment, useEffect, useRef, useState } from 'react'
import { ApiError, api, toApiError } from '../api'
import type { Branch, StockItem, StockMovement } from '../types'
import { ChevronRight } from './icons'
import { ErrorBanner, Segmented } from './ui'

const LOW_STOCK = 5

type Props = {
  branches: Branch[]
  /** Bumped by the parent after any change, so figures refresh and changed ones briefly highlight. */
  version: number
}

export function StockPanel({ branches, version }: Props) {
  const [branchId, setBranchId] = useState(() => branches.find((b) => !b.isWarehouse)?.id ?? branches[0].id)
  const [data, setData] = useState<{ branchId: number; items: StockItem[] } | null>(null)
  const [changed, setChanged] = useState<Set<number>>(new Set())
  const [expanded, setExpanded] = useState<number | null>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const previous = useRef<{ branchId: number; byProduct: Map<number, number> } | null>(null)

  useEffect(() => {
    let ignore = false
    api.stock(branchId)
      .then((items) => {
        if (ignore) return
        const before = previous.current?.branchId === branchId ? previous.current.byProduct : null
        setChanged(new Set(items.filter((i) => before?.has(i.productId) && before.get(i.productId) !== i.onHand).map((i) => i.productId)))
        previous.current = { branchId, byProduct: new Map(items.map((i) => [i.productId, i.onHand])) }
        setData({ branchId, items })
        setError(null)
      })
      .catch((err) => { if (!ignore) setError(toApiError(err)) })
    return () => { ignore = true }
  }, [branchId, version])

  const items = data?.branchId === branchId ? data.items : null

  return (
    <section className="card" aria-labelledby="stock-title">
      <div className="card-header" style={{ marginBottom: 12 }}>
        <div>
          <h2 id="stock-title" className="card-title">Stock on Hand</h2>
          <p className="card-caption">Tap a product to see every movement behind its figure.</p>
        </div>
      </div>

      <Segmented
        full
        label="Location"
        value={branchId}
        onChange={(id) => { setBranchId(id); setExpanded(null) }}
        options={branches.map((b) => ({ value: b.id, label: b.isWarehouse ? 'Warehouse' : b.name }))}
      />

      {error && <div style={{ marginTop: 14 }}><ErrorBanner error={error} onDismiss={() => setError(null)} /></div>}

      {items === null ? (
        !error && <div className="loading"><span className="spinner large" aria-label="Loading stock" /></div>
      ) : (
        <table className="stock-table">
          <thead>
            <tr><th scope="col">Product</th><th scope="col">On hand</th></tr>
          </thead>
          <tbody>
            {items.map((item) => {
              const isOpen = expanded === item.productId
              return (
                <Fragment key={item.productId}>
                  <tr className={`stock-row${isOpen ? ' is-open' : ''}`}>
                    <td>
                      <button
                        type="button"
                        className="stock-toggle"
                        aria-expanded={isOpen}
                        aria-controls={`history-${item.productId}`}
                        onClick={() => setExpanded(isOpen ? null : item.productId)}
                      >
                        <span className="stock-chevron"><ChevronRight size={12} /></span>
                        <span>
                          {item.name}
                          <span className="stock-sku">{item.sku}</span>
                        </span>
                      </button>
                    </td>
                    <td>
                      {item.onHand === 0 && <span className="stock-tag out">Out of stock</span>}
                      {item.onHand > 0 && item.onHand < LOW_STOCK && <span className="stock-tag low">Low</span>}
                      <span key={`${item.productId}-${item.onHand}`} className={`stock-qty${changed.has(item.productId) ? ' is-changed' : ''}`}>
                        {item.onHand}
                      </span>
                      {(item.requestedOut > 0 || item.incoming > 0) && (
                        <span className="stock-flow">
                          {item.requestedOut > 0 && <span title="Asked for by this branch's open requests (not reserved)">{item.requestedOut} requested</span>}
                          {item.incoming > 0 && <span title="Dispatched to this branch, in transit">+{item.incoming} incoming</span>}
                        </span>
                      )}
                    </td>
                  </tr>
                  {isOpen && (
                    <tr className="stock-history-row">
                      <td colSpan={2} id={`history-${item.productId}`}>
                        <MovementHistory branchId={branchId} productId={item.productId} version={version} />
                      </td>
                    </tr>
                  )}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      )}
    </section>
  )
}

const timeFormat = new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' })

function describe(m: StockMovement): string {
  switch (m.kind) {
    case 'Opening': return 'Opening stock'
    case 'TransferOut': return `Sent to ${m.counterparty} · #${m.transferId}`
    case 'TransferIn': return `Received from ${m.counterparty} · #${m.transferId}`
  }
}

/** The ledger entries behind one figure, newest first, with the balance after each. */
function MovementHistory({ branchId, productId, version }: { branchId: number; productId: number; version: number }) {
  const [movements, setMovements] = useState<StockMovement[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let ignore = false
    api.movements(branchId, productId)
      .then((data) => { if (!ignore) { setMovements(data); setError(null) } })
      .catch((err) => { if (!ignore) setError(toApiError(err).message) })
    return () => { ignore = true }
  }, [branchId, productId, version])

  if (error) return <p className="history-empty">{error}</p>
  if (movements === null) return <div className="history-loading"><span className="spinner" aria-label="Loading history" /></div>
  if (movements.length === 0) return <p className="history-empty">No movements recorded yet.</p>

  return (
    <ol className="history" aria-label="Recent stock movements">
      {movements.map((m) => (
        <li key={m.id} className="history-item">
          <span className="history-what">
            {describe(m)}
            <span className="history-when">{timeFormat.format(new Date(m.occurredAt))}</span>
          </span>
          <span className={`history-change ${m.change > 0 ? 'up' : 'down'}`}>{m.change > 0 ? `+${m.change}` : `−${-m.change}`}</span>
          <span className="history-balance" title="Balance after">{m.balanceAfter}</span>
        </li>
      ))}
    </ol>
  )
}
