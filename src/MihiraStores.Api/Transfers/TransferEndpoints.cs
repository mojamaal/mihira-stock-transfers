using MihiraStores.Api.Errors;
using Microsoft.AspNetCore.Mvc;

namespace MihiraStores.Api.Transfers;

/// <summary>Thin HTTP mapping only. Every rule lives in <see cref="TransferService"/>.</summary>
public static class TransferEndpoints
{
    public static void MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transfers").WithTags("Transfers");

        group.MapGet("/", (int? branchId, string? status, TransferService transfers, CancellationToken ct) =>
            transfers.ListAsync(branchId, status, ct));

        group.MapGet("/{id:int}", (int id, TransferService transfers, CancellationToken ct) =>
            transfers.GetAsync(id, ct));

        group.MapPost("/", async (
            CreateTransferRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            TransferService transfers,
            CancellationToken ct) =>
        {
            var (transfer, created) = await transfers.CreateAsync(request, ParseIdempotencyKey(idempotencyKey), ct);
            return created
                ? Results.Created($"/api/transfers/{transfer.Id}", transfer)
                : Results.Ok(transfer);
        });

        group.MapPost("/{id:int}/dispatch", (int id, TransferService transfers, CancellationToken ct) =>
            transfers.DispatchAsync(id, ct));

        group.MapPost("/{id:int}/receive", (int id, TransferService transfers, CancellationToken ct) =>
            transfers.ReceiveAsync(id, ct));

        group.MapPost("/{id:int}/cancel", (int id, TransferService transfers, CancellationToken ct) =>
            transfers.CancelAsync(id, ct));
    }

    private static Guid? ParseIdempotencyKey(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        return Guid.TryParse(header, out var key)
            ? key
            : throw AppException.Validation(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["The Idempotency-Key header must be a GUID."],
            });
    }
}
