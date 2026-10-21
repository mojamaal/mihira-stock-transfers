using MihiraStores.Api.Data;
using MihiraStores.Api.Domain;
using MihiraStores.Api.Errors;
using MihiraStores.Api.Stock;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Transfers;

/// <summary>The outcome of a command. <see cref="Replayed"/> is true when the action had already happened and nothing changed.</summary>
public sealed record CommandResult(TransferDto Transfer, bool Replayed);

/// <summary>Owns every transfer rule. Nothing else changes a transfer's status, and stock only moves through here.</summary>
public sealed class TransferService(AppDbContext db, TransferQueries queries, StockMover stock)
{
    /// <summary>
    /// Creates a transfer in Requested status. Stock is NOT checked or reserved here: it leaves the sender at dispatch.
    /// A repeated idempotency key returns the original transfer instead of creating another.
    /// </summary>
    public async Task<CommandResult> CreateAsync(CreateTransferRequest request, Guid? idempotencyKey, CancellationToken ct)
    {
        if (idempotencyKey is Guid key && await FindIdByKeyAsync(key, ct) is int existingId)
        {
            return new(await queries.GetAsync(existingId, ct), Replayed: true);
        }

        var lines = await TransferValidation.ValidateCreateAsync(db, request, ct);

        var transfer = new Transfer
        {
            FromBranchId = request.FromBranchId!.Value,
            ToBranchId = request.ToBranchId!.Value,
            Status = TransferStatus.Requested,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTimeOffset.UtcNow,
            Lines = lines.Select(l => new TransferLine { ProductId = l.ProductId, Quantity = l.Quantity }).ToList(),
        };
        db.Transfers.Add(transfer);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (idempotencyKey is not null && ex.IsUniqueViolation())
        {
            // Two identical requests raced past the lookup above; the unique index let only one in.
            db.ChangeTracker.Clear();
            var winnerId = await FindIdByKeyAsync(idempotencyKey.Value, ct)
                ?? throw new InvalidOperationException("Idempotency key conflict without a stored transfer.", ex);
            return new(await queries.GetAsync(winnerId, ct), Replayed: true);
        }

        return new(await queries.GetAsync(transfer.Id, ct), Replayed: false);
    }

    // How the commands stay correct under double-clicks, retries and parallel requests:
    // each one claims its status change with a single conditional UPDATE ("... WHERE Status = <required>")
    // and uses the affected row count as the answer, instead of reading the status and then writing it.
    // Only one request can win that claim; the rest see 0 rows and are resolved by ResolveUnclaimedAsync.
    // Stock moves the same way (StockMover's guarded UPDATEs), inside the same transaction.
    // READ COMMITTED is enough: a conditional UPDATE takes an update lock on the row it checks, so a
    // competing request waits, then re-evaluates the WHERE clause against the committed value.

    public Task<CommandResult> DispatchAsync(int id, CancellationToken ct) => InTransactionAsync(async () =>
    {
        if (!await ClaimAsync(id, TransferAction.Dispatch, ct))
        {
            return await ResolveUnclaimedAsync(id, TransferAction.Dispatch, ct);
        }

        var transfer = await LoadWithLinesAsync(id, ct);
        var shortLines = new List<TransferLine>();
        foreach (var line in transfer.Lines)
        {
            if (!await stock.TryTakeAsync(transfer.FromBranchId, line.ProductId, line.Quantity, ct))
            {
                shortLines.Add(line);
            }
        }

        if (shortLines.Count > 0)
        {
            // Throwing rolls back the status claim and every line already taken: a dispatch is all or nothing.
            throw await InsufficientStockAsync(id, transfer.FromBranchId, shortLines, ct);
        }

        return new CommandResult(await queries.GetAsync(id, ct), Replayed: false);
    }, ct);

    /// <summary>
    /// Adds the stock to the receiving branch. The receiver may record less than was sent (e.g. damaged goods):
    /// only the received quantity is added, and the line keeps both figures so the shortfall stays on record.
    /// </summary>
    public Task<CommandResult> ReceiveAsync(int id, ReceiveTransferRequest? request, CancellationToken ct) => InTransactionAsync(async () =>
    {
        if (!await ClaimAsync(id, TransferAction.Receive, ct))
        {
            return await ResolveUnclaimedAsync(id, TransferAction.Receive, ct);
        }

        var transfer = await LoadWithLinesAsync(id, ct);
        var receivedByProduct = TransferValidation.ValidateReceive(transfer, request); // throwing leaves it Dispatched

        foreach (var line in transfer.Lines)
        {
            var received = receivedByProduct.GetValueOrDefault(line.ProductId, line.Quantity);
            await db.TransferLines
                .Where(l => l.Id == line.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.QuantityReceived, received), ct);
            await stock.AddAsync(transfer.ToBranchId, line.ProductId, received, ct);
        }

        return new CommandResult(await queries.GetAsync(id, ct), Replayed: false);
    }, ct);

    /// <summary>Cancels a transfer that has not been dispatched. Stock never moves on cancel.</summary>
    public Task<CommandResult> CancelAsync(int id, CancellationToken ct) => InTransactionAsync(async () =>
        await ClaimAsync(id, TransferAction.Cancel, ct)
            ? new CommandResult(await queries.GetAsync(id, ct), Replayed: false)
            : await ResolveUnclaimedAsync(id, TransferAction.Cancel, ct), ct);

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction under EF's retrying execution strategy, so a transient
    /// SQL failure (dropped connection, failover, deadlock victim) re-runs the whole unit instead of failing.
    /// Re-running is safe because every command is idempotent: if an earlier attempt actually committed,
    /// the retry's status claim finds nothing to claim and returns the transfer as a replay.
    /// Throwing from <paramref name="work"/> rolls everything back (the transaction is disposed uncommitted).
    /// </summary>
    private Task<CommandResult> InTransactionAsync(Func<Task<CommandResult>> work, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });

    /// <summary>Moves the transfer to the action's resulting status, but only if it is in the required status.</summary>
    private async Task<bool> ClaimAsync(int id, TransferAction action, CancellationToken ct)
    {
        var required = TransferWorkflow.RequiredStatus(action);
        var next = TransferWorkflow.ResultingStatus(action);
        var now = DateTimeOffset.UtcNow;
        var claimable = db.Transfers.Where(t => t.Id == id && t.Status == required);

        var claimed = action switch
        {
            TransferAction.Dispatch => await claimable.ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, next).SetProperty(t => t.DispatchedAt, now), ct),
            TransferAction.Receive => await claimable.ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, next).SetProperty(t => t.ReceivedAt, now), ct),
            TransferAction.Cancel => await claimable.ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, next).SetProperty(t => t.CancelledAt, now), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        return claimed == 1;
    }

    /// <summary>
    /// A command claimed nothing. Either the transfer is missing (404), the same action already happened
    /// (an idempotent replay: return the transfer unchanged, move no stock), or the action is not allowed (409).
    /// </summary>
    private async Task<CommandResult> ResolveUnclaimedAsync(int id, TransferAction action, CancellationToken ct)
    {
        var status = await db.Transfers.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => (TransferStatus?)t.Status)
            .SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound($"Transfer #{id} was not found.");

        if (status == TransferWorkflow.ResultingStatus(action))
        {
            return new CommandResult(await queries.GetAsync(id, ct), Replayed: true);
        }

        throw AppException.Conflict(TransferWorkflow.RejectionMessage(id, action, status));
    }

    /// <summary>Lines come back in ProductId order so concurrent transactions always lock stock rows in the same order.</summary>
    private Task<Transfer> LoadWithLinesAsync(int id, CancellationToken ct) =>
        db.Transfers.AsNoTracking()
            .Include(t => t.Lines.OrderBy(l => l.ProductId))
            .SingleAsync(t => t.Id == id, ct);

    private async Task<AppException> InsufficientStockAsync(
        int transferId, int branchId, List<TransferLine> shortLines, CancellationToken ct)
    {
        // Short lines were not changed, so these reads show the figures that made the dispatch fail.
        var productIds = shortLines.Select(l => l.ProductId).ToList();
        var branchName = await db.Branches.Where(b => b.Id == branchId).Select(b => b.Name).SingleAsync(ct);
        var available = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Sku,
                Quantity = db.StockLevels.Where(s => s.BranchId == branchId && s.ProductId == p.Id).Select(s => s.Quantity).FirstOrDefault(),
            })
            .ToDictionaryAsync(p => p.Id, ct);

        var details = shortLines.Select(l =>
        {
            var p = available[l.ProductId];
            return $"{p.Name} ({p.Sku}): {p.Quantity} available, {l.Quantity} needed";
        });

        return AppException.Conflict(
            $"Cannot dispatch transfer #{transferId}: not enough stock at {branchName}. {string.Join("; ", details)}.");
    }

    private Task<int?> FindIdByKeyAsync(Guid key, CancellationToken ct) =>
        db.Transfers.AsNoTracking()
            .Where(t => t.IdempotencyKey == key)
            .Select(t => (int?)t.Id)
            .SingleOrDefaultAsync(ct);
}
