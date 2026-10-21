namespace MihiraStores.Api.Domain;

public class TransferLine
{
    public int Id { get; set; }

    public int TransferId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Quantity requested, and taken from the sender on dispatch.</summary>
    public int Quantity { get; set; }

    /// <summary>Quantity that actually arrived. Null until the transfer is received.</summary>
    public int? QuantityReceived { get; set; }

    /// <summary>Why less arrived than was sent. Set exactly when <see cref="QuantityReceived"/> is below <see cref="Quantity"/>.</summary>
    public ShortfallReason? ShortfallReason { get; set; }
}

/// <summary>Why part of a transfer did not arrive. The shortfall is written off; it does not return to the sender.</summary>
public enum ShortfallReason
{
    Damaged,
    Missing,
    Other,
}
