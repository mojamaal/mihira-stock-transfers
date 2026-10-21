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

public static class BranchEndpoints
{
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
    }
}
