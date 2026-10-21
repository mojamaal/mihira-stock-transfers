using MihiraStores.Api.Data;
using MihiraStores.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Stock;

/// <summary>
/// The only code that changes stock levels. Each change is a single guarded UPDATE, so the database decides
/// atomically whether it is allowed. Callers run it inside their transaction.
/// </summary>
public sealed class StockMover(AppDbContext db)
{
    /// <summary>Takes stock only if enough is on hand. Returns false, and changes nothing, when it is short.</summary>
    public async Task<bool> TryTakeAsync(int branchId, int productId, int quantity, CancellationToken ct) =>
        await db.StockLevels
            .Where(s => s.BranchId == branchId && s.ProductId == productId && s.Quantity >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - quantity), ct) == 1;

    public async Task AddAsync(int branchId, int productId, int quantity, CancellationToken ct)
    {
        if (quantity == 0 || await IncrementAsync(branchId, productId, quantity, ct))
        {
            return;
        }

        // No row yet for this branch and product: create it.
        try
        {
            db.StockLevels.Add(new StockLevel { BranchId = branchId, ProductId = productId, Quantity = quantity });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Safety net. On SQL Server at READ COMMITTED the UPDATE above keeps a key lock even when it finds
            // no row, so a concurrent receive waits for us and this path is not reached in practice. Under a
            // different isolation level or plan, though, two transactions could both find no row and both
            // insert; the loser lands here. The row exists now, so add to it. (EF rolled back to its
            // savepoint, so our transaction is still usable.)
            db.ChangeTracker.Clear();
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
}
