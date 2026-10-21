using MihiraStores.Api.Data;
using MihiraStores.Api.Domain;
using MihiraStores.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Branches;

public sealed record BranchDto(int Id, string Name, bool IsWarehouse);

/// <param name="OnHand">Physically at the branch now.</param>
/// <param name="RequestedOut">Asked for by this branch's open (Requested) outgoing transfers. Not reserved: whichever is dispatched first gets the stock.</param>
/// <param name="Incoming">Dispatched to this branch and in transit; it is added when received.</param>
public sealed record StockItemDto(int ProductId, string Sku, string Name, int OnHand, int RequestedOut, int Incoming);

/// <param name="Counterparty">The other branch in the transfer that caused the movement, if any.</param>
public sealed record StockMovementDto(
    long Id,
    int ProductId,
    string Sku,
    string ProductName,
    StockMovementKind Kind,
    int Change,
    int BalanceAfter,
    int? TransferId,
    string? Counterparty,
    DateTimeOffset OccurredAt);

public static class BranchEndpoints
{
    private const int MaxMovements = 100;

    public static void MapBranchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/branches").WithTags("Branches");

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            await db.Branches.AsNoTracking()
                .OrderByDescending(b => b.IsWarehouse).ThenBy(b => b.Name)
                .Select(b => new BranchDto(b.Id, b.Name, b.IsWarehouse))
                .ToListAsync(ct));

        // Every product is listed, with 0 where the branch holds none, plus what is promised out and on its way in.
        group.MapGet("/{id:int}/stock", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Branches.AnyAsync(b => b.Id == id, ct))
            {
                throw AppException.NotFound($"Branch #{id} was not found.");
            }

            var requestedOut = db.Transfers.Where(t => t.FromBranchId == id && t.Status == TransferStatus.Requested).SelectMany(t => t.Lines);
            var incoming = db.Transfers.Where(t => t.ToBranchId == id && t.Status == TransferStatus.Dispatched).SelectMany(t => t.Lines);

            return await db.Products.AsNoTracking()
                .OrderBy(p => p.Name)
                .Select(p => new StockItemDto(
                    p.Id,
                    p.Sku,
                    p.Name,
                    db.StockLevels.Where(s => s.BranchId == id && s.ProductId == p.Id).Select(s => s.Quantity).FirstOrDefault(),
                    requestedOut.Where(l => l.ProductId == p.Id).Sum(l => (int?)l.Quantity) ?? 0,
                    incoming.Where(l => l.ProductId == p.Id).Sum(l => (int?)l.Quantity) ?? 0))
                .ToListAsync(ct);
        });

        // The ledger behind the figures: newest first, optionally for one product.
        group.MapGet("/{id:int}/movements", async (int id, int? productId, int? take, AppDbContext db, CancellationToken ct) =>
        {
            if (take is < 1 or > MaxMovements)
            {
                throw AppException.Validation(new Dictionary<string, string[]>
                {
                    ["take"] = [$"Take must be between 1 and {MaxMovements}."],
                });
            }

            if (!await db.Branches.AnyAsync(b => b.Id == id, ct))
            {
                throw AppException.NotFound($"Branch #{id} was not found.");
            }

            var movements = db.StockMovements.AsNoTracking().Where(m => m.BranchId == id);
            if (productId is int product)
            {
                movements = movements.Where(m => m.ProductId == product);
            }

            return await movements
                .OrderByDescending(m => m.Id)
                .Take(take ?? 20)
                .Select(m => new StockMovementDto(
                    m.Id,
                    m.ProductId,
                    m.Product.Sku,
                    m.Product.Name,
                    m.Kind,
                    m.Change,
                    m.BalanceAfter,
                    m.TransferId,
                    m.Transfer == null ? null
                        : m.Kind == StockMovementKind.TransferOut ? m.Transfer.ToBranch.Name : m.Transfer.FromBranch.Name,
                    m.OccurredAt))
                .ToListAsync(ct);
        });
    }
}
