namespace MihiraStores.Api.Domain;

/// <summary>How many units of one product a branch holds. The database forbids negative quantities.</summary>
public class StockLevel
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }
}
