using System.Net.Http.Json;
using MihiraStores.Api.Branches;
using MihiraStores.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Tests;

public sealed class StockLedgerTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task The_ledger_always_reconciles_with_stock_on_hand()
    {
        // A mix of everything that can happen to stock, including refusals and races that must leave no trace.
        var full = await CreateTransfer(Colombo, Kandy, (Rice, 10), (Tea, 4));
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Act(full, "dispatch")));
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Act(full, "receive")));

        var partial = await CreateTransfer(Warehouse, Galle, (CoconutOil, 20));
        await Act(partial, "dispatch");
        await Client.PostAsJsonAsync($"/api/transfers/{partial}/receive",
            new { lines = new[] { new { productId = CoconutOil, quantityReceived = 17, reason = "Missing" } } });

        var refused = await CreateTransfer(Galle, Colombo, (Rice, 2), (CoconutOil, 99));
        await Act(refused, "dispatch");
        var cancelled = await CreateTransfer(Kandy, Galle, (Rice, 1));
        await Act(cancelled, "cancel");

        await using var db = Factory.CreateDbContext();
        var stock = await db.StockLevels.ToListAsync();
        var ledger = await db.StockMovements.ToListAsync();

        Assert.All(stock, s =>
        {
            var movements = ledger.Where(m => m.BranchId == s.BranchId && m.ProductId == s.ProductId).OrderBy(m => m.Id).ToList();
            Assert.Equal(s.Quantity, movements.Sum(m => m.Change));
            Assert.Equal(s.Quantity, movements.LastOrDefault()?.BalanceAfter ?? 0);
        });

        // Refused and cancelled transfers moved nothing, so they left nothing in the ledger.
        Assert.DoesNotContain(ledger, m => m.TransferId == refused || m.TransferId == cancelled);
        // The 3 missing bottles are a write-off: 20 left the warehouse, 17 arrived in Galle.
        Assert.Equal(-20, ledger.Where(m => m.TransferId == partial && m.BranchId == Warehouse).Sum(m => m.Change));
        Assert.Equal(17, ledger.Where(m => m.TransferId == partial && m.BranchId == Galle).Sum(m => m.Change));
    }

    [Fact]
    public async Task Movements_explain_each_figure_with_its_cause_and_running_balance()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 10));
        await Act(id, "dispatch");

        var colombo = await Client.GetFromJsonAsync<List<StockMovementDto>>($"/api/branches/{Colombo}/movements?productId={Rice}", Json);

        Assert.Collection(colombo!,
            latest => Assert.Equal((StockMovementKind.TransferOut, -10, 30, (int?)id, "Kandy"),
                (latest.Kind, latest.Change, latest.BalanceAfter, latest.TransferId, latest.Counterparty)),
            opening => Assert.Equal((StockMovementKind.Opening, 40, 40), (opening.Kind, opening.Change, opening.BalanceAfter)));
    }
}
