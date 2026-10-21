using MihiraStores.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<TransferLine> TransferLines => Set<TransferLine>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Branch>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(100);
            b.HasIndex(x => x.Name).IsUnique();
        });

        model.Entity<Product>(p =>
        {
            p.Property(x => x.Sku).HasMaxLength(32);
            p.Property(x => x.Name).HasMaxLength(200);
            p.HasIndex(x => x.Sku).IsUnique();
        });

        model.Entity<StockLevel>(s =>
        {
            s.HasKey(x => new { x.BranchId, x.ProductId });
            // Last line of defence: even buggy code cannot make stock negative.
            s.ToTable(t => t.HasCheckConstraint("CK_StockLevels_Quantity_NonNegative", "[Quantity] >= 0"));
            s.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            s.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Transfer>(t =>
        {
            t.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            t.Property(x => x.IdempotencyRequestHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            t.ToTable(tb => tb.HasCheckConstraint("CK_Transfers_DifferentBranches", "[FromBranchId] <> [ToBranchId]"));
            t.HasOne(x => x.FromBranch).WithMany().HasForeignKey(x => x.FromBranchId).OnDelete(DeleteBehavior.Restrict);
            t.HasOne(x => x.ToBranch).WithMany().HasForeignKey(x => x.ToBranchId).OnDelete(DeleteBehavior.Restrict);
            t.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Cascade);
            t.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
            t.HasIndex(x => x.Status);
            t.HasIndex(x => x.FromBranchId);
            t.HasIndex(x => x.ToBranchId);
        });

        model.Entity<TransferLine>(l =>
        {
            l.ToTable(tb =>
            {
                tb.HasCheckConstraint("CK_TransferLines_Quantity_Positive", "[Quantity] > 0");
                tb.HasCheckConstraint("CK_TransferLines_QuantityReceived_Range",
                    "[QuantityReceived] IS NULL OR ([QuantityReceived] >= 0 AND [QuantityReceived] <= [Quantity])");
            });
            l.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            l.HasIndex(x => new { x.TransferId, x.ProductId }).IsUnique();
        });
    }
}
