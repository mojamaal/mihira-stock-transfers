using MihiraStores.Api.Data;
using MihiraStores.Api.Domain;
using MihiraStores.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Transfers;

/// <summary>Which side of a transfer a branch filter matches.</summary>
public enum TransferDirection
{
    /// <summary>The branch is the sender or the receiver.</summary>
    Any,

    /// <summary>The branch is the sender.</summary>
    Outgoing,

    /// <summary>The branch is the receiver.</summary>
    Incoming,
}

public sealed record TransferPage(
    IReadOnlyList<TransferDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    /// <summary>How many transfers are in each status under the branch/direction filter (ignoring the status filter).</summary>
    IReadOnlyDictionary<TransferStatus, int> StatusCounts);

/// <summary>Read side of transfers. Never changes data.</summary>
public sealed class TransferQueries(AppDbContext db)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<TransferDto> GetAsync(int id, CancellationToken ct)
    {
        var transfer = await QueryTransfers().SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw AppException.NotFound($"Transfer #{id} was not found.");
        return TransferDto.From(transfer);
    }

    /// <summary>Newest first, one page at a time, with per-status counts so a client can show its work queue.</summary>
    public async Task<TransferPage> ListAsync(
        int? branchId, string? direction, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var dir = Parse<TransferDirection>("direction", direction, errors) ?? TransferDirection.Any;
        var statusFilter = Parse<TransferStatus>("status", status, errors);
        var pageNumber = page ?? 1;
        var size = pageSize ?? DefaultPageSize;

        if (dir != TransferDirection.Any && branchId is null)
        {
            errors["direction"] = ["Choose a branch to filter by direction."];
        }

        if (pageNumber < 1) errors["page"] = ["Page must be 1 or more."];
        if (size is < 1 or > MaxPageSize) errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];
        if (errors.Count > 0) throw AppException.Validation(errors);

        IQueryable<Transfer> scoped = db.Transfers.AsNoTracking();
        if (branchId is int branch)
        {
            scoped = dir switch
            {
                TransferDirection.Outgoing => scoped.Where(t => t.FromBranchId == branch),
                TransferDirection.Incoming => scoped.Where(t => t.ToBranchId == branch),
                _ => scoped.Where(t => t.FromBranchId == branch || t.ToBranchId == branch),
            };
        }

        var counts = await scoped
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var statusCounts = Enum.GetValues<TransferStatus>().ToDictionary(s => s, s => counts.GetValueOrDefault(s));

        var filtered = statusFilter is TransferStatus only ? scoped.Where(t => t.Status == only) : scoped;
        var total = statusFilter is TransferStatus selected ? statusCounts[selected] : statusCounts.Values.Sum();

        var ids = await filtered
            .OrderByDescending(t => t.Id)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .Select(t => t.Id)
            .ToListAsync(ct);
        var transfers = await QueryTransfers().Where(t => ids.Contains(t.Id)).OrderByDescending(t => t.Id).ToListAsync(ct);

        return new TransferPage(
            transfers.Select(TransferDto.From).ToList(),
            pageNumber,
            size,
            total,
            TotalPages: Math.Max(1, (int)Math.Ceiling(total / (double)size)),
            statusCounts);
    }

    private static T? Parse<T>(string field, string? value, Dictionary<string, string[]> errors) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) return parsed;

        errors[field] = [$"Unknown {field} '{value}'. Use one of: {string.Join(", ", Enum.GetNames<T>())}."];
        return null;
    }

    private IQueryable<Transfer> QueryTransfers() =>
        db.Transfers.AsNoTracking()
            .Include(t => t.FromBranch)
            .Include(t => t.ToBranch)
            .Include(t => t.Lines).ThenInclude(l => l.Product)
            .AsSplitQuery();
}
