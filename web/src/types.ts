// Mirrors the API's JSON contract (see src/MihiraStores.Api/Transfers/TransferDtos.cs).

export type Branch = { id: number; name: string; isWarehouse: boolean }

export type Product = { id: number; sku: string; name: string }

export type StockItem = {
  productId: number
  sku: string
  name: string
  /** Physically at the branch now. */
  onHand: number
  /** Asked for by this branch's open outgoing requests. Not reserved: the first dispatch gets the stock. */
  requestedOut: number
  /** Dispatched to this branch and still in transit. */
  incoming: number
}

export type TransferStatus = 'Requested' | 'Dispatched' | 'Received' | 'Cancelled'

export const TRANSFER_STATUSES: TransferStatus[] = ['Requested', 'Dispatched', 'Received', 'Cancelled']

export type TransferAction = 'dispatch' | 'receive' | 'cancel'

export type ShortfallReason = 'Damaged' | 'Missing' | 'Other'

export type TransferLine = {
  productId: number
  sku: string
  productName: string
  quantity: number
  quantityReceived: number | null
  /** Why less arrived than was sent; set only when something is short. */
  shortfallReason: ShortfallReason | null
}

export type Transfer = {
  id: number
  fromBranch: { id: number; name: string }
  toBranch: { id: number; name: string }
  status: TransferStatus
  createdAt: string
  dispatchedAt: string | null
  receivedAt: string | null
  cancelledAt: string | null
  receiptNote: string | null
  lines: TransferLine[]
  /** Decided by the server's state machine; the UI never works this out itself. */
  allowedActions: TransferAction[]
}

/** Which side of a transfer a branch filter matches. */
export type TransferDirection = 'Any' | 'Outgoing' | 'Incoming'

export type TransferPage = {
  items: Transfer[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  /** Per-status totals under the branch/direction filter, ignoring the status filter. */
  statusCounts: Record<TransferStatus, number>
}

export type CreateTransferRequest = {
  fromBranchId: number | null
  toBranchId: number | null
  lines: { productId: number | null; quantity: number | null }[]
}
