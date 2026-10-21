using MihiraStores.Api.Domain;

namespace MihiraStores.Api.Transfers;

// Nullable fields let the server tell "missing" apart from "zero" and report it clearly.
public sealed record CreateTransferRequest(int? FromBranchId, int? ToBranchId, List<CreateTransferLine>? Lines);

public sealed record CreateTransferLine(int? ProductId, int? Quantity);

/// <summary>Optional body for receive. Products left out are treated as fully received.</summary>
public sealed record ReceiveTransferRequest(List<ReceiveLine>? Lines);

public sealed record ReceiveLine(int? ProductId, int? QuantityReceived);

public sealed record BranchRef(int Id, string Name);

public sealed record TransferLineDto(int ProductId, string Sku, string ProductName, int Quantity, int? QuantityReceived);

public sealed record TransferDto(
    int Id,
    BranchRef FromBranch,
    BranchRef ToBranch,
    TransferStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<TransferLineDto> Lines,
    IReadOnlyList<string> AllowedActions)
{
    public static TransferDto From(Transfer t) => new(
        t.Id,
        new BranchRef(t.FromBranch.Id, t.FromBranch.Name),
        new BranchRef(t.ToBranch.Id, t.ToBranch.Name),
        t.Status,
        t.CreatedAt,
        t.DispatchedAt,
        t.ReceivedAt,
        t.CancelledAt,
        t.Lines
            .OrderBy(l => l.Product.Name)
            .Select(l => new TransferLineDto(l.ProductId, l.Product.Sku, l.Product.Name, l.Quantity, l.QuantityReceived))
            .ToList(),
        TransferWorkflow.AllowedActions(t.Status).Select(a => a.ToString().ToLowerInvariant()).ToList());
}
