namespace MihiraStores.Api.Domain;

/// <summary>A stock-holding location. The warehouse is modelled as a branch with <see cref="IsWarehouse"/> set.</summary>
public class Branch
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsWarehouse { get; set; }
}
