using MihiraStores.Api.Errors;
using Microsoft.AspNetCore.Mvc;

namespace MihiraStores.Api.Transfers;

/// <summary>Thin HTTP mapping only. Every rule lives in <see cref="TransferService"/>.</summary>
public static class TransferEndpoints
{
    /// <summary>Set on a response when the request repeated an action that had already happened, so nothing changed.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    public static void MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transfers").WithTags("Transfers");

        group.MapGet("/", (int? branchId, string? status, TransferQueries queries, CancellationToken ct) =>
            queries.ListAsync(branchId, status, ct));

        group.MapGet("/{id:int}", (int id, TransferQueries queries, CancellationToken ct) =>
            queries.GetAsync(id, ct));

        group.MapPost("/", async (
            CreateTransferRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            TransferService transfers,
            HttpContext http,
            CancellationToken ct) =>
        {
            var result = await transfers.CreateAsync(request, ParseIdempotencyKey(idempotencyKey), ct);
            return result.Replayed
                ? ToResult(http, result)
                : Results.Created($"/api/transfers/{result.Transfer.Id}", result.Transfer);
        });

        group.MapPost("/{id:int}/dispatch", async (int id, TransferService transfers, HttpContext http, CancellationToken ct) =>
            ToResult(http, await transfers.DispatchAsync(id, ct)));

        // The body is optional: without one, everything sent is received.
        group.MapPost("/{id:int}/receive", async (int id, ReceiveTransferRequest? request, TransferService transfers, HttpContext http, CancellationToken ct) =>
            ToResult(http, await transfers.ReceiveAsync(id, request, ct)));

        group.MapPost("/{id:int}/cancel", async (int id, TransferService transfers, HttpContext http, CancellationToken ct) =>
            ToResult(http, await transfers.CancelAsync(id, ct)));
    }

    private static IResult ToResult(HttpContext http, CommandResult result)
    {
        if (result.Replayed)
        {
            http.Response.Headers[ReplayedHeader] = "true";
        }

        return Results.Ok(result.Transfer);
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
