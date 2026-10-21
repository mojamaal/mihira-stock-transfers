using System.Net;
using System.Net.Http.Json;
using MihiraStores.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Tests;

public sealed class TransferLifecycleTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Parallel_dispatches_of_the_same_transfer_move_stock_exactly_once()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 10));

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Act(id, "dispatch")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(40 - 10, await StockOf(Colombo, Rice));
        Assert.Equal(TransferStatus.Dispatched, await StatusOf(id));
    }

    [Fact]
    public async Task Dispatching_twice_is_a_no_op_the_second_time()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 10));

        var first = await Act(id, "dispatch");
        var second = await Act(id, "dispatch");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(IsReplay(first));
        Assert.True(IsReplay(second)); // tells the client nothing changed this time
        Assert.Equal(TransferStatus.Dispatched, (await ReadTransfer(second)).Status);
        Assert.Equal(30, await StockOf(Colombo, Rice));
    }

    [Fact]
    public async Task Exactly_one_of_many_parallel_dispatches_is_reported_as_the_one_that_moved_stock()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 10));

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Act(id, "dispatch")));

        Assert.Single(responses, r => !IsReplay(r));
    }

    [Fact]
    public async Task Parallel_receives_into_a_missing_stock_row_create_it_once_and_add_every_amount()
    {
        // Kandy has no Coconut Oil row at all, so the first receive creates it and the rest add to it.
        // (The exact insert race is forced deterministically in StockMoverTests.)
        await DeleteStockRow(Kandy, CoconutOil);
        var transfers = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            var id = await CreateTransfer(Warehouse, Kandy, (CoconutOil, 1));
            await Act(id, "dispatch");
            transfers.Add(id);
        }

        var responses = await Task.WhenAll(transfers.Select(id => Act(id, "receive")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(12, await StockOf(Kandy, CoconutOil));
    }

    [Fact]
    public async Task Dispatch_that_would_make_stock_negative_is_rejected_and_nothing_moves()
    {
        // Rice has enough (2 of 2); Coconut Oil does not (0 of 5). The Rice line must be rolled back too.
        var id = await CreateTransfer(Galle, Colombo, (Rice, 2), (CoconutOil, 5));

        var response = await Act(id, "dispatch");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadProblem(response);
        Assert.Contains("not enough stock at Galle", problem.Detail);
        Assert.Contains("Coconut Oil 1L (COCO-OIL-1L): 0 available, 5 needed", problem.Detail);
        Assert.DoesNotContain("Rice", problem.Detail);

        Assert.Equal(2, await StockOf(Galle, Rice));
        Assert.Equal(0, await StockOf(Galle, CoconutOil));
        Assert.Equal(TransferStatus.Requested, await StatusOf(id));
    }

    [Fact]
    public async Task Dispatch_can_take_a_branch_down_to_exactly_zero()
    {
        var id = await CreateTransfer(Galle, Colombo, (Rice, 2));

        var response = await Act(id, "dispatch");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await StockOf(Galle, Rice));
    }

    [Fact]
    public async Task Two_transfers_competing_for_the_same_stock_cannot_both_be_dispatched()
    {
        await SetStock(Colombo, Tea, 10);
        var toKandy = await CreateTransfer(Colombo, Kandy, (Tea, 7));
        var toGalle = await CreateTransfer(Colombo, Galle, (Tea, 7));

        var responses = await Task.WhenAll(Act(toKandy, "dispatch"), Act(toGalle, "dispatch"));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(3, await StockOf(Colombo, Tea));
    }

    [Fact]
    public async Task Receive_before_dispatch_is_rejected()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 5));

        var response = await Act(id, "receive");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"Cannot receive transfer #{id}: it is Requested. Only Dispatched transfers can be received.",
            (await ReadProblem(response)).Detail);
        Assert.Equal(15, await StockOf(Kandy, Rice));
        Assert.Equal(TransferStatus.Requested, await StatusOf(id));
    }

    [Fact]
    public async Task Dispatch_then_receive_moves_stock_from_sender_to_receiver_once()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 5), (Tea, 3));

        await Act(id, "dispatch");
        Assert.Equal(15, await StockOf(Kandy, Rice)); // in transit: not at the receiver yet

        var received = await Act(id, "receive");
        var again = await Act(id, "receive");

        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(35, await StockOf(Colombo, Rice));
        Assert.Equal(27, await StockOf(Colombo, Tea));
        Assert.Equal(20, await StockOf(Kandy, Rice));
        Assert.Equal(21, await StockOf(Kandy, Tea));

        var transfer = await ReadTransfer(again);
        Assert.Equal(TransferStatus.Received, transfer.Status);
        Assert.All(transfer.Lines, l => Assert.Equal(l.Quantity, l.QuantityReceived));
        Assert.Empty(transfer.AllowedActions);
    }

    [Fact]
    public async Task Parallel_receives_of_the_same_transfer_add_stock_exactly_once()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 5));
        await Act(id, "dispatch");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Act(id, "receive")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(15 + 5, await StockOf(Kandy, Rice));
    }

    [Fact]
    public async Task Partial_receive_adds_only_what_arrived_and_records_the_shortfall()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 10), (Tea, 4));
        await Act(id, "dispatch");

        // 3 bags of rice arrived damaged; tea is left out of the body, so it counts as fully received.
        var response = await Client.PostAsJsonAsync($"/api/transfers/{id}/receive",
            new { lines = new[] { new { productId = Rice, quantityReceived = 7, reason = "Damaged" } }, note = "Bags split in the van" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var transfer = await ReadTransfer(response);
        Assert.Equal(TransferStatus.Received, transfer.Status);
        Assert.Equal("Bags split in the van", transfer.ReceiptNote);
        var rice = transfer.Lines.Single(l => l.ProductId == Rice);
        Assert.Equal((10, 7, ShortfallReason.Damaged), (rice.Quantity, rice.QuantityReceived, rice.ShortfallReason));
        var tea = transfer.Lines.Single(l => l.ProductId == Tea);
        Assert.Equal((4, (ShortfallReason?)null), (tea.QuantityReceived, tea.ShortfallReason));

        Assert.Equal(15 + 7, await StockOf(Kandy, Rice));
        Assert.Equal(18 + 4, await StockOf(Kandy, Tea));
        Assert.Equal(40 - 10, await StockOf(Colombo, Rice)); // the sender still lost all 10
    }

    [Fact]
    public async Task A_short_receipt_must_say_why()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 10));
        await Act(id, "dispatch");

        var noReason = await Client.PostAsJsonAsync($"/api/transfers/{id}/receive",
            new { lines = new[] { new { productId = Rice, quantityReceived = 7 } } });
        var otherWithoutNote = await Client.PostAsJsonAsync($"/api/transfers/{id}/receive",
            new { lines = new[] { new { productId = Rice, quantityReceived = 7, reason = "Other" } } });

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Contains("Say why 3 are short", (await ReadProblem(noReason)).Detail);
        Assert.Equal(HttpStatusCode.BadRequest, otherWithoutNote.StatusCode);
        Assert.Equal(TransferStatus.Dispatched, await StatusOf(id));
        Assert.Equal(15, await StockOf(Kandy, Rice));
    }

    [Fact]
    public async Task Receiving_more_than_was_sent_is_rejected_and_the_transfer_stays_in_transit()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 5));
        await Act(id, "dispatch");

        var response = await Client.PostAsJsonAsync($"/api/transfers/{id}/receive",
            new { lines = new[] { new { productId = Rice, quantityReceived = 6 } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TransferStatus.Dispatched, await StatusOf(id));
        Assert.Equal(15, await StockOf(Kandy, Rice));
    }

    [Fact]
    public async Task Cancelling_a_requested_transfer_moves_no_stock_and_blocks_dispatch()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 5));

        var cancelled = await Act(id, "cancel");
        var dispatch = await Act(id, "dispatch");

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(TransferStatus.Cancelled, (await ReadTransfer(cancelled)).Status);
        Assert.Equal(HttpStatusCode.Conflict, dispatch.StatusCode);
        Assert.Equal(40, await StockOf(Colombo, Rice));
        Assert.Equal(TransferStatus.Cancelled, await StatusOf(id));
    }

    [Fact]
    public async Task A_dispatched_transfer_cannot_be_cancelled()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 5));
        await Act(id, "dispatch");

        var response = await Act(id, "cancel");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TransferStatus.Dispatched, await StatusOf(id));
        Assert.Equal(35, await StockOf(Colombo, Rice));
    }

    [Fact]
    public async Task Allowed_actions_follow_the_status()
    {
        var id = await CreateTransfer(Colombo, Kandy, (Rice, 1));
        var requested = await ReadTransfer(await Client.GetAsync($"/api/transfers/{id}"));
        var dispatched = await ReadTransfer(await Act(id, "dispatch"));

        Assert.Equal(["dispatch", "cancel"], requested.AllowedActions);
        Assert.Equal(["receive"], dispatched.AllowedActions);
    }

    [Fact]
    public async Task Actions_on_an_unknown_transfer_return_404()
    {
        var response = await Act(999_999, "dispatch");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_database_itself_refuses_negative_stock()
    {
        await using var db = Factory.CreateDbContext();

        var write = () => db.StockLevels
            .Where(s => s.BranchId == Galle && s.ProductId == Rice)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, -1));

        await Assert.ThrowsAnyAsync<Exception>(write);
        Assert.Equal(2, await StockOf(Galle, Rice));
    }
}
