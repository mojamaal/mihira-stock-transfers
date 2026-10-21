using MihiraStores.Api.Data;
using MihiraStores.Api.Domain;
using MihiraStores.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Transfers;

/// <summary>Read side of transfers. Never changes data.</summary>
public sealed class TransferQueries(AppDbContext db)
{
    private const int MaxListSize = 200;

    public async Task<TransferDto> GetAsync(int id, CancellationToken ct)
    {
        var transfer = await QueryTransfers().SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw AppException.NotFound($"Transfer #{id} was not found.");
        return TransferDto.From(transfer);
    }

    /// <summary>Newest first. <paramref name="branchId"/> matches either the sending or the receiving branch.</summary>
    public async Task<IReadOnlyList<TransferDto>> ListAsync(int? branchId, string? status, CancellationToken ct)
    {
        var query = QueryTransfers();

        if (branchId is int branch)
        {
            query = query.Where(t => t.FromBranchId == branch || t.ToBranchId == branch);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TransferStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                throw AppException.Validation(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Unknown status '{status}'. Use one of: {string.Join(", ", Enum.GetNames<TransferStatus>())}."],
                });
            }

            query = query.Where(t => t.Status == parsed);
        }

        var transfers = await query.OrderByDescending(t => t.Id).Take(MaxListSize).ToListAsync(ct);
        return transfers.Select(TransferDto.From).ToList();
    }

    private IQueryable<Transfer> QueryTransfers() =>
        db.Transfers.AsNoTracking()
            .Include(t => t.FromBranch)
            .Include(t => t.ToBranch)
            .Include(t => t.Lines).ThenInclude(l => l.Product)
            .AsSplitQuery();
}
