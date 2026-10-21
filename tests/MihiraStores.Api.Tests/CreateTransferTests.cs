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
        Assert.True(IsReplay(second));
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
    public async Task Reusing_an_idempotency_key_for_a_different_request_is_refused()
    {
        var key = Guid.NewGuid();
        var first = await PostCreate(new { fromBranchId = Colombo, toBranchId = Kandy, lines = new[] { new { productId = Rice, quantity = 5 } } }, key);

        var different = await PostCreate(new { fromBranchId = Colombo, toBranchId = Kandy, lines = new[] { new { productId = Rice, quantity = 50 } } }, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, different.StatusCode);
        Assert.Contains($"already used to create transfer #{(await ReadTransfer(first)).Id}", (await ReadProblem(different)).Detail);
        Assert.Equal(1, await TransferCount());
    }

    [Fact]
    public async Task The_same_lines_in_a_different_order_count_as_the_same_request()
    {
        var key = Guid.NewGuid();
        await PostCreate(new { fromBranchId = Colombo, toBranchId = Kandy, lines = new[] { new { productId = Rice, quantity = 5 }, new { productId = Tea, quantity = 2 } } }, key);

        var reordered = await PostCreate(new { fromBranchId = Colombo, toBranchId = Kandy, lines = new[] { new { productId = Tea, quantity = 2 }, new { productId = Rice, quantity = 5 } } }, key);

        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        Assert.True(IsReplay(reordered));
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
    public async Task List_filters_by_branch_direction_and_status()
    {
        var colomboToKandy = await CreateTransfer(Colombo, Kandy, (Rice, 1));
        var kandyToGalle = await CreateTransfer(Kandy, Galle, (Rice, 1));
        var warehouseToColombo = await CreateTransfer(Warehouse, Colombo, (Rice, 1));
        await Act(warehouseToColombo, "dispatch");

        var kandyAny = await List($"branchId={Kandy}");
        var kandyOutgoing = await List($"branchId={Kandy}&direction=outgoing");
        var kandyIncoming = await List($"branchId={Kandy}&direction=incoming");
        var dispatched = await List("status=Dispatched");

        Assert.Equal([kandyToGalle, colomboToKandy], kandyAny.Items.Select(t => t.Id));
        Assert.Equal([kandyToGalle], kandyOutgoing.Items.Select(t => t.Id));
        Assert.Equal([colomboToKandy], kandyIncoming.Items.Select(t => t.Id));
        Assert.Equal([warehouseToColombo], dispatched.Items.Select(t => t.Id));
    }

    [Fact]
    public async Task List_pages_newest_first_and_reports_totals_and_status_counts()
    {
        var ids = new List<int>();
        for (var i = 0; i < 25; i++) ids.Add(await CreateTransfer(Colombo, Kandy, (Rice, 1)));
        await Act(ids[0], "cancel");

        var page2 = await List("page=2&pageSize=10");

        Assert.Equal(ids.AsEnumerable().Reverse().Skip(10).Take(10), page2.Items.Select(t => t.Id));
        Assert.Equal((25, 3), (page2.TotalCount, page2.TotalPages));
        Assert.Equal(24, page2.StatusCounts[TransferStatus.Requested]);
        Assert.Equal(1, page2.StatusCounts[TransferStatus.Cancelled]);
        Assert.Equal(0, page2.StatusCounts[TransferStatus.Received]);
    }

    [Theory]
    [InlineData("direction=incoming")] // a direction needs a branch
    [InlineData("pageSize=500")]
    [InlineData("page=0")]
    [InlineData("status=Lost")]
    public async Task List_rejects_invalid_filters(string query)
    {
        var response = await Client.GetAsync($"/api/transfers?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Transfers.TransferPage> List(string query) =>
        (await Client.GetFromJsonAsync<Transfers.TransferPage>($"/api/transfers?{query}", Json))!;

    private static async Task<IDictionary<string, string[]>> ReadValidationErrors(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json);
        return problem!.Errors;
    }
}
