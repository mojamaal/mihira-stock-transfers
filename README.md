# Mihira Stores: branch stock transfers

A system for moving stock between Mihira Stores' warehouse and its Colombo, Kandy and Galle branches.
A branch **requests** a transfer, the sender **dispatches** it (stock leaves the sender), and the receiver
**receives** it (stock arrives). A transfer can be **cancelled** until it is dispatched.

- **API:** ASP.NET Core (.NET 10) minimal APIs, EF Core 10, SQL Server
- **UI:** one React + TypeScript screen (Vite), with live updates
- **Tests:** 42 xUnit integration tests that run the real API against a real SQL Server (Testcontainers)

The design centres on the client's two worries: **stock must never go negative, and a double-click or a
network retry must never move stock twice**. It also targets the root problem they describe, "the stock
figures never match": every stock change is recorded in an append-only ledger that always reconciles.

---

## Run it

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download), Node.js 20.19+ (or 22.12+), and Docker (Docker Desktop on Windows/macOS).

```bash
# 1. Start SQL Server (local dev container, port 14333)
docker compose up -d --wait

# 2. Start the API on http://localhost:5080. It applies migrations and seeds data on startup.
dotnet run --project src/MihiraStores.Api

# 3. In a second terminal, start the UI
cd web
npm install
npm run dev
```

Open **<http://localhost:5173>**. To see live updates, open it in two windows: a change in one appears in the other.

**Using an existing SQL Server instead of Docker:** set the connection string before step 2, e.g. for a local SQL Express:

```powershell
$env:ConnectionStrings__Default = "Server=.\SQLEXPRESS;Database=MihiraStores;Trusted_Connection=True;TrustServerCertificate=True"
```

**Start again from the seed data:** stop the API, run `docker compose down -v`, then repeat the steps above.

> The SA password in `docker-compose.yml` / `appsettings.Development.json` is a throwaway **local development
> credential only**. Never reuse it anywhere.

### Using it

The screen has three areas: **New Transfer** and **Stock on Hand** on the left, and **Transfers** on the right.
A green **Live** badge in the header means changes made elsewhere appear automatically.

1. **Request:** in New Transfer, pick **From** and **To**, choose a product and quantity (**Add product** for more
   lines), and press **Request Transfer**. Each line shows what the sender has on hand, and warns if you ask for more.
2. **Dispatch:** in Transfers, press **Dispatch**. Stock leaves the sender, and the receiver's stock shows it as incoming.
3. **Receive:** press **Receive**. The quantities are prefilled with what was sent. Lower one if goods arrived
   damaged or missing, pick a reason, and confirm. Only what arrived is added.
4. **Cancel:** press **Cancel** on a transfer that hasn't been dispatched yet.
5. **Try the rules:** request 5 Coconut Oil from Galle (it has 3) and dispatch it to see the refusal. Double-click
   Dispatch, or press it in two windows at once, to see that stock moves only once.
6. **Look back:** filter Transfers by location, direction (in/out) and status. Tap a product in Stock on Hand
   to see every movement behind its figure.

### Run the tests

```bash
dotnet test
```

Docker must be running: the tests start their own SQL Server container. The first run downloads the SQL Server
image (~1.5 GB), so it can take a few minutes. After that the suite takes about 20 seconds.

Optional: `src/MihiraStores.Api/MihiraStores.Api.http` has sample requests (VS Code REST Client / Visual Studio / Rider).

**Where the brief's example tests are** (all in `tests/MihiraStores.Api.Tests/`, run against real SQL Server):

| Brief's example | Tests |
|---|---|
| Over-stock dispatch | `TransferLifecycleTests.Dispatch_that_would_make_stock_negative_is_rejected_and_nothing_moves`, `Two_transfers_competing_for_the_same_stock_cannot_both_be_dispatched` |
| Double dispatch | `TransferLifecycleTests.Parallel_dispatches_of_the_same_transfer_move_stock_exactly_once` (10 at once), `Dispatching_twice_is_a_no_op_the_second_time` |
| Receive | `TransferLifecycleTests.Dispatch_then_receive_moves_stock_from_sender_to_receiver_once`, `Parallel_receives_of_the_same_transfer_add_stock_exactly_once`, `Receive_before_dispatch_is_rejected` |
| Other rules | Cancel, status transitions, validation (same branch, zero quantity), idempotent create, partial receipt, ledger reconciliation, live events: 42 tests in total |

Last run: `Passed! - Failed: 0, Passed: 42, Skipped: 0, Total: 42`.

---

## How double counting and negative stock are prevented

The rules are enforced in the database, not by checking state in C# and then writing it.

1. **Each status change is claimed with one conditional `UPDATE`.** For example, dispatch runs
   `UPDATE Transfers SET Status='Dispatched' … WHERE Id=@id AND Status='Requested'` and looks at the affected
   row count. Ten simultaneous dispatches all reach the database, but only one can match `Status='Requested'`.
   The rest affect 0 rows and never touch stock.
2. **Stock is taken with a guarded `UPDATE`.** `… SET Quantity = Quantity - @qty WHERE … AND Quantity >= @qty`.
   If any line is short, the whole dispatch is rolled back: status, earlier lines and ledger rows. The 409
   response lists every short product with what is available and what is needed.
3. **Both run in one transaction.** READ COMMITTED is enough, because a conditional `UPDATE` locks the row it checks,
   so a competing request waits and then re-checks the committed value. Lines are processed in product order
   so concurrent transfers lock stock rows in the same order and cannot deadlock.
4. **The database refuses bad data even if the code has a bug.** CHECK constraints enforce `Quantity >= 0` on stock,
   from ≠ to, `Quantity > 0` on lines, `0 ≤ QuantityReceived ≤ Quantity`, "a shortfall reason exists exactly when
   something is short", and a non-negative balance in the ledger.
5. **Repeats are safe and say so.** Repeating an action that already happened returns **200** with the transfer
   unchanged, moves no stock, and sets the `Idempotent-Replayed: true` header. The UI then says "Transfer #12
   was already dispatched. No stock moved again" rather than claiming success. An action from the wrong status
   (receiving a Requested transfer) returns **409**.
6. **Creating is idempotent too.** The UI sends an `Idempotency-Key` header. A double-clicked or retried create
   returns the original transfer, even when the duplicates arrive at the same instant (a unique index decides).
   The key is bound to a hash of the request, so reusing it for a *different* request returns **422** instead of
   silently returning the wrong transfer.
7. **Transient SQL failures are retried.** Commands run under EF Core's retrying execution strategy. That is safe
   only because of point 5: if an attempt actually committed before the connection dropped, the retry finds
   nothing left to claim and returns the transfer as a replay.

The tests prove these against real SQL Server: 10 parallel dispatches move stock once (and exactly one
response is not a replay), 10 parallel receives add once, two transfers competing for the same stock can't
both be dispatched, 8 parallel creates with one key make one transfer, and a short line rolls back the lines
that had enough. The double-dispatch tests were also checked to **fail** when the status guard is removed.

## The stock ledger

Every stock change writes a `StockMovements` row in the same transaction: the branch, the product, the signed change,
the cause (opening stock, sent on transfer #n, received on transfer #n) and the exact balance after it. Rows
are never updated or deleted. A test runs concurrent, refused, cancelled and partial operations and then checks
that for every branch and product the ledger sums to the stock on hand and its last balance matches. In the UI,
tap a product in **Stock on Hand** to see the movements behind its figure.

---

## API

Base path `/api`. Errors are [RFC 7807 ProblemDetails](https://www.rfc-editor.org/rfc/rfc7807) with a human-readable `detail`.
Validation errors also carry field-level `errors`. Requests may send `X-Client-Id` (the UI sends a per-tab id) so live
events can say which client made a change.

| Method & path | What it does | Success | Errors |
|---|---|---|---|
| `GET /branches` | Warehouse and branches | 200 | |
| `GET /products` | Products | 200 | |
| `GET /branches/{id}/stock` | Per product: on hand, requested by open outgoing transfers, incoming in transit | 200 | 404 |
| `GET /branches/{id}/movements?productId=&take=` | Ledger entries, newest first (take 1–100, default 20) | 200 | 400, 404 |
| `GET /transfers?branchId=&direction=&status=&page=&pageSize=` | One page, newest first, with totals and per-status counts | 200 | 400 |
| `GET /transfers/{id}` | One transfer | 200 | 404 |
| `POST /transfers` | Request a transfer (optional `Idempotency-Key` GUID header) | 201, or 200 + replay header | 400, 404, 422 |
| `POST /transfers/{id}/dispatch` | Requested → Dispatched; stock leaves the sender | 200 | 404, 409 |
| `POST /transfers/{id}/receive` | Dispatched → Received; stock arrives. Optional body for a short receipt | 200 | 400, 404, 409 |
| `POST /transfers/{id}/cancel` | Requested → Cancelled; no stock moves | 200 | 404, 409 |
| `GET /events` | Server-sent events: `ready`, `transfer` (one per committed change), `ping` | 200 stream | |

`direction` is `Any` (default; the branch is sender or receiver), `Outgoing` or `Incoming`, and needs `branchId`.
`pageSize` defaults to 20 (max 100).

```jsonc
// POST /api/transfers
{ "fromBranchId": 2, "toBranchId": 3, "lines": [ { "productId": 1, "quantity": 5 } ] }

// POST /api/transfers/{id}/receive (optional body; products left out count as fully received)
{ "lines": [ { "productId": 1, "quantityReceived": 3, "reason": "Damaged" } ], "note": "Two bags split" }
```

Each transfer includes `allowedActions` (e.g. `["dispatch", "cancel"]`), taken from the server's single transition
table. The UI shows only those buttons, so it never re-implements the rules.

---

## Seed data

Created on first startup when the database is empty, with an opening entry in the ledger for each figure.

| Location | Rice 5kg | Red Dhal 1kg | Coconut Oil 1L | Ceylon Tea 400g | White Sugar 1kg |
|---|---:|---:|---:|---:|---:|
| Central Warehouse | 500 | 400 | 300 | 250 | 450 |
| Colombo | 40 | 25 | 12 | 30 | 20 |
| Kandy | 15 | 8 | 6 | 18 | 10 |
| Galle | 2 | 10 | 3 | 6 | 4 |

Every location starts with stock of every product. Galle is deliberately low, so to see the "not enough stock" rule
straight away, request 5 Coconut Oil from Galle and dispatch it. Stock may go down to exactly zero (the brief only
forbids going *below* zero), so dispatching all of a branch's stock is allowed.

---

## Assumptions and decisions

The brief leaves some things open. Each decision below is explicit, and where a reasonable person could want it
the other way, the system makes the alternative visible rather than hiding it.

- **The warehouse acts as a branch.** It is a location with `IsWarehouse = true` and can send and receive.
- **Stock is checked at dispatch, not reserved at request.** The brief says stock leaves at dispatch, and
  reserving on request would let an unapproved request block stock. To keep that from being a blind spot, every
  stock figure also shows how much open requests ask for and how much is incoming. The request form warns when
  other open requests already compete for the same stock ("whichever is dispatched first gets it").
- **A dispatch is all or nothing.** Sending part of a request would silently change what was asked for. If any
  line is short, nothing moves and the response names every short line.
- **In-transit stock belongs to neither branch.** It left the sender at dispatch and is shown as "incoming" at the
  receiver until it is received.
- **A repeated action is a successful no-op, clearly flagged.** It returns 200 with `Idempotent-Replayed: true`
  (so retries after a lost response succeed), and the UI tells the person it had already happened. An action
  from the wrong status is a 409.
- **The branch filter is explicit.** With a branch chosen, the list can show transfers in and out, outgoing only,
  or incoming only. Status counts reflect the chosen branch and direction.
- **Short receipts must be explained.** Each short line needs a reason (Damaged, Missing or Other, where Other needs a
  note). The shortfall is written off: it left the sender and never reached the receiver, and the ledger shows both.
  It does not return to the sender.
- **Transfers are immutable once created**, apart from status changes and the receipt. To change a request,
  cancel it and create a new one. There is no delete, so the history stays complete.
- **One line per product; whole units; at most 50 lines; 1 to 1,000,000 per line.** Duplicate lines are rejected
  with a message asking to combine them, and the limits are guardrails against absurd input.
- **No authentication or roles**, per the brief. Anyone can perform any action (see trade-offs).

## Trade-offs

- **No authentication.** That is the brief's choice, but it means nobody is recorded as having done each step.
  With auth, the ledger and transfer timestamps would carry the user, and only the sending branch could dispatch.
- **Live updates are in-process.** They suit a single API instance. Several instances would need a shared
  backplane (Redis pub/sub or SQL Server change notifications). A screen that misses events while disconnected
  refetches on reconnect, so it never stays stale.
- **Offset paging.** If a new transfer arrives while you are on page 2, items shift by one. Keyset paging would
  avoid that, but offset is simpler and gives page numbers.
- **Without an `Idempotency-Key`, a create retried after a transient fault could be stored twice.** The UI always
  sends a key, and API clients should too.
- **Tests need Docker.** That is deliberate: an in-memory database would not enforce transactions, conditional
  updates or CHECK constraints, which are exactly what the tests need to prove.
- **Plain HTTP in development** to avoid dev-certificate trust problems. HTTPS redirection and HSTS are on outside Development.
- **Receiving into a missing stock row creates it.** The seed data gives every branch a row for every product, so
  this path is rarely used. On SQL Server at READ COMMITTED, concurrent receives into a missing row serialise on a
  key lock (verified with a lock dump), and the code also handles a duplicate-key insert as a safety net.
- **Not re-verified in a browser after the hardening round.** The UI builds and lints cleanly and every API
  behaviour is covered by tests, but the final round of UI changes (live updates, paging, receive reasons, ledger
  history) was not walked through in a browser or checked from a fresh clone.

## With another day

- Authentication with per-branch permissions, and the user recorded on every action and ledger row.
- Stock adjustments (counts, write-offs) as further ledger entry kinds, plus a reconciliation report.
- A loss report built from shortfall reasons.
- Front-end tests (component tests plus a Playwright end-to-end run), and a CI workflow that runs both suites.
- The API in `docker-compose.yml` too, so the whole system starts with one command.

---

## Project layout

```text
src/MihiraStores.Api/
  Domain/       entities, the transfer state machine (TransferStatus.cs), ledger entry
  Data/         DbContext with constraints, migrations, seeder
  Transfers/    TransferService (commands, owns every rule), TransferQueries (reads), validation, thin endpoints
  Stock/        StockMover: the only code that changes stock, and it writes the ledger
  Live/         Server-sent events
  Branches/, Products/, Errors/
tests/MihiraStores.Api.Tests/   Integration tests (WebApplicationFactory + Testcontainers SQL Server)
web/                            React + TypeScript UI (Vite dev server proxies /api to the API)
docs/                           The build prompt used with Claude Code, and the AI session log
```

## Time spent

**Started: 10:42:57 · Finished: 13:00:50 · Total: about 2 h 18 min** (21 October 2026, Sri Lanka time, UTC+05:30)

Analysing the brief and writing the build prompt came just before that and was finished by 10:29:49, when the
prompt file was saved. Times come from file-system and git commit timestamps. Run `git log --format='%h %ci %s'`
to see every commit time.

| Stage | Started | Finished | Duration |
|---|---|---|---|
| Analysing the brief and writing the build prompt | before 10:29:49 | 10:29:49 | not timestamped |
| Build: setup to docs (commits `ec74dfd` to `6a9bcf2`) | 10:42:57 | 11:11:04 | ~28 min |
| Review of assumptions and weak spots (discussion, no code) | 11:11:04 | 11:40:33 | ~29 min |
| Hardening: fixes, ledger, live updates (commits `79fee2c` to `acdcf7c`) | 11:40:33 | 12:17:40 | ~37 min |
| Final changes, docs, my own review and push (commits `69bd96f` to `99a1f61`) | 12:17:40 | 13:00:50 | ~43 min |

