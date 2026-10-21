import { useCallback, useEffect, useState } from 'react'
import { ApiError, api, toApiError } from './api'
import { NewTransferForm } from './components/NewTransferForm'
import { StockPanel } from './components/StockPanel'
import { TransferList } from './components/TransferList'
import { TransferGlyph } from './components/icons'
import { ErrorBanner, Toast } from './components/ui'
import type { Branch, Product, Transfer, TransferAction } from './types'

const ACTION_MESSAGES: Record<TransferAction, (t: Transfer) => string> = {
  dispatch: (t) => `Transfer #${t.id} dispatched from ${t.fromBranch.name}`,
  receive: (t) => `Transfer #${t.id} received at ${t.toBranch.name}`,
  cancel: (t) => `Transfer #${t.id} cancelled`,
}

export default function App() {
  const [reference, setReference] = useState<{ branches: Branch[]; products: Product[] } | null>(null)
  const [loadError, setLoadError] = useState<ApiError | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [version, setVersion] = useState(0)
  const [toast, setToast] = useState<{ id: number; message: string } | null>(null)

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
  const notify = useCallback((message: string) => {
    setVersion((v) => v + 1)
    setToast({ id: Date.now(), message })
  }, [])

  return (
    <>
      <header className="app-header">
        <div className="app-header-inner">
          <span className="app-icon"><TransferGlyph /></span>
          <div>
            <h1 className="app-title">Stock Transfers</h1>
            <p className="app-subtitle">Mihira Stores · Warehouse, Colombo, Kandy and Galle</p>
          </div>
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
              onCreated={(t) => notify(`Transfer #${t.id} requested: ${t.fromBranch.name} → ${t.toBranch.name}`)}
            />
            <StockPanel branches={reference.branches} version={version} />
          </div>
          <div className="column">
            <TransferList
              branches={reference.branches}
              version={version}
              onChanged={(t, action) => notify(ACTION_MESSAGES[action](t))}
            />
          </div>
        </main>
      )}

      {toast && <Toast key={toast.id} message={toast.message} />}
    </>
  )
}
