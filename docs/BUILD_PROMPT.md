# Build prompt: Mihira Stores branch stock transfers

> Paste this whole file into Claude Code, opened at the repository root, or tell it:
> "Read docs/BUILD_PROMPT.md and carry it out."

---

## 0. Your role and how to work

You are a senior full-stack engineer building a take-home assessment for a software engineering role. The reviewers will clone the repo, follow the README, try to break the stock rules (double-clicks, parallel requests, over-dispatch), read the code and tests, and read the commit history.

How to work:

- **Build it, don't just describe it.** Work through the phases in section 12 in order. Once the README steps work, check every item in section 13 yourself.
- **Run things.** After each phase, build and run the tests. Never say something works, passes or is done unless you have actually run it. If something fails, fix it, or report it as failing with the output.
- **Commit after each phase** with a clear message (see section 12). The history is reviewed. Don't push, and don't create remotes.
- **Stay in scope.** "A smaller piece of work done well and explained honestly beats a big one rushed." The time budget is about 3 hours of human time, and the reviewers judge whether the structure is "sensible for its size". Section 11 lists what not to build.
- **Make decisions without asking**, unless a choice is truly ambiguous and expensive to undo. Record every assumption so it can go into the README.
- **Keep a running log** in `docs/AI_SESSION_LOG.md`. Note every place where you (the AI) got something wrong, had to correct yourself, or where a build or test failure showed a wrong assumption. Record facts only and never invent entries. This log feeds AI_USAGE.md.

---

## 1. The brief (source of truth)

Mihira Stores (a fictional retail chain) has a **warehouse** and three branches: **Colombo, Kandy and Galle**. Branches move stock between each other. Today this happens by phone and a notebook, and the figures never match.

The required flow:
1. A branch **requests** a transfer: from branch, to branch, and one or more products with quantities.
2. The sending branch **dispatches** it. **Stock leaves the sender at this point.**
3. The receiving branch **receives** it. **Stock arrives at the receiver at this point.**
4. A transfer that has not been dispatched can be **cancelled**.

The client's biggest worry: **stock going negative**, and **the same transfer being counted twice** when someone double-clicks or the network retries.

Allowed statuses: `Requested → Dispatched → Received`, or `Requested → Cancelled`. **Nothing else is allowed.**

Required business rules:
- Stock at a branch can never go below zero. A dispatch that would cause this is rejected with a clear message.
- Dispatching or receiving the same transfer twice must not move stock twice.
- Actions are only allowed from the right status. For example, a transfer that was never dispatched cannot be received.
- From and to branches must be different, and quantities must be greater than zero.

Required deliverables:
- **Back end:** ASP.NET Core Web API on .NET 8 or later, EF Core, SQL Server or PostgreSQL.
- **Front end:** one React screen (TypeScript). The user can pick from/to branches, add products and quantities, see available stock, and submit. The user can also see a list of transfers with their status and dispatch, receive or cancel them. It must work and show errors clearly. apply apple ui design principle and ui and ux design best practices and ux should be seamless and the ui and ux should be excellent.
- **Tests:**  perform meaningful automated tests of the business rules (for example over-stock dispatch, double dispatch, receive).
- **Seed data**, created when the app starts: the warehouse, three branches, five products and some starting stock.
- **README.md:** how to run it in a few steps, assumptions, trade-offs, and what you would do with another day.
- **AI_USAGE.md:** which tools were used and for what, one place where the AI was wrong or unhelpful and how that was noticed, and which parts the human wrote or decided.
- **Time spent:** roughly how long the work took.

Not needed: login, user roles, styling, deployment. Docker is optional.

Optional, only if everything else is done and green: the receiving branch can record a smaller quantity than was sent (for example, damaged goods), and the difference is kept on record.

How it is reviewed: it runs by following the README; the stock rules hold, including double-dispatch; the code is clear and sensibly structured for its size; the tests check things that matter; the README and AI notes are clear and honest.

---

## 2. Current state of the repo and machine

- Windows 11. Shell: PowerShell, with Git Bash also available.
- Installed: .NET SDK **10.0.400**, Node **24**, npm 11, git, **Docker 29**, and a local **SQL Server Express** instance (`.\SQLEXPRESS`).
- The repo root holds `ravigno.slnx` and a template Web API project at `ravigno/` (net10.0, controllers template, WeatherForecast sample, a VS-generated `Dockerfile` and `.dockerignore`, user secrets id).
- **The folder is not a git repository yet.** Initialise it first.

---

## 3. Target stack and decisions (already made, don't re-litigate)

| Concern | Decision | Why |
|---|---|---|
| Runtime | .NET 10 (keep the existing TFM) | The brief allows ".NET 8 or later". The SDK is installed. |
| Database | SQL Server, via EF Core SQL Server provider (latest stable 10.x) | SQL Server is installed locally and runs in Docker for reviewers. |
| Schema | EF Core **migrations**, applied at startup with `Database.Migrate()` | Shows a real schema history. `EnsureCreated` can't evolve and skips migrations. |
| API style | **Minimal APIs** in endpoint groups (`MapGroup`) | Least ceremony for 8 endpoints. Delete the controllers template code. |
| Errors | RFC 7807 **ProblemDetails** everywhere | Gives the UI one consistent `title`/`detail` shape to show. |
| Front end | **Vite + React + TypeScript**, plain `fetch`, no UI library | The brief needs one screen and no styling. Fewer dependencies mean fewer failure points. |
| Tests | **xUnit + WebApplicationFactory + Testcontainers.MsSql** (real SQL Server in Docker) | The rules depend on transactions, atomic conditional `UPDATE`s and CHECK constraints, and only a real engine runs those. |
| Local DB for reviewers | `docker-compose.yml` with `mcr.microsoft.com/mssql/server:2022-latest` | One dependency (Docker). The README also documents pointing at an existing SQL Server instead. |

---

## 4. Solution structure

Rename the template to meaningful names. Keep it to **two .NET projects plus one web app**. Don't split into Domain/Application/Infrastructure projects (see section 11 for why).

```
/MihiraStores.slnx
/docker-compose.yml               SQL Server 2022 for local dev
/.gitignore                       .NET + Node + VS + .env
/README.md
/AI_USAGE.md
/docs/BUILD_PROMPT.md             this file (keep it; it is honest evidence of AI use)
/docs/AI_SESSION_LOG.md           running log (section 0)
/src/MihiraStores.Api/
    Program.cs                    composition root only: services, middleware, endpoint mapping, migrate+seed
    Domain/
        Branch.cs  Product.cs  StockLevel.cs  Transfer.cs  TransferLine.cs  TransferStatus.cs
    Data/
        AppDbContext.cs           with IEntityTypeConfiguration classes or OnModelCreating, keys, indexes, CHECKs
        DbSeeder.cs               idempotent seeding
        Migrations/
    Transfers/
        TransferService.cs        ALL state-transition and stock logic lives here
        TransferEndpoints.cs      thin HTTP mapping only
        TransferDtos.cs           request/response records
        CreateTransferValidator.cs (or validation inside the service: keep it simple)
    Branches/
        BranchEndpoints.cs        GET /branches, GET /branches/{id}/stock
    Products/
        ProductEndpoints.cs       GET /products
    Errors/
        AppException.cs           NotFound / Conflict / Validation kinds
        ProblemDetailsExceptionHandler.cs  IExceptionHandler → ProblemDetails
/tests/MihiraStores.Api.Tests/
    ApiFactory.cs                 WebApplicationFactory + Testcontainers SQL Server
    TransferRulesTests.cs
    ...
/web/                             Vite React TS app
    src/api.ts                    typed client + error parsing
    src/types.ts
    src/App.tsx                   the screen (split into a few components if it gets long)
    vite.config.ts                dev proxy /api → API
```

Rules for the layout:
- **Endpoints are thin.** They parse the request, call `TransferService`, and map the result. No business logic in endpoint lambdas.
- **`TransferService` is the single owner of the rules.** Nothing else changes `Transfers.Status` or `StockLevels.Quantity`.
- Delete `WeatherForecast*`, the `Controllers` folder, and `ravigno.http`, or replace the last with a useful `MihiraStores.Api.http` holding sample requests for every endpoint.
- Update or delete the template `Dockerfile` so it matches the new paths. A broken Dockerfile in the repo looks careless. If you keep it, make sure `docker build` works.

---

## 5. Data model

All quantities are `int`. Never use floating point for counts. Timestamps are `DateTimeOffset` in UTC. Store enums as strings (`HasConversion<string>()`) so the database stays readable.

**Branch**: `Id` (int), `Name` (unique, required, max 100), `IsWarehouse` (bool).
The warehouse is modelled as a Branch with `IsWarehouse = true`. It can send and receive like any branch. *(Assumption: record it in the README.)*

**Product**: `Id` (int), `Sku` (unique, required, max 32), `Name` (required, max 200).

**StockLevel**: composite PK `(BranchId, ProductId)`, `Quantity` (int).
- **DB CHECK constraint `Quantity >= 0`.** This is the last line of defence: even buggy code can't make stock negative.
- FKs to Branch and Product.

**Transfer**: `Id` (int), `FromBranchId`, `ToBranchId`, `Status` (string enum), `IdempotencyKey` (nullable `Guid`, **unique filtered index** where not null), `CreatedAt`, `DispatchedAt?`, `ReceivedAt?`, `CancelledAt?`.
- **DB CHECK constraint `FromBranchId <> ToBranchId`.**
- Index on `(Status)`, `(FromBranchId)` and `(ToBranchId)` for the list filter.

**TransferLine**: `Id`, `TransferId`, `ProductId`, `Quantity` (the amount requested and sent), `QuantityReceived` (nullable int; set on receive).
- **DB CHECK `Quantity > 0`**, and **CHECK `QuantityReceived IS NULL OR (QuantityReceived >= 0 AND QuantityReceived <= Quantity)`**.
- **Unique index `(TransferId, ProductId)`.** A product appears at most once per transfer.

Don't add a `RowVersion` / concurrency token. The design in section 6 doesn't need one, and unused machinery confuses readers.

---

## 6. The core: state transitions, concurrency and idempotency

This is the most important section. The reviewers named the double-dispatch case explicitly.

### 6.1 The anti-pattern you must NOT use

```csharp
var t = await db.Transfers.FindAsync(id);
if (t.Status != TransferStatus.Requested) throw ...;   // two parallel requests both pass this check
t.Status = TransferStatus.Dispatched;
stock.Quantity -= qty;                                  // stock moves twice
await db.SaveChangesAsync();
```
A read-check-write in application code is a race. Two requests that arrive together both read `Requested`, and both move stock. Disabling the button in the UI doesn't fix this, because network retries and other clients bypass the UI.

### 6.2 The required pattern: atomic conditional updates inside one transaction

Let the database do the check and the change in **one statement**, and use the affected row count as the result.

**Dispatch(transferId):**
```
BEGIN TRANSACTION
  1. claimed = UPDATE Transfers
               SET Status = 'Dispatched', DispatchedAt = @now
               WHERE Id = @id AND Status = 'Requested'
     -- EF: db.Transfers.Where(t => t.Id == id && t.Status == Requested)
     --        .ExecuteUpdateAsync(s => s.SetProperty(...))
  2. if claimed == 0 → ROLLBACK, then resolve the outcome (6.3).
  3. Load the transfer's lines (AsNoTracking), ORDERED BY ProductId
     (one fixed lock order across transactions prevents deadlocks).
  4. For each line:
       n = UPDATE StockLevels
           SET Quantity = Quantity - @qty
           WHERE BranchId = @from AND ProductId = @p AND Quantity >= @qty
       if n == 0 → ROLLBACK (this also undoes step 1 and earlier lines) and throw
         Conflict listing EVERY short line, e.g.
         "Cannot dispatch transfer #12: Colombo has 3 × Rice 5kg (RICE-5KG) but the transfer needs 10."
         (Read current quantities after the rollback, or check all lines before
          throwing, so the message lists all the shortfalls, not just the first.)
COMMIT
```

**Receive(transferId, optional per-line received quantities):**
```
BEGIN TRANSACTION
  1. claimed = UPDATE Transfers SET Status='Received', ReceivedAt=@now
               WHERE Id=@id AND Status='Dispatched'
  2. if claimed == 0 → ROLLBACK and resolve the outcome (6.3).
  3. For each line ordered by ProductId: set QuantityReceived (defaults to Quantity),
     then increment the receiver's StockLevel by QuantityReceived.
     If the receiver has no StockLevel row for that product, insert one.
     (The seeder creates a row for every branch × product, but the code must not
      rely on that silently.)
COMMIT
```

**Cancel(transferId):**
```
claimed = UPDATE Transfers SET Status='Cancelled', CancelledAt=@now
          WHERE Id=@id AND Status='Requested'
if claimed == 0 → resolve the outcome (6.3). Cancel never touches stock.
```

Use `await using var tx = await db.Database.BeginTransactionAsync()`. The default READ COMMITTED isolation is enough, because each conditional `UPDATE` takes an update lock on the rows it checks. Say this in a short code comment, because a reviewer will wonder about it.

Wrap the transaction in EF's execution strategy (`db.Database.CreateExecutionStrategy().ExecuteAsync(...)`) **only** if you enable `EnableRetryOnFailure`. The two must be used together, or EF throws. The simplest correct choice is to leave retry-on-failure off and say so in the README trade-offs.

### 6.3 Resolving "0 rows claimed": idempotent replay versus an invalid transition

When the conditional status update affects 0 rows, reload the transfer and decide:

| Current status | Action asked | Result |
|---|---|---|
| not found | any | **404** ProblemDetails: "Transfer #12 was not found." |
| already the target status (Dispatched for dispatch, Received for receive, Cancelled for cancel) | same action | **200 OK** with the current transfer and **no stock movement**. This is an idempotent replay: a client that retried after a lost response, or a double-click, sees success. |
| any other | any | **409 Conflict**, e.g. "Cannot receive transfer #12: it is Requested. Only Dispatched transfers can be received." |

Define the allowed transitions **once**, as a small table or a switch in `TransferStatus` / `TransferService`, and use it both for the error messages and for the `allowedActions` field returned to the UI (section 7). There must be no duplicated status logic in the front end.

### 6.4 Idempotent creation (the double-click on "Submit")

- `POST /transfers` accepts an optional **`Idempotency-Key`** header holding a GUID. The UI makes one key per form with `crypto.randomUUID()` and keeps it until the request succeeds.
- If a transfer with that key already exists, return it with **200** instead of creating a new one.
- Catch the race where two identical requests insert at the same moment: catch `DbUpdateException` caused by the unique index violation (SQL error 2601/2627), then load and return the existing transfer.
- A malformed header (not a GUID) gets a **400**.
- Record in the README trade-offs that the server doesn't check whether a repeated key arrives with a different body. A production system would hash the body and reject mismatches.

---

## 7. API contract

Base path: `/api`. JSON in camelCase. Enums are serialised as strings (`JsonStringEnumConverter`). Every error is a ProblemDetails with a human-readable `detail`. Validation errors use `ValidationProblemDetails`, keyed by field, for example `lines[1].quantity`.

| Method & path | Purpose | Success | Errors |
|---|---|---|---|
| `GET /api/branches` | All branches (id, name, isWarehouse) | 200 | — |
| `GET /api/products` | All products (id, sku, name) | 200 | — |
| `GET /api/branches/{id}/stock` | Every product's quantity at the branch, including zeros | 200 `[{productId, sku, name, quantity}]` | 404 unknown branch |
| `POST /api/transfers` | Create a request | **201** + `Location` + TransferDto (or **200** on an idempotent replay) | 400 validation, 404 unknown branch/product |
| `GET /api/transfers?branchId=&status=` | List, newest first. `branchId` matches **either** from or to. `status` is optional. Cap at 200 rows. | 200 `TransferDto[]` | 400 invalid status value |
| `GET /api/transfers/{id}` | One transfer | 200 | 404 |
| `POST /api/transfers/{id}/dispatch` | Requested → Dispatched; stock leaves the sender | 200 TransferDto | 404, 409 wrong status, 409 insufficient stock |
| `POST /api/transfers/{id}/receive` | Dispatched → Received; stock arrives at the receiver. Optional body (partial receipt, phase 7 only) | 200 | 404, 409, 400 |
| `POST /api/transfers/{id}/cancel` | Requested → Cancelled | 200 | 404, 409 |

**CreateTransferRequest**
```json
{ "fromBranchId": 2, "toBranchId": 3, "lines": [ { "productId": 1, "quantity": 5 } ] }
```
Validation (400, every error reported together, not one at a time):
- `fromBranchId` and `toBranchId` are required and must exist (404 if a referenced id doesn't exist; 400 if missing).
- `fromBranchId != toBranchId`: "From and to branches must be different."
- `lines` has between 1 and 50 items.
- Each `quantity` is between 1 and 1,000,000. Must be greater than zero, per the brief. The upper bound guards against overflow and nonsense input.
- No duplicate `productId` across lines: "Product RICE-5KG appears more than once; combine the lines."
- Every `productId` exists.
- **Don't check stock levels at creation.** Stock is checked at dispatch, which is where the brief says stock leaves. A request is a request; stock can change before dispatch. The UI shows available stock as guidance (section 8).

**TransferDto**
```json
{
  "id": 12,
  "fromBranch": { "id": 2, "name": "Colombo" },
  "toBranch":   { "id": 3, "name": "Kandy" },
  "status": "Requested",
  "createdAt": "...", "dispatchedAt": null, "receivedAt": null, "cancelledAt": null,
  "lines": [ { "productId": 1, "sku": "RICE-5KG", "productName": "Rice 5kg",
               "quantity": 10, "quantityReceived": null } ],
  "allowedActions": ["dispatch", "cancel"]
}
```
`allowedActions` comes from the single transition table on the server. The UI renders buttons from it.

---

## 8. Front end: one screen

Vite + React + TypeScript (strict mode on). In dev, use the Vite proxy (`/api` → `http://localhost:5080`) so CORS isn't needed. No component library, no state library, no router. Basic readable CSS is fine. Don't spend time on styling.

The screen has three areas:

**A. New transfer form**
- "From" and "To" selects, filled from `GET /api/branches` (warehouse labelled "(Warehouse)"). Show an inline message and disable submit when they are the same.
- Line rows: product select, quantity input (`type=number`, min 1, whole numbers only), a remove button, and an **"Available at {from}: N"** hint taken from `GET /api/branches/{from}/stock`. Refetch it whenever "From" changes. Highlight the hint, without blocking, when quantity > available, with the text "more than currently available; dispatch will be rejected unless stock arrives first".
- "Add product" button. A product already used in another line is disabled in later selects.
- Submit button: **disabled while the request is in flight**. It sends the form's `Idempotency-Key`. On success, reset the form, make a **new** key, refresh the list and show a short success message ("Transfer #12 requested"). On failure, keep the form and the **same** key, so a retry can't create a duplicate.

**B. Transfers list**
- Filters: branch (All, or one branch) and status (All, or one status). Changing a filter refetches.
- Table: #, from → to, lines (e.g. "Rice 5kg × 10, Tea 400g × 4"), status shown as a plain badge, timestamps, actions.
- Action buttons come **only from `allowedActions`**. Each row's buttons are disabled while that row's action is in flight. After any action, refetch the list and the stock panel.
- Cancel asks for confirmation (`window.confirm` is fine).

**C. Stock panel**
- A branch select and a table of product, SKU and quantity for that branch. It refreshes after every action, so a reviewer can watch stock move.

**Errors**
- `api.ts` has one `request<T>()` helper. On a non-2xx response it parses ProblemDetails and throws an `ApiError` carrying `title`, `detail`, `status` and field `errors`. On a network failure it throws "Could not reach the server. Is the API running?"
- One dismissible error banner shows `detail` (or `title`). Field validation errors appear next to the relevant input where practical, or are listed in the banner.
- Never show raw JSON, stack traces or `[object Object]`.

---

## 9. Security and robustness

There's no auth by design (the brief says so). Still do the following, and say in the README that auth was out of scope and how you'd add it.

- **Validate all input on the server.** The UI checks are a convenience only. The API must reject bad input on its own.
- **No raw SQL string building.** Use EF LINQ and `ExecuteUpdateAsync` only, which are parameterised. If you ever need raw SQL, use `FromSql`/`ExecuteSql` with interpolation, never string concatenation.
- **Don't leak internals.** Use `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()`. Unhandled exceptions return a generic 500 ProblemDetails. Stack traces appear only in Development, and even then not in the `detail` the UI shows. Log the exception server-side with `ILogger`.
- **Business rule failures are never 500s.** They are 400, 404 or 409 with clear messages.
- **Bounds:** at most 50 lines per transfer, quantities at most 1,000,000, list results capped at 200. Kestrel's default request body limit is fine.
- **Secrets:** the connection string lives in `appsettings.Development.json` / environment variables. The docker-compose SA password is a clearly labelled **local development credential only**, and the README says it must never be reused. No real secrets go in the repo. `.gitignore` covers `.env`, `bin/`, `obj/`, `node_modules/`, `dist/` and `.vs/`.
- **CORS:** not enabled, because the dev proxy is used. If CORS is ever added, it must list exact origins, never `AllowAnyOrigin`.
- **HTTPS:** run the API over plain `http://localhost:5080` in Development so reviewers don't hit dev-certificate trust problems. Keep `UseHttpsRedirection` and HSTS **outside** Development only. Note this in the README.
- **The database enforces the invariants** (CHECK constraints, unique indexes, FKs), so the data stays correct even if application code has a bug.
- **Transfers are never deleted or edited** after creation, apart from status changes and receive quantities. There is no DELETE or PUT endpoint. A cancelled transfer stays as history.

---

## 10. Seed data

`DbSeeder.SeedAsync(db)` runs at startup straight after `Migrate()`. It is **idempotent**: if any branches exist, it does nothing. Use fixed, readable data:

Branches: `1 Central Warehouse (warehouse)`, `2 Colombo`, `3 Kandy`, `4 Galle`.

Products:
| SKU | Name |
|---|---|
| RICE-5KG | Rice 5kg |
| DHAL-1KG | Red Dhal 1kg |
| COCO-OIL-1L | Coconut Oil 1L |
| TEA-400G | Ceylon Tea 400g |
| SUGAR-1KG | White Sugar 1kg |

Starting stock: create a StockLevel for **every branch × product**. Give the warehouse plenty (200–500 each), give the branches modest varied amounts, and include **a few zeros and low numbers** (for example Galle has 0 Coconut Oil and 2 Rice) so a reviewer can trigger the "insufficient stock" rule straight away. Put a small table of the starting stock in the README.

The README should also explain how to reset the data (`docker compose down -v`, or drop the database) so a reviewer can start fresh.

---

## 11. What NOT to do, and why

| Don't | Why |
|---|---|
| Read-check-write status or stock changes in C# (section 6.1) | Race condition. Parallel requests double-move stock, which is exactly what the reviewers test. |
| Rely on disabling buttons to stop double submits | Network retries, two tabs and API clients bypass the UI. The server must be idempotent. |
| Use the EF Core **InMemory** provider (or SQLite as a stand-in) for the rule tests | InMemory has no real transactions, no CHECK constraints and no relational semantics. Tests would pass while the real system is broken. Use real SQL Server through Testcontainers. |
| `EnsureCreated()` | It bypasses migrations, and the schema can't evolve. Use migrations. |
| Reserve or deduct stock when a transfer is **requested** | The brief says stock leaves at **dispatch**. Reservation adds a second ledger the brief didn't ask for. Mention it as a "next day" idea. |
| Silently clamp a dispatch to the available stock | That hides a business failure. Reject it with a clear message. |
| Add auth, roles, users, multi-tenancy or audit-user columns | Out of scope per the brief. It costs time and adds review surface. |
| Clean-architecture multi-project split, CQRS/MediatR, a repository wrapper over EF, AutoMapper, a generic `Result<T>` framework | Over-engineering for about 8 endpoints. Reviewers grade "sensibly structured **for its size**". EF's DbContext already is a unit of work and a repository. |
| Redux, React Query, a router, a UI kit, Tailwind config work | One screen, and styling is not needed. Plain React state and fetch are easier to review. |
| Floating-point quantities | Counts are integers, and floating point brings rounding errors. |
| Hard-delete or edit transfers; PUT/DELETE endpoints | Destroys the history the client is trying to build ("the figures never match"). |
| Business-rule failures as 500, or as 200 with `{ success: false }` | Clients and the UI depend on correct status codes. Use ProblemDetails 400/404/409. |
| Stock checks at request time that **block** creation | Stock can change before dispatch. Dispatch is the authoritative check. The UI only warns. |
| Preview/beta NuGet or npm packages, or unpinned "latest" in docs | Reproducibility for reviewers. Use the latest **stable** versions and commit the lockfile (`package-lock.json`). |
| Features beyond the brief (other than the optional partial receive) | Time is limited. Unrequested features signal poor prioritisation. |
| Claiming tests pass, or that something works, without running it | The reviewers will run it. Honesty is graded. |
| Writing fictional anecdotes in AI_USAGE.md | The brief explicitly grades honesty. Use only real entries from `docs/AI_SESSION_LOG.md`, and leave the human's sections for the human to write. |
| Committing `bin/`, `obj/`, `node_modules/`, `.vs/` or secrets | Noise and risk. |

---

## 12. Phases and commits

Commit at the end of each phase. Use short imperative messages, for example `Add transfer state machine with atomic dispatch`. Build and test before every commit.

1. **Repo setup.** `git init`, `.gitignore`, rename to `MihiraStores.slnx` / `src/MihiraStores.Api`, remove the template sample code, set the launch profile to `http://localhost:5080`, add `docker-compose.yml`, add this prompt under `docs/`, start `docs/AI_SESSION_LOG.md`. *Commit: "Set up solution structure and local SQL Server"*
2. **Domain, data and seeding.** Entities, DbContext configuration (keys, indexes, CHECKs, string enums), the initial migration, `DbSeeder`, and migrate+seed on startup. Run the API and confirm the database and seed rows exist. *Commit.*
3. **Read endpoints.** Branches, products, stock, and the transfers list/get. ProblemDetails handling. Smoke test with the `.http` file or curl. *Commit.*
4. **Transfer commands.** Create (validation + idempotency key), dispatch, receive, cancel, exactly as in section 6. *Commit.*
5. **Tests** (section 14). All green against Testcontainers. *Commit.* If anything in phase 4 turns out wrong, fix it in its own commit and log it in `AI_SESSION_LOG.md`.
6. **Front end** (section 8). Run it against the API and walk the full flow by hand: create, dispatch, receive, cancel, over-dispatch error, double-click submit. *Commit.*
7. **Optional: partial receive.** Only if phases 1–6 are complete and green. `POST /receive` takes an optional `{ "lines": [{ "productId", "quantityReceived" }] }`. Each value must be between 0 and the line's quantity, and the body must list each transfer product at most once. Products left out default to the full quantity. Store `QuantityReceived` and add only that amount to the receiver. The DTO exposes `quantityReceived`, and the UI shows the shortfall (`quantity − quantityReceived`) as "short/damaged". The receive button opens a small inline form, prefilled with full quantities. Add a test. *Commit.*
8. **Docs.** README and AI_USAGE (section 15). Run the README steps from a clean clone in a temp folder to prove they work. *Commit.*

---

## 13. Definition of done (check every item yourself)

- [ ] `docker compose up -d` → `dotnet run --project src/MihiraStores.Api` → the API starts, migrates and seeds on an empty database.
- [ ] `cd web && npm install && npm run dev` → the screen loads at `http://localhost:5173` and talks to the API.
- [ ] `dotnet test` passes, with Docker running.
- [ ] `npm run build` in `web/` passes with no TypeScript errors.
- [ ] Manual check: dispatching more than is available shows a clear error, and stock and status are unchanged.
- [ ] Manual check: double-clicking Dispatch/Receive moves stock once (watch the stock panel).
- [ ] Manual check: double-clicking Submit creates one transfer.
- [ ] Receive on a Requested transfer, or cancel on a Dispatched one, is impossible from the UI (no button) and returns 409 from the API.
- [ ] No warnings left from `dotnet build` that you introduced.
- [ ] Git history shows one commit per phase, with no build output or secrets committed.

---

## 14. Tests

Use xUnit, `WebApplicationFactory<Program>` and **Testcontainers.MsSql**. One container per test run, shared through a collection fixture. The factory replaces the connection string with the container's. Before each test, reset data to a known state: delete transfers and lines, then reset stock levels to known values, or use Respawn. Tests go through **HTTP**, so they also check status codes and ProblemDetails. Add `public partial class Program {}` so the factory can see `Program`.

Name each test as a sentence describing the behaviour. Each one asserts the response **and** the database state (stock quantities and transfer status). Test helpers like `CreateTransfer(...)`, `GetStock(branch, product)` and `SetStock(branch, product, qty)` keep the tests readable.

Required, most important first:
1. **Concurrent dispatch moves stock exactly once.** Create a transfer for 10. Fire 10 dispatch calls in parallel (`Task.WhenAll`). All return 200, the sender's stock went down by exactly 10, and the status is Dispatched.
2. **Sequential double dispatch is an idempotent no-op.** The second call returns 200, and stock is unchanged after it.
3. **Over-stock dispatch is rejected atomically.** Two lines: line 1 has enough, line 2 doesn't. The response is 409 and its `detail` names the product and the available quantity. **Both** stock levels are unchanged (proving the rollback), and the status is still Requested.
4. **Receive before dispatch is rejected.** 409, with the receiver's stock unchanged.
5. **Dispatch then receive moves stock from sender to receiver.** The sender goes down by Q, the receiver goes up by Q, and the status is Received. A second receive is a no-op and stock is unchanged.
6. **Concurrent receive adds stock exactly once.**
7. **Cancel.** Cancelling a Requested transfer gives Cancelled with no stock change. Cancelling a Dispatched transfer gives 409. Dispatching a Cancelled transfer gives 409.
8. **Create validation.** Same from/to → 400. Quantity 0 or negative → 400. Duplicate product lines → 400. No lines → 400. Unknown product → 404.
9. **Idempotent create.** Two POSTs with the same `Idempotency-Key`, including two fired in parallel, produce one transfer, and both responses carry the same id.
10. *(Phase 7)* **Partial receive** adds only the received quantity and records the shortfall. A received quantity greater than the sent quantity → 400.

---

## 15. Documentation

**README.md** for a reviewer who has never seen the code. Keep it short and scannable:
1. One-paragraph overview.
2. **Prerequisites:** .NET 10 SDK, Node 20+, Docker.
3. **Run it** in at most about 5 commands (compose up, run API, npm install, npm run dev, open the URL). Also the alternative of using an existing SQL Server by setting `ConnectionStrings__Default`.
4. **Run the tests** (Docker must be running).
5. **How double-counting and negative stock are prevented:** 5–10 lines explaining conditional updates, the transaction, CHECK constraints, idempotent replays and the idempotency key. This is what reviewers care about most, so make it easy to find.
6. API endpoint table.
7. **Assumptions:** the warehouse acts as a branch; no reservation at request time; in-transit stock belongs to neither branch; a repeated action returns 200 as a no-op; the list filter matches from or to; seeded rows exist for every branch × product; no auth; and anything else decided along the way.
8. **Trade-offs:** no execution-strategy retries; the idempotency key isn't bound to a body hash; no pagination beyond a 200-row cap; tests need Docker; HTTP in dev; and others that come up.
9. **With another day:** a stock movement ledger (an append-only history table, so stock is auditable and can be rebuilt), auth with per-branch permissions (only the sending branch dispatches, only the receiver receives), stock reservation on request, pagination, optimistic UI, a front-end test or two, a CI workflow, and the containerised API in compose.
10. Seed data table.
11. **Time spent:** leave the placeholder `TODO (human): ~X hours`. Don't guess it.

**AI_USAGE.md:** draft it:
- Tools: Claude Code, used for planning and analysing the brief, and for generating most of the code, tests and docs under human direction. The planning conversation and this prompt (`docs/BUILD_PROMPT.md`) were also AI-assisted. Say so plainly.
- "Where the AI was wrong or unhelpful": draft it **only** from real entries in `docs/AI_SESSION_LOG.md`, with what went wrong and how it was caught (failing test, build error, manual check). If there are no real entries, leave a clearly marked `TODO (human)`.
- "What I wrote or decided myself": leave a `TODO (human)` section with prompts listing the decisions the human made, such as accepting or changing the stack, the idempotency semantics, and scope cuts. **Don't write it in the human's voice as if it were fact.**

---

## 16. Final report

When you finish, reply with:
- What was built, phase by phase, with the commit list (`git log --oneline`).
- The test results: the actual `dotnet test` summary output.
- Anything skipped, unfinished or failing, stated plainly.
- The `TODO (human)` items the candidate must fill in before submitting (time spent, decisions, AI-wrong anecdote if not already real).
