// Mirrors the API's JSON contract (see src/MihiraStores.Api/Transfers/TransferDtos.cs).

export type Branch = { id: number; name: string; isWarehouse: boolean }

export type Product = { id: number; sku: string; name: string }

export type StockItem = { productId: number; sku: string; name: string; quantity: number }

export type TransferStatus = 'Requested' | 'Dispatched' | 'Received' | 'Cancelled'

export const TRANSFER_STATUSES: TransferStatus[] = ['Requested', 'Dispatched', 'Received', 'Cancelled']

export type TransferAction = 'dispatch' | 'receive' | 'cancel'

export type TransferLine = {
  productId: number
  sku: string
  productName: string
  quantity: number
  quantityReceived: number | null
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
  lines: TransferLine[]
  /** Decided by the server's state machine; the UI never works this out itself. */
  allowedActions: TransferAction[]
}

export type CreateTransferRequest = {
  fromBranchId: number | null
  toBranchId: number | null
  lines: { productId: number | null; quantity: number | null }[]
}
