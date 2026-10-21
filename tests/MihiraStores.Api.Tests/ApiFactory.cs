using MihiraStores.Api.Data;
using MihiraStores.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace MihiraStores.Api.Tests;

/// <summary>
/// Runs the real API in memory against a real SQL Server in Docker. The rules depend on transactions,
/// conditional UPDATEs and CHECK constraints, which an in-memory fake would not enforce.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public IReadOnlyDictionary<string, int> BranchIds { get; private set; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> ProductIds { get; private set; } = new Dictionary<string, int>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _sql.GetConnectionString());
    }

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        // Starting the server runs the app's own startup: migrations, then seeding.
        _ = Server;

        await using var db = CreateDbContext();
        BranchIds = await db.Branches.ToDictionaryAsync(b => b.Name, b => b.Id);
        ProductIds = await db.Products.ToDictionaryAsync(p => p.Sku, p => p.Id);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        return new AppDbContext(options);
    }

    /// <summary>Removes all transfers and puts stock back to the seeded figures, so every test starts from the same state.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.TransferLines.ExecuteDeleteAsync();
        await db.Transfers.ExecuteDeleteAsync();
        await db.StockLevels.ExecuteDeleteAsync(); // tests may delete rows, so rebuild them all

        db.StockLevels.AddRange(DbSeeder.StartingStock.Select(s => new StockLevel
        {
            BranchId = BranchIds[s.Key.Branch],
            ProductId = ProductIds[s.Key.Sku],
            Quantity = s.Value,
        }));
        await db.SaveChangesAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
