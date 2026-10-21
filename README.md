# Mihira Stores: branch stock transfers

A small system for moving stock between Mihira Stores' warehouse and its Colombo, Kandy and Galle branches.
A branch **requests** a transfer, the sender **dispatches** it (stock leaves the sender), and the receiver
**receives** it (stock arrives). A transfer can be **cancelled** until it is dispatched.

- **API:** ASP.NET Core (.NET 10) minimal APIs, EF Core 10, SQL Server
- **UI:** one React + TypeScript screen (Vite)
- **Tests:** 28 xUnit integration tests that run the real API against a real SQL Server (Testcontainers)

The design centres on the client's main worry: **stock must never go negative, and a double-click or a
network retry must never move stock twice**. See [How double counting and negative stock are prevented](#how-double-counting-and-negative-stock-are-prevented).

---

## Run it

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download), Node.js 20.19+ (or 22.12+), and Docker (Docker Desktop on Windows/macOS).

```bash
# 1. Start SQL Server (local dev container, port 14333)
docker compose up -d --wait

# 2. Start the API on http://localhost:5080. It creates the database, applies migrations and seeds data on startup.
dotnet run --project src/MihiraStores.Api

# 3. In a second terminal, start the UI
cd web
npm install
npm run dev
```

Open **<http://localhost:5173>**.

**Using an existing SQL Server instead of Docker:** set the connection string before step 2, e.g. for a local SQL Express:

```powershell
$env:ConnectionStrings__Default = "Server=.\SQLEXPRESS;Database=MihiraStores;Trusted_Connection=True;TrustServerCertificate=True"
```

**Start again from the seed data:** stop the API, run `docker compose down -v`, then repeat the steps above.

> The SA password in `docker-compose.yml` / `appsettings.Development.json` is a throwaway **local development
> credential only**. Never reuse it anywhere.

### Run the tests

```bash
dotnet test
```

Docker must be running: the tests start their own SQL Server container. The first run downloads the SQL Server
image (~1.5 GB), so it can take a few minutes. After that the suite takes about 10 seconds.

Optional: `src/MihiraStores.Api/MihiraStores.Api.http` has sample requests for every endpoint (VS Code REST Client / Visual Studio / Rider).

---

## How double counting and negative stock are prevented

The rules are enforced in the database, not by checking state in C# and then writing it.

1. **Each status change is claimed with one conditional `UPDATE`.** For example, dispatch runs
   `UPDATE Transfers SET Status='Dispatched' … WHERE Id=@id AND Status='Requested'` and looks at the affected
   row count. Ten simultaneous dispatches all reach the database, but only one can match `Status='Requested'`.
   The rest affect 0 rows and never touch stock. A read-check-write version would let all ten through.
2. **Stock is taken with a guarded `UPDATE`.** `… SET Quantity = Quantity - @qty WHERE … AND Quantity >= @qty`.
   If any line is short, the whole dispatch is rolled back: status, earlier lines, everything. The 409 response
   lists every short product with what is available and what is needed.
3. **Both steps run in one transaction.** READ COMMITTED is enough, because a conditional `UPDATE` locks the row it checks,
   so a competing request waits and then re-checks the committed value. Lines are processed in product order
   so concurrent transfers always lock stock rows in the same order and cannot deadlock.
4. **The database refuses bad data even if the code has a bug.** CHECK constraints enforce
   `Quantity >= 0` on stock, from ≠ to on transfers, `Quantity > 0` on lines, and
   `0 ≤ QuantityReceived ≤ Quantity`.
5. **Repeats are safe.** Repeating an action that already happened (dispatching a Dispatched transfer)
   returns **200** with the transfer unchanged and moves no stock, so a client that retried after a lost
   response sees success. An action from the wrong status (receiving a Requested transfer) returns **409**.
6. **Creating a transfer is idempotent too.** The UI sends an `Idempotency-Key` header. A unique index means
   a double-clicked or retried create returns the original transfer (200) instead of creating a second one,
   even when the duplicates arrive at the same instant.

The tests prove these points against real SQL Server: 10 parallel dispatches move stock once, 10 parallel
receives add it once, two transfers competing for the same stock can't both be dispatched, 8 parallel
creates with one key make one transfer, and a short line rolls back the lines that had enough. The
double-dispatch tests were also checked to **fail** when the status guard is removed from the `UPDATE`.

---

## API

Base path `/api`. Errors are [RFC 7807 ProblemDetails](https://www.rfc-editor.org/rfc/rfc7807) with a human-readable `detail`.
Validation errors also carry field-level `errors`.

| Method & path | What it does | Success | Errors |
|---|---|---|---|
| `GET /branches` | Warehouse and branches | 200 | |
| `GET /products` | Products | 200 | |
| `GET /branches/{id}/stock` | Quantity of every product at a branch (0 included) | 200 | 404 |
| `GET /transfers?branchId=&status=` | Transfers, newest first; `branchId` matches sender **or** receiver; max 200 | 200 | 400 bad status |
| `GET /transfers/{id}` | One transfer | 200 | 404 |
| `POST /transfers` | Request a transfer (optional `Idempotency-Key` GUID header) | 201, or 200 on a repeat | 400, 404 |
| `POST /transfers/{id}/dispatch` | Requested → Dispatched; stock leaves the sender | 200 | 404, 409 |
| `POST /transfers/{id}/receive` | Dispatched → Received; stock arrives. Optional body for a partial receipt | 200 | 400, 404, 409 |
| `POST /transfers/{id}/cancel` | Requested → Cancelled; no stock moves | 200 | 404, 409 |

```jsonc
// POST /api/transfers
{ "fromBranchId": 2, "toBranchId": 3, "lines": [ { "productId": 1, "quantity": 5 } ] }

// POST /api/transfers/{id}/receive (optional body; products left out count as fully received)
{ "lines": [ { "productId": 1, "quantityReceived": 3 } ] }
```

Each transfer in a response includes `allowedActions` (e.g. `["dispatch", "cancel"]`), taken from the
server's single transition table. The UI shows only those buttons, so it never re-implements the rules.

---

## Seed data

Created on first startup when the database is empty. Every location has a stock row for every product.

| Location | Rice 5kg | Red Dhal 1kg | Coconut Oil 1L | Ceylon Tea 400g | White Sugar 1kg |
|---|---:|---:|---:|---:|---:|
| Central Warehouse | 500 | 400 | 300 | 250 | 450 |
| Colombo | 40 | 25 | 12 | 30 | 20 |
| Kandy | 15 | 8 | 0 | 18 | 10 |
| Galle | 2 | 10 | 0 | 6 | 0 |

To see the "not enough stock" rule straight away, request Coconut Oil from Galle and dispatch it.

---

## Assumptions

- **The warehouse acts as a branch.** It is a location with `IsWarehouse = true` and can send and receive like any branch.
- **Stock is checked at dispatch, not at request.** The brief says stock leaves at dispatch, so a request can ask for
  more than is on hand today (stock may arrive first). The UI warns but does not block. Nothing is reserved.
- **While a transfer is Dispatched, its stock is in transit and belongs to neither branch.**
- **Repeating an action that already succeeded returns 200 without moving stock**, rather than 409, so that
  network retries look like success. An action from the wrong status is a 409.
- **The branch filter matches either side** (a transfer from Colombo or to Colombo shows under Colombo).
- **A product appears once per transfer.** Duplicate lines are rejected with a message asking to combine them.
- **Quantities are whole units**, from 1 to 1,000,000 per line, with at most 50 lines per transfer.
- **Partial receipt:** the shortfall (sent − received) is recorded on the line and is treated as lost (damaged or missing).
  It does not go back to the sender.
- **No authentication or roles**, per the brief. Anyone can perform any action.
- **The UI's idempotency key follows the form contents.** Re-submitting the same content (double-click, retry
  after an error) reuses the key. Editing the form makes a new one.

## Trade-offs

- **No automatic retry on transient SQL errors.** EF's `EnableRetryOnFailure` needs every manual transaction
  wrapped in an execution strategy. Retries are left off to keep the transaction code simple.
- **The idempotency key isn't bound to the request body.** Reusing a key with a different body returns the
  original transfer. A production system would store a hash of the body and reject mismatches.
- **No pagination.** The list is capped at the newest 200 transfers.
- **Tests need Docker.** That was deliberate: an in-memory database would not enforce transactions, conditional
  updates or CHECK constraints, which are exactly what the tests need to prove.
- **Plain HTTP in development** to avoid dev-certificate trust problems. HTTPS redirection and HSTS are on outside Development.
- **Receiving into a missing stock row** inserts it. The seed data makes that path unused, and two concurrent
  receives creating the same missing row could collide. That's acceptable at this scale.
- **The UI refetches after each action** rather than streaming updates, so another user's changes appear on
  the next action or when you press refresh.

## With another day

- **A stock movement ledger.** Add an append-only table of every +/− with the transfer that caused it, so stock
  figures can be audited and rebuilt. That directly addresses "the stock figures never match".
- **Authentication with per-branch permissions:** only the sending branch dispatches, only the receiver receives.
  Record who did each step.
- A reason field for receipt shortfalls (damaged / missing), and a report of losses.
- Optional stock reservation at request time, if the business wants requests to hold stock.
- Pagination and search on the transfer list. Live updates (SignalR or polling) for multiple users.
- Front-end tests (component tests for the form, plus one Playwright end-to-end run), and a CI workflow that runs both suites.
- The API in `docker-compose.yml` too, so the whole system starts with one command.

---

## Project layout

```text
src/MihiraStores.Api/      API: Domain/ (entities + transition table), Data/ (DbContext, migrations, seeder),
                           Transfers/ (TransferService owns every rule; endpoints are thin), Branches/, Products/, Errors/
tests/MihiraStores.Api.Tests/  Integration tests (WebApplicationFactory + Testcontainers SQL Server)
web/                       React + TypeScript UI (Vite dev server proxies /api to the API)
docs/                      The build prompt used with Claude Code, and the AI session log
```

## Time spent

TODO (human): ~X hours.
