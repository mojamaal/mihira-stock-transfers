using System.Net.Http.Json;
using MihiraStores.Api.Branches;

namespace MihiraStores.Api.Tests;

public sealed class StockViewTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Branch_stock_shows_on_hand_open_requests_and_incoming_transit()
    {
        await CreateTransfer(Colombo, Kandy, (Rice, 5));                     // requested out of Colombo
        var toGalle = await CreateTransfer(Colombo, Galle, (Rice, 3));       // dispatched: left Colombo
        await Act(toGalle, "dispatch");
        var fromWarehouse = await CreateTransfer(Warehouse, Colombo, (Rice, 7)); // in transit to Colombo
        await Act(fromWarehouse, "dispatch");
        var cancelled = await CreateTransfer(Colombo, Kandy, (Rice, 9));     // cancelled: claims nothing
        await Act(cancelled, "cancel");

        var stock = await Client.GetFromJsonAsync<List<StockItemDto>>($"/api/branches/{Colombo}/stock", Json);
        var rice = stock!.Single(s => s.ProductId == Rice);

        Assert.Equal((40 - 3, 5, 7), (rice.OnHand, rice.RequestedOut, rice.Incoming));
    }
}
