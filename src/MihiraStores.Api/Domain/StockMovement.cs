namespace MihiraStores.Api.Domain;

/// <summary>
/// One change to a branch's stock, recorded in the same transaction as the change itself. The ledger is
/// append-only: rows are never updated or deleted, so for every branch and product the sum of
/// <see cref="Change"/> equals the stock level, and any figure can be traced back to what caused it.
/// </summary>
public class StockMovement
{
    public long Id { get; set; }

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public StockMovementKind Kind { get; set; }

    /// <summary>Signed: negative when stock left the branch.</summary>
    public int Change { get; set; }

    /// <summary>The branch's stock of this product straight after this movement.</summary>
    public int BalanceAfter { get; set; }

    public int? TransferId { get; set; }
    public Transfer? Transfer { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}

public enum StockMovementKind
{
    /// <summary>Starting stock when the system was set up.</summary>
    Opening,

    /// <summary>Stock left this branch when a transfer was dispatched.</summary>
    TransferOut,

    /// <summary>Stock arrived at this branch when a transfer was received.</summary>
    TransferIn,
}
