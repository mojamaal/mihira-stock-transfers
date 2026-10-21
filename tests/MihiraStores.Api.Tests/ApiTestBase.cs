using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MihiraStores.Api.Domain;
using MihiraStores.Api.Transfers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Tests;

/// <summary>Shared helpers. Each test starts from the seeded stock with no transfers.</summary>
[Collection(ApiCollection.Name)]
public abstract class ApiTestBase(ApiFactory factory) : IAsyncLifetime
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected ApiFactory Factory { get; } = factory;
    protected HttpClient Client { get; } = factory.CreateClient();

    // Seeded stock used by the tests (see DbSeeder): Colombo has 40 Rice and 30 Tea; Galle has 2 Rice and 0 Coconut Oil.
    protected int Warehouse => Factory.BranchIds["Central Warehouse"];
    protected int Colombo => Factory.BranchIds["Colombo"];
    protected int Kandy => Factory.BranchIds["Kandy"];
    protected int Galle => Factory.BranchIds["Galle"];
    protected int Rice => Factory.ProductIds["RICE-5KG"];
    protected int Tea => Factory.ProductIds["TEA-400G"];
    protected int CoconutOil => Factory.ProductIds["COCO-OIL-1L"];

    public Task InitializeAsync() => Factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected Task<HttpResponseMessage> PostCreate(object body, Guid? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers") { Content = JsonContent.Create(body) };
        if (idempotencyKey is Guid key)
        {
            request.Headers.Add("Idempotency-Key", key.ToString());
        }

        return Client.SendAsync(request);
    }

    protected async Task<int> CreateTransfer(int from, int to, params (int ProductId, int Quantity)[] lines)
    {
        var response = await PostCreate(new
        {
            fromBranchId = from,
            toBranchId = to,
            lines = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity }),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadTransfer(response)).Id;
    }

    protected Task<HttpResponseMessage> Act(int transferId, string action) =>
        Client.PostAsync($"/api/transfers/{transferId}/{action}", content: null);

    protected static async Task<TransferDto> ReadTransfer(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<TransferDto>(Json))!;

    protected static async Task<ProblemDetails> ReadProblem(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json))!;

    protected async Task<int> StockOf(int branchId, int productId)
    {
        await using var db = Factory.CreateDbContext();
        return await db.StockLevels
            .Where(s => s.BranchId == branchId && s.ProductId == productId)
            .Select(s => s.Quantity)
            .SingleAsync();
    }

    protected async Task SetStock(int branchId, int productId, int quantity)
    {
        await using var db = Factory.CreateDbContext();
        await db.StockLevels
            .Where(s => s.BranchId == branchId && s.ProductId == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, quantity));
    }

    protected async Task<TransferStatus> StatusOf(int transferId)
    {
        await using var db = Factory.CreateDbContext();
        return await db.Transfers.Where(t => t.Id == transferId).Select(t => t.Status).SingleAsync();
    }

    protected async Task<int> TransferCount()
    {
        await using var db = Factory.CreateDbContext();
        return await db.Transfers.CountAsync();
    }
}
