using MihiraStores.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Products;

public sealed record ProductDto(int Id, string Sku, string Name);

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/products").WithTags("Products")
            .MapGet("/", async (AppDbContext db, CancellationToken ct) =>
                await db.Products.AsNoTracking()
                    .OrderBy(p => p.Name)
                    .Select(p => new ProductDto(p.Id, p.Sku, p.Name))
                    .ToListAsync(ct));
    }
}
