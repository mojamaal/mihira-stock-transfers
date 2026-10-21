import { useEffect, useState, type FormEvent } from 'react'
import { ApiError, api, newIdempotencyKey, toApiError } from '../api'
import type { Branch, Product, Transfer } from '../types'
import { Close, Plus, Swap } from './icons'
import { ErrorBanner, Stepper } from './ui'

type LineDraft = { key: number; productId: number | null; quantity: string }

let nextLineKey = 1
const emptyLine = (): LineDraft => ({ key: nextLineKey++, productId: null, quantity: '1' })

const MAX_QUANTITY = 1_000_000

function parseQuantity(text: string): number | null {
  if (!/^\d+$/.test(text.trim())) return null
  const n = Number(text)
  return n >= 1 && n <= MAX_QUANTITY ? n : null
}

type Props = {
  branches: Branch[]
  products: Product[]
  /** Bumped by the parent after any change, so the available-stock hints stay current. */
  version: number
  /** `replayed` is true when the server recognised a repeat of an earlier submission and created nothing new. */
  onCreated: (transfer: Transfer, replayed: boolean) => void
}

export function NewTransferForm({ branches, products, version, onCreated }: Props) {
  const [fromId, setFromId] = useState<number | null>(null)
  const [toId, setToId] = useState<number | null>(null)
  const [lines, setLines] = useState<LineDraft[]>(() => [emptyLine()])
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [stock, setStock] = useState<{ branchId: number; byProduct: Map<number, number> } | null>(null)

  // One key per distinct form content: retrying the same request (double-click, lost response) reuses it,
  // so the server returns the original transfer; any edit makes it a new request with a new key.
  const [idempotencyKey, setIdempotencyKey] = useState(newIdempotencyKey)
  const edited = () => {
    setIdempotencyKey(newIdempotencyKey())
    setError(null)
  }

  useEffect(() => {
    if (fromId === null) return
    let ignore = false
    api.stock(fromId)
      .then((items) => {
        if (!ignore) setStock({ branchId: fromId, byProduct: new Map(items.map((i) => [i.productId, i.quantity])) })
      })
      .catch(() => {
        // The hint is only a guide; the server checks stock at dispatch, so a failed lookup just hides it.
        if (!ignore) setStock(null)
      })
    return () => { ignore = true }
  }, [fromId, version])

  const available = fromId !== null && stock?.branchId === fromId ? stock.byProduct : null
  const fromName = branches.find((b) => b.id === fromId)?.name
  const sameBranch = fromId !== null && fromId === toId
  const usedProducts = new Set(lines.map((l) => l.productId).filter((id): id is number => id !== null))
  const linesValid = lines.every((l) => l.productId !== null && parseQuantity(l.quantity) !== null)
  const canSubmit = fromId !== null && toId !== null && !sameBranch && lines.length > 0 && linesValid && !submitting

  const fieldError = (field: string) => error?.fieldErrors[field]?.[0]

  function updateLine(key: number, patch: Partial<LineDraft>) {
    setLines((current) => current.map((l) => (l.key === key ? { ...l, ...patch } : l)))
    edited()
  }

  function removeLine(key: number) {
    setLines((current) => (current.length === 1 ? [emptyLine()] : current.filter((l) => l.key !== key)))
    edited()
  }

  function addLine() {
    setLines((current) => [...current, emptyLine()])
    edited()
  }

  function swap() {
    setFromId(toId)
    setToId(fromId)
    edited()
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!canSubmit) return

    setSubmitting(true)
    setError(null)
    try {
      const { transfer, replayed } = await api.createTransfer(
        {
          fromBranchId: fromId,
          toBranchId: toId,
          lines: lines.map((l) => ({ productId: l.productId, quantity: parseQuantity(l.quantity) })),
        },
        idempotencyKey,
      )
      setLines([emptyLine()])
      setIdempotencyKey(newIdempotencyKey())
      onCreated(transfer, replayed)
    } catch (err) {
      // Keep the form and the same key, so retrying cannot create a duplicate.
      const apiError = toApiError(err)
      setError(apiError.status === 400
        ? new ApiError(400, 'Check the request', apiError.message, apiError.fieldErrors)
        : apiError)
    } finally {
      setSubmitting(false)
    }
  }

  const warehouse = branches.filter((b) => b.isWarehouse)
  const stores = branches.filter((b) => !b.isWarehouse)
  const branchOptions = (
    <>
      <option value="" disabled>Chooseâ€¦</option>
      {warehouse.length > 0 && (
        <optgroup label="Warehouse">
          {warehouse.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
        </optgroup>
      )}
      <optgroup label="Branches">
        {stores.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
      </optgroup>
    </>
  )

  return (
    <section className="card" aria-labelledby="new-transfer-title">
      <div className="card-header">
        <div>
          <h2 id="new-transfer-title" className="card-title">New Transfer</h2>
          <p className="card-caption">Request stock to move between locations.</p>
        </div>
      </div>

      {error && <ErrorBanner error={error} onDismiss={() => setError(null)} />}

      <form onSubmit={submit} noValidate>
        <div className="route">
          <div className="field">
            <label className="field-label" htmlFor="from-branch">From</label>
            <select
              id="from-branch"
              className={`select${fieldError('fromBranchId') ? ' is-invalid' : ''}`}
              value={fromId ?? ''}
              onChange={(e) => { setFromId(Number(e.target.value)); edited() }}
            >
              {branchOptions}
            </select>
          </div>

          <button
            type="button"
            className="icon-button"
            onClick={swap}
            disabled={fromId === null && toId === null}
            aria-label="Swap from and to"
            title="Swap"
          >
            <Swap />
          </button>

          <div className="field">
            <label className="field-label" htmlFor="to-branch">To</label>
            <select
              id="to-branch"
              className={`select${sameBranch || fieldError('toBranchId') ? ' is-invalid' : ''}`}
              value={toId ?? ''}
              onChange={(e) => { setToId(Number(e.target.value)); edited() }}
              aria-describedby={sameBranch ? 'route-error' : undefined}
            >
              {branchOptions}
            </select>
          </div>
        </div>
        {(sameBranch || fieldError('toBranchId')) && (
          <p id="route-error" className="field-message" style={{ marginTop: 8 }}>
            {sameBranch ? 'From and to must be different locations.' : fieldError('toBranchId')}
          </p>
        )}

        <h3 className="section-label">Products</h3>
        <ul className="lines">
          {lines.map((line, index) => {
            const quantity = parseQuantity(line.quantity)
            const qtyInvalid = line.quantity.trim() !== '' && quantity === null
            const onHand = line.productId !== null ? available?.get(line.productId) : undefined
            const over = onHand !== undefined && quantity !== null && quantity > onHand
            const serverQtyError = fieldError(`lines[${index}].quantity`)
            const serverProductError = fieldError(`lines[${index}].productId`)

            return (
              <li key={line.key} className="line">
                <div className="line-row">
                  <select
                    className={`select${serverProductError ? ' is-invalid' : ''}`}
                    aria-label={`Product ${index + 1}`}
                    value={line.productId ?? ''}
                    onChange={(e) => updateLine(line.key, { productId: Number(e.target.value) })}
                  >
                    <option value="" disabled>Choose a productâ€¦</option>
                    {products.map((p) => (
                      <option key={p.id} value={p.id} disabled={p.id !== line.productId && usedProducts.has(p.id)}>
                        {p.name}
                      </option>
                    ))}
                  </select>
                  <Stepper
                    id={`qty-${line.key}`}
                    label={`Quantity for product ${index + 1}`}
                    value={line.quantity}
                    onChange={(value) => updateLine(line.key, { quantity: value })}
                    max={MAX_QUANTITY}
                    invalid={qtyInvalid || !!serverQtyError}
                  />
                  <button
                    type="button"
                    className="icon-button small danger"
                    onClick={() => removeLine(line.key)}
                    aria-label={`Remove product ${index + 1}`}
                    title="Remove"
                  >
                    <Close />
                  </button>
                </div>

                {(qtyInvalid || serverQtyError) && (
                  <p className="field-message">{serverQtyError ?? 'Enter a whole number above zero.'}</p>
                )}
                {serverProductError && <p className="field-message">{serverProductError}</p>}

                {onHand !== undefined && (
                  <p className={`availability${over ? ' warn' : ''}`}>
                    <span>Available at {fromName}: <strong>{onHand}</strong></span>
                    {over && <span>More than is on hand now. Dispatch will be refused unless stock arrives first.</span>}
                  </p>
                )}
              </li>
            )
          })}
        </ul>
        {fieldError('lines') && <p className="field-message" style={{ marginTop: 8 }}>{fieldError('lines')}</p>}

        <button type="button" className="add-line" onClick={addLine} disabled={lines.length >= products.length}>
          <Plus /> Add product
        </button>

        <button type="submit" className="btn btn-filled btn-large" disabled={!canSubmit} aria-busy={submitting}>
          {submitting ? <><span className="spinner" aria-hidden="true" /> Requestingâ€¦</> : 'Request Transfer'}
        </button>
      </form>
    </section>
  )
}
