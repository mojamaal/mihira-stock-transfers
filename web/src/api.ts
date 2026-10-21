import type {
  Branch,
  CreateTransferRequest,
  Product,
  StockItem,
  Transfer,
  TransferAction,
  TransferDirection,
  TransferPage,
  TransferStatus,
} from './types'

/** An error the user can read: built from the API's ProblemDetails, or from a network failure. */
export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, title: string, detail: string, fieldErrors: Record<string, string[]> = {}) {
    super(detail)
    this.status = status
    this.title = title
    this.fieldErrors = fieldErrors
  }
}

type ProblemDetails = { title?: string; detail?: string; errors?: Record<string, string[]> }

/** The server sets this header when a request repeated something that had already happened, so nothing changed. */
const REPLAYED_HEADER = 'Idempotent-Replayed'

export type CommandResult = { transfer: Transfer; replayed: boolean }

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  return (await send<T>(path, init)).data
}

async function command(path: string, init: RequestInit): Promise<CommandResult> {
  const { data, response } = await send<Transfer>(path, init)
  return { transfer: data, replayed: response.headers.get(REPLAYED_HEADER) === 'true' }
}

async function send<T>(path: string, init?: RequestInit): Promise<{ data: T; response: Response }> {
  let response: Response
  try {
    response = await fetch(`/api${path}`, {
      ...init,
      headers: { Accept: 'application/json', ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...init?.headers },
    })
  } catch {
    throw new ApiError(0, 'Connection problem', 'Could not reach the server. Is the API running?')
  }

  if (response.ok) {
    return { data: (await response.json()) as T, response }
  }

  let problem: ProblemDetails = {}
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    // Not JSON (for example the dev proxy could not reach the API); fall back to a generic message below.
  }

  if (response.status >= 500 && !problem.detail) {
    throw new ApiError(response.status, 'Server problem', 'The server could not complete the request. Is the API running?')
  }

  throw new ApiError(
    response.status,
    problem.title ?? 'Request failed',
    problem.detail ?? problem.title ?? `The request failed (HTTP ${response.status}).`,
    problem.errors ?? {},
  )
}

export const api = {
  branches: () => request<Branch[]>('/branches'),
  products: () => request<Product[]>('/products'),
  stock: (branchId: number) => request<StockItem[]>(`/branches/${branchId}/stock`),

  transfers: (filter: {
    branchId: number | null
    direction: TransferDirection
    status: TransferStatus | null
    page: number
    pageSize: number
  }) => {
    const query = new URLSearchParams({ page: String(filter.page), pageSize: String(filter.pageSize) })
    if (filter.branchId) {
      query.set('branchId', String(filter.branchId))
      query.set('direction', filter.direction)
    }
    if (filter.status) query.set('status', filter.status)
    return request<TransferPage>(`/transfers?${query}`)
  },

  /** The idempotency key makes a double-click or a retry return the same transfer instead of creating another. */
  createTransfer: (body: CreateTransferRequest, idempotencyKey: string) =>
    command('/transfers', {
      method: 'POST',
      body: JSON.stringify(body),
      headers: { 'Idempotency-Key': idempotencyKey },
    }),

  act: (transferId: number, action: TransferAction, body?: unknown) =>
    command(`/transfers/${transferId}/${action}`, {
      method: 'POST',
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error
  return new ApiError(0, 'Something went wrong', error instanceof Error ? error.message : 'Please try again.')
}

/** A v4 UUID. crypto.randomUUID only exists in secure contexts (https or localhost), so fall back when it is missing. */
export function newIdempotencyKey(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
