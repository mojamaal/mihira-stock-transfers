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
}
