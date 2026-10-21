import { useEffect, useRef, useState } from 'react'
import { ApiError, api, toApiError } from '../api'
import type { Branch, StockItem } from '../types'
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
          <p className="card-caption">On hand now, with open requests and stock on its way in.</p>
        </div>
      </div>

      <Segmented
        full
        label="Location"
        value={branchId}
        onChange={setBranchId}
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
            {items.map((item) => (
              <tr key={item.productId}>
                <td>
                  {item.name}
                  <span className="stock-sku">{item.sku}</span>
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
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
