using System.Net;
using System.Net.Http.Json;
using MihiraStores.Api.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MihiraStores.Api.Tests;

public sealed class CreateTransferTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Creating_a_transfer_does_not_move_stock()
    {
        var response = await PostCreate(new { fromBranchId = Colombo, toBranchId = Kandy, lines = new[] { new { productId = Rice, quantity = 5 } } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await ReadTransfer(response);
        Assert.Equal(TransferStatus.Requested, transfer.Status);
        Assert.Equal($"/api/transfers/{transfer.Id}", response.Headers.Location?.ToString());
        Assert.Equal(40, await StockOf(Colombo, Rice));
    }

    [Fact]
    public async Task Requesting_more_than_is_in_stock_is_allowed_because_stock_is_checked_at_dispatch()
    {
        var response = await PostCreate(new { fromBranchId = Galle, toBranchId = Colombo, lines = new[] { new { productId = CoconutOil, quantity = 5 } } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Same_from_and_to_branch_is_rejected()
    {
        var response = await PostCreate(new { fromBranchId = Colombo, toBranchId = Colombo, lines = new[] { new { productId = Rice, quantity = 1 } } });

        var errors = await ReadValidationErrors(response);
        Assert.Contains("From and to branches must be different.", errors["toBranchId"]);
        Assert.Equal(0, await TransferCount());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Quantity_must_be_more_than_zero(int quantity)
    {
        var response = await PostCreate(new { fromBranchId = Colombo, toBranchId = Kandy, lines = new[] { new { productId = Rice, quantity } } });

        var errors = await ReadValidationErrors(response);
        Assert.Contains("Quantity must be more than zero.", errors["lines[0].quantity"]);
    }

    [Fact]
    public async Task A_transfer_needs_at_least_one_line()
    {
        var response = await PostCreate(new { fromBranchId = Colombo, toBranchId = Kandy, lines = Array.Empty<object>() });

        var errors = await ReadValidationErrors(response);
        Assert.Contains("Add at least one product.", errors["lines"]);
    }

    [Fact]
    public async Task The_same_product_cannot_appear_on_two_lines()
    {
        var response = await PostCreate(new
        {
            fromBranchId = Colombo,
            toBranchId = Kandy,
            lines = new[] { new { productId = Rice, quantity = 1 }, new { productId = Rice, quantity = 2 } },
        });

        var errors = await ReadValidationErrors(response);
        Assert.Contains("Product RICE-5KG appears more than once; combine the lines.", errors["lines"]);
    }

    [Fact]
    public async Task All_validation_problems_are_reported_together()
    {
        var response = await PostCreate(new { fromBranchId = Colombo, toBranchId = Colombo, lines = new[] { new { productId = Rice, quantity = 0 } } });

        var errors = await ReadValidationErrors(response);
        Assert.True(errors.ContainsKey("toBranchId"));
        Assert.True(errors.ContainsKey("lines[0].quantity"));
    }

    [Fact]
    public async Task Unknown_product_returns_404()
    {
        var response = await PostCreate(new { fromBranchId = Colombo, toBranchId = Kandy, lines = new[] { new { productId = 999_999, quantity = 1 } } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Product #999999 was not found.", (await ReadProblem(response)).Detail);
    }

    [Fact]
    public async Task Repeating_a_create_with_the_same_idempotency_key_returns_the_original_transfer()
    {
        var key = Guid.NewGuid();
        var body = new { fromBranchId = Colombo, toBranchId = Kandy, lines = new[] { new { productId = Rice, quantity = 5 } } };

        var first = await PostCreate(body, key);
        var second = await PostCreate(body, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await ReadTransfer(first)).Id, (await ReadTransfer(second)).Id);
        Assert.Equal(1, await TransferCount());
    }

    [Fact]
    public async Task Parallel_creates_with_the_same_idempotency_key_create_one_transfer()
    {
        var key = Guid.NewGuid();
        var body = new { fromBranchId = Colombo, toBranchId = Kandy, lines = new[] { new { productId = Rice, quantity = 5 } } };

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostCreate(body, key)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"Unexpected {(int)r.StatusCode}"));
        var ids = await Task.WhenAll(responses.Select(ReadTransfer));
        Assert.Single(ids.Select(t => t.Id).Distinct());
        Assert.Equal(1, await TransferCount());
    }

    [Fact]
    public async Task A_malformed_idempotency_key_is_rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(new { fromBranchId = Colombo, toBranchId = Kandy, lines = new[] { new { productId = Rice, quantity = 1 } } }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "not-a-guid");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await TransferCount());
    }

    [Fact]
    public async Task List_filters_by_branch_on_either_side_and_by_status()
    {
        var colomboToKandy = await CreateTransfer(Colombo, Kandy, (Rice, 1));
        var kandyToGalle = await CreateTransfer(Kandy, Galle, (Rice, 1));
        var warehouseToColombo = await CreateTransfer(Warehouse, Colombo, (Rice, 1));
        await Act(warehouseToColombo, "dispatch");

        var kandy = await Client.GetFromJsonAsync<List<Transfers.TransferDto>>($"/api/transfers?branchId={Kandy}", Json);
        var dispatched = await Client.GetFromJsonAsync<List<Transfers.TransferDto>>("/api/transfers?status=Dispatched", Json);

        Assert.Equal([kandyToGalle, colomboToKandy], kandy!.Select(t => t.Id));
        Assert.Equal([warehouseToColombo], dispatched!.Select(t => t.Id));
    }

    private static async Task<IDictionary<string, string[]>> ReadValidationErrors(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        return problem!.Errors;
    }
}
