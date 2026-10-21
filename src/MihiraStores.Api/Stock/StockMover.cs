using MihiraStores.Api.Data;
using MihiraStores.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Stock;

/// <summary>
/// The only code that changes stock levels. Each change is a single guarded UPDATE, so the database decides
/// atomically whether it is allowed, and each one is written to the stock movement ledger. Callers run it
/// inside their transaction and save the staged ledger rows before committing.
/// </summary>
public sealed class StockMover(AppDbContext db)
{
    /// <summary>Takes stock only if enough is on hand. Returns false, and changes nothing, when it is short.</summary>
    public async Task<bool> TryTakeAsync(int branchId, int productId, int quantity, int transferId, CancellationToken ct)
    {
        var taken = await db.StockLevels
            .Where(s => s.BranchId == branchId && s.ProductId == productId && s.Quantity >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - quantity), ct) == 1;

        if (taken)
        {
            await RecordAsync(branchId, productId, -quantity, StockMovementKind.TransferOut, transferId, ct);
        }

        return taken;
    }

    public async Task AddAsync(int branchId, int productId, int quantity, int transferId, CancellationToken ct)
    {
        if (quantity == 0)
        {
            return;
        }

        if (!await IncrementAsync(branchId, productId, quantity, ct))
        {
            await CreateRowAsync(branchId, productId, quantity, ct);
        }

        await RecordAsync(branchId, productId, quantity, StockMovementKind.TransferIn, transferId, ct);
    }

    private async Task CreateRowAsync(int branchId, int productId, int quantity, CancellationToken ct)
    {
        var row = new StockLevel { BranchId = branchId, ProductId = productId, Quantity = quantity };
        try
        {
            db.StockLevels.Add(row);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Safety net. On SQL Server at READ COMMITTED the UPDATE above keeps a key lock even when it finds
            // no row, so a concurrent receive waits for us and this path is not reached in practice. Under a
            // different isolation level or plan, though, two transactions could both find no row and both
            // insert; the loser lands here. The row exists now, so add to it. (EF rolled back to its
            // savepoint, so our transaction is still usable.) Only our row is dropped: ledger rows staged
            // earlier in this transaction must survive.
            db.Entry(row).State = EntityState.Detached;
            if (!await IncrementAsync(branchId, productId, quantity, ct))
            {
                throw new InvalidOperationException($"Stock row for branch {branchId}, product {productId} vanished during receive.", ex);
            }
        }
    }

    private async Task<bool> IncrementAsync(int branchId, int productId, int quantity, CancellationToken ct) =>
        await db.StockLevels
            .Where(s => s.BranchId == branchId && s.ProductId == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity + quantity), ct) == 1;

    /// <summary>
    /// Stages a ledger row. The balance is read straight after our own UPDATE, while this transaction still holds
    /// the row's lock, so no other change can slip in between and the running balance is exact.
    /// </summary>
    private async Task RecordAsync(int branchId, int productId, int change, StockMovementKind kind, int transferId, CancellationToken ct)
    {
        var balance = await db.StockLevels
            .Where(s => s.BranchId == branchId && s.ProductId == productId)
            .Select(s => s.Quantity)
            .SingleAsync(ct);

        db.StockMovements.Add(new StockMovement
        {
            BranchId = branchId,
            ProductId = productId,
            Kind = kind,
            Change = change,
            BalanceAfter = balance,
            TransferId = transferId,
            OccurredAt = DateTimeOffset.UtcNow,
        });
    }
}
