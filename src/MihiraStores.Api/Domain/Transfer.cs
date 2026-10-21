namespace MihiraStores.Api.Domain;

public class Transfer
{
    public int Id { get; set; }

    public int FromBranchId { get; set; }
    public Branch FromBranch { get; set; } = null!;

    public int ToBranchId { get; set; }
    public Branch ToBranch { get; set; } = null!;

    public TransferStatus Status { get; set; } = TransferStatus.Requested;

    /// <summary>Client-supplied key that makes "create" safe to retry (double-click, network retry).</summary>
    public Guid? IdempotencyKey { get; set; }

    /// <summary>SHA-256 of the request the key was first used with, so a key reused for a different request is refused.</summary>
    public string? IdempotencyRequestHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Optional note from the receiving branch, e.g. what happened to missing goods.</summary>
    public string? ReceiptNote { get; set; }

    public List<TransferLine> Lines { get; set; } = [];
}
