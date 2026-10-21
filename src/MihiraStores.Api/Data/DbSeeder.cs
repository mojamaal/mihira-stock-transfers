using MihiraStores.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Data;

/// <summary>Creates the warehouse, three branches, five products and starting stock on an empty database.</summary>
public static class DbSeeder
{
    // Starting stock per branch, in the same order as Products below. Every figure starts above zero; Galle is
    // deliberately low (2 Rice, 3 Coconut Oil) so the "not enough stock" rule is easy to see.
    //                                         Rice  Dhal  Oil  Tea  Sugar
    private static readonly (string Branch, bool IsWarehouse, int[] Stock)[] Branches =
    [
        ("Central Warehouse", true, [500, 400, 300, 250, 450]),
        ("Colombo", false, [40, 25, 12, 30, 20]),
        ("Kandy", false, [15, 8, 6, 18, 10]),
        ("Galle", false, [2, 10, 3, 6, 4]),
    ];

    private static readonly (string Sku, string Name)[] Products =
    [
        ("RICE-5KG", "Rice 5kg"),
        ("DHAL-1KG", "Red Dhal 1kg"),
        ("COCO-OIL-1L", "Coconut Oil 1L"),
        ("TEA-400G", "Ceylon Tea 400g"),
        ("SUGAR-1KG", "White Sugar 1kg"),
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Branches.AnyAsync(ct))
        {
            return;
        }

        var products = Products.Select(p => new Product { Sku = p.Sku, Name = p.Name }).ToList();
        db.Products.AddRange(products);

        foreach (var (name, isWarehouse, stock) in Branches)
        {
            var branch = new Branch { Name = name, IsWarehouse = isWarehouse };
            db.Branches.Add(branch);

            // Every branch gets a row for every product (zero included), so stock is always explicit.
            for (var i = 0; i < products.Count; i++)
            {
                AddOpeningStock(db, branch, products[i], stock[i]);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds a stock level and, when it isn't zero, the matching Opening entry in the ledger.</summary>
    public static void AddOpeningStock(AppDbContext db, Branch branch, Product product, int quantity)
    {
        db.StockLevels.Add(new StockLevel { Branch = branch, Product = product, Quantity = quantity });
        if (quantity != 0)
        {
            db.StockMovements.Add(new StockMovement
            {
                Branch = branch,
                Product = product,
                Kind = StockMovementKind.Opening,
                Change = quantity,
                BalanceAfter = quantity,
                OccurredAt = DateTimeOffset.UtcNow,
            });
        }
    }

    /// <summary>The seeded starting figures keyed by (branch name, SKU). The tests use this to reset stock between runs.</summary>
    public static IReadOnlyDictionary<(string Branch, string Sku), int> StartingStock =>
        Branches.SelectMany(b => Products.Select((p, i) => (Key: (b.Branch, p.Sku), Qty: b.Stock[i])))
            .ToDictionary(x => x.Key, x => x.Qty);
}
