import { useEffect, useRef, type ReactNode } from 'react'
import type { ApiError } from '../api'
import type { TransferStatus } from '../types'
import { Check, Close, Info, Minus, Plus, Warning } from './icons'

/** A dismissible error message. Shows the server's explanation, plus any field-level problems. */
export function ErrorBanner({ error, onDismiss }: { error: ApiError; onDismiss: () => void }) {
  const fieldMessages = [...new Set(Object.values(error.fieldErrors).flat())]
  const showList = fieldMessages.length > 1

  return (
    <div className="banner" role="alert">
      <span className="banner-icon"><Warning /></span>
      <div className="banner-body">
        <p className="banner-title">{error.title}</p>
        {showList ? (
          <ul className="banner-list">
            {fieldMessages.map((m) => <li key={m}>{m}</li>)}
          </ul>
        ) : (
          <p className="banner-detail">{error.message}</p>
        )}
      </div>
      <button type="button" className="icon-button small" onClick={onDismiss} aria-label="Dismiss error">
        <Close />
      </button>
    </div>
  )
}

export type ToastTone = 'success' | 'info'

export function Toast({ message, tone = 'success' }: { message: string; tone?: ToastTone }) {
  return (
    <div className={`toast toast-${tone}`} role="status" aria-live="polite">
      <span className="toast-icon">{tone === 'success' ? <Check /> : <Info />}</span>
      {message}
    </div>
  )
}

export function StatusBadge({ status }: { status: TransferStatus }) {
  const label = status === 'Dispatched' ? 'In transit' : status
  return <span className={`badge badge-${status}`} title={status}>{label}</span>
}

type SegmentedProps<T> = {
  label: string
  value: T
  options: { value: T; label: string }[]
  onChange: (value: T) => void
  full?: boolean
}

export function Segmented<T extends string | number | null>({ label, value, options, onChange, full }: SegmentedProps<T>) {
  return (
    <div className={`segmented${full ? ' full' : ''}`} role="group" aria-label={label}>
      {options.map((o) => (
        <button key={String(o.value)} type="button" aria-pressed={o.value === value} onClick={() => onChange(o.value)}>
          {o.label}
        </button>
      ))}
    </div>
  )
}

type StepperProps = {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  min?: number
  max?: number
  invalid?: boolean
}

/** A − / number / + control. The raw text is kept so the user can type freely; validity is decided by the caller. */
export function Stepper({ id, label, value, onChange, min = 1, max = 1_000_000, invalid }: StepperProps) {
  const n = Number.parseInt(value, 10)
  const current = Number.isNaN(n) ? min : n
  const set = (next: number) => onChange(String(Math.min(max, Math.max(min, next))))

  return (
    <div className={`stepper${invalid ? ' is-invalid' : ''}`}>
      <button type="button" onClick={() => set(current - 1)} disabled={current <= min} aria-label={`Decrease ${label}`} tabIndex={-1}>
        <Minus />
      </button>
      <input
        id={id}
        type="number"
        inputMode="numeric"
        min={min}
        max={max}
        step={1}
        value={value}
        aria-label={label}
        aria-invalid={invalid || undefined}
        onChange={(e) => onChange(e.target.value)}
        onFocus={(e) => e.target.select()}
      />
      <button type="button" onClick={() => set(current + 1)} disabled={current >= max} aria-label={`Increase ${label}`} tabIndex={-1}>
        <Plus />
      </button>
    </div>
  )
}

type SheetProps = {
  open: boolean
  title: string
  message?: ReactNode
  children?: ReactNode
  confirmLabel: string
  cancelLabel?: string
  tone?: 'default' | 'destructive'
  confirmDisabled?: boolean
  onConfirm: () => void
  onClose: () => void
}

/** A modal confirmation sheet built on the native <dialog> element (focus trap, Esc to close, backdrop). */
export function Sheet({
  open, title, message, children, confirmLabel, cancelLabel = 'Not now', tone = 'default', confirmDisabled, onConfirm, onClose,
}: SheetProps) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog
      ref={ref}
      className="sheet"
      aria-labelledby="sheet-title"
      onClose={onClose}
      onClick={(e) => { if (e.target === ref.current) onClose() }}
    >
      {open && (
        <form method="dialog" onSubmit={(e) => { e.preventDefault(); onConfirm() }}>
          <div className="sheet-body">
            <h2 id="sheet-title" className="sheet-title">{title}</h2>
            {message && <p className="sheet-message">{message}</p>}
            {children}
          </div>
          <div className="sheet-actions">
            <button type="button" className="btn btn-plain" onClick={onClose}>{cancelLabel}</button>
            <button
              type="submit"
              className={`btn ${tone === 'destructive' ? 'btn-destructive-filled' : 'btn-filled'}`}
              disabled={confirmDisabled}
              autoFocus
            >
              {confirmLabel}
            </button>
          </div>
        </form>
      )}
    </dialog>
  )
}
