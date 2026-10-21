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
    }
}
