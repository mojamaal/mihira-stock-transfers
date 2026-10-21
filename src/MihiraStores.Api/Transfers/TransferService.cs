using MihiraStores.Api.Data;
using MihiraStores.Api.Domain;
using MihiraStores.Api.Errors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Transfers;

/// <summary>Owns every transfer rule. Nothing else changes a transfer's status or a branch's stock.</summary>
public sealed class TransferService(AppDbContext db)
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

    /// <summary>
    /// Creates a transfer in Requested status. Stock is NOT checked or reserved here: it leaves the sender at dispatch.
    /// Returns <c>Created = false</c> when the idempotency key was already used (a double-click or a retry).
    /// </summary>
    public async Task<(TransferDto Transfer, bool Created)> CreateAsync(
        CreateTransferRequest request, Guid? idempotencyKey, CancellationToken ct)
    {
        if (idempotencyKey is Guid key && await FindIdByKeyAsync(key, ct) is int existingId)
        {
            return (await GetAsync(existingId, ct), false);
        }

        var lines = await ValidateCreateAsync(request, ct);

        var transfer = new Transfer
        {
            FromBranchId = request.FromBranchId!.Value,
            ToBranchId = request.ToBranchId!.Value,
            Status = TransferStatus.Requested,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTimeOffset.UtcNow,
            Lines = lines.Select(l => new TransferLine { ProductId = l.ProductId, Quantity = l.Quantity }).ToList(),
        };
        db.Transfers.Add(transfer);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (idempotencyKey is not null && IsUniqueViolation(ex))
        {
            // Two identical requests raced past the lookup above; the unique index let only one in.
            db.ChangeTracker.Clear();
            var winnerId = await FindIdByKeyAsync(idempotencyKey.Value, ct) ?? throw new InvalidOperationException("Idempotency key conflict without a stored transfer.", ex);
            return (await GetAsync(winnerId, ct), false);
        }

        return (await GetAsync(transfer.Id, ct), true);
    }

    // How the state-changing commands stay correct under double-clicks, retries and parallel requests:
    // each one claims its status change with a single conditional UPDATE ("... WHERE Status = <required>")
    // and uses the affected row count as the answer, instead of reading the status and then writing it.
    // Only one request can win that claim; the rest see 0 rows and are resolved by ResolveUnclaimedAsync.
    // Stock is moved the same way ("... WHERE Quantity >= @qty"), inside the same transaction.
    // READ COMMITTED is enough: a conditional UPDATE takes an update lock on the row it checks, so a
    // competing request waits, then re-evaluates the WHERE clause against the committed value.

    public async Task<TransferDto> DispatchAsync(int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimed = await db.Transfers
            .Where(t => t.Id == id && t.Status == TransferStatus.Requested)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TransferStatus.Dispatched)
                .SetProperty(t => t.DispatchedAt, DateTimeOffset.UtcNow), ct);

        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            return await ResolveUnclaimedAsync(id, TransferAction.Dispatch, ct);
        }

        var transfer = await LoadForStockMovementAsync(id, ct);
        var shortLines = new List<TransferLine>();

        // Lines are processed in ProductId order so concurrent transactions always lock stock rows in the same order.
        foreach (var line in transfer.Lines)
        {
            var taken = await db.StockLevels
                .Where(s => s.BranchId == transfer.FromBranchId && s.ProductId == line.ProductId && s.Quantity >= line.Quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - line.Quantity), ct);

            if (taken == 0)
            {
                shortLines.Add(line);
            }
        }

        if (shortLines.Count > 0)
        {
            // Undo the status claim and any lines already taken: a dispatch is all or nothing.
            await tx.RollbackAsync(ct);
            throw await InsufficientStockAsync(id, transfer.FromBranchId, shortLines, ct);
        }

        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TransferDto> ReceiveAsync(int id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimed = await db.Transfers
            .Where(t => t.Id == id && t.Status == TransferStatus.Dispatched)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TransferStatus.Received)
                .SetProperty(t => t.ReceivedAt, DateTimeOffset.UtcNow), ct);

        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            return await ResolveUnclaimedAsync(id, TransferAction.Receive, ct);
        }

        var transfer = await LoadForStockMovementAsync(id, ct);

        foreach (var line in transfer.Lines)
        {
            var received = line.Quantity;

            await db.TransferLines
                .Where(l => l.Id == line.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.QuantityReceived, received), ct);

            await AddStockAsync(transfer.ToBranchId, line.ProductId, received, ct);
        }

        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Cancels a transfer that has not been dispatched. Stock never moves on cancel.</summary>
    public async Task<TransferDto> CancelAsync(int id, CancellationToken ct)
    {
        var claimed = await db.Transfers
            .Where(t => t.Id == id && t.Status == TransferStatus.Requested)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TransferStatus.Cancelled)
                .SetProperty(t => t.CancelledAt, DateTimeOffset.UtcNow), ct);

        return claimed == 0
            ? await ResolveUnclaimedAsync(id, TransferAction.Cancel, ct)
            : await GetAsync(id, ct);
    }

    /// <summary>
    /// A command claimed nothing. Either the transfer is missing (404), the same action already happened
    /// (an idempotent replay: return the transfer unchanged, move no stock), or the action is not allowed (409).
    /// </summary>
    private async Task<TransferDto> ResolveUnclaimedAsync(int id, TransferAction action, CancellationToken ct)
    {
        var status = await db.Transfers.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => (TransferStatus?)t.Status)
            .SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound($"Transfer #{id} was not found.");

        if (status == TransferWorkflow.ResultingStatus(action))
        {
            return await GetAsync(id, ct);
        }

        throw AppException.Conflict(TransferWorkflow.RejectionMessage(id, action, status));
    }

    private async Task AddStockAsync(int branchId, int productId, int quantity, CancellationToken ct)
    {
        if (quantity == 0)
        {
            return;
        }

        var updated = await db.StockLevels
            .Where(s => s.BranchId == branchId && s.ProductId == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity + quantity), ct);

        if (updated == 0)
        {
            // The seeder creates a row for every branch × product, but don't rely on that silently.
            db.StockLevels.Add(new StockLevel { BranchId = branchId, ProductId = productId, Quantity = quantity });
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<Transfer> LoadForStockMovementAsync(int id, CancellationToken ct)
    {
        var transfer = await db.Transfers.AsNoTracking()
            .Include(t => t.Lines.OrderBy(l => l.ProductId))
            .SingleAsync(t => t.Id == id, ct);
        return transfer;
    }

    private async Task<AppException> InsufficientStockAsync(
        int transferId, int branchId, List<TransferLine> shortLines, CancellationToken ct)
    {
        var productIds = shortLines.Select(l => l.ProductId).ToList();
        var branchName = await db.Branches.Where(b => b.Id == branchId).Select(b => b.Name).SingleAsync(ct);
        var available = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Sku,
                Quantity = db.StockLevels.Where(s => s.BranchId == branchId && s.ProductId == p.Id).Select(s => s.Quantity).FirstOrDefault(),
            })
            .ToDictionaryAsync(p => p.Id, ct);

        var details = shortLines.Select(l =>
        {
            var p = available[l.ProductId];
            return $"{p.Name} ({p.Sku}): {p.Quantity} available, {l.Quantity} needed";
        });

        return AppException.Conflict(
            $"Cannot dispatch transfer #{transferId}: not enough stock at {branchName}. {string.Join("; ", details)}.");
    }

    private Task<int?> FindIdByKeyAsync(Guid key, CancellationToken ct) =>
        db.Transfers.AsNoTracking()
            .Where(t => t.IdempotencyKey == key)
            .Select(t => (int?)t.Id)
            .SingleOrDefaultAsync(ct);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    private record struct ValidLine(int ProductId, int Quantity);

    private const int MaxLines = 50;
    private const int MaxQuantity = 1_000_000;

    /// <summary>Reports every input problem at once (400), then checks that referenced branches and products exist (404).</summary>
    private async Task<IReadOnlyList<ValidLine>> ValidateCreateAsync(CreateTransferRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message) =>
            (errors.TryGetValue(field, out var list) ? list : errors[field] = []).Add(message);

        if (request.FromBranchId is null) Add("fromBranchId", "Choose the branch sending the stock.");
        if (request.ToBranchId is null) Add("toBranchId", "Choose the branch receiving the stock.");
        if (request.FromBranchId is not null && request.FromBranchId == request.ToBranchId)
        {
            Add("toBranchId", "From and to branches must be different.");
        }

        var lines = request.Lines ?? [];
        if (lines.Count == 0) Add("lines", "Add at least one product.");
        if (lines.Count > MaxLines) Add("lines", $"A transfer can have at most {MaxLines} products.");

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line is null)
            {
                Add($"lines[{i}]", "This line is empty.");
                continue;
            }

            if (line.ProductId is null) Add($"lines[{i}].productId", "Choose a product.");
            if (line.Quantity is null or < 1) Add($"lines[{i}].quantity", "Quantity must be more than zero.");
            else if (line.Quantity > MaxQuantity) Add($"lines[{i}].quantity", $"Quantity cannot be more than {MaxQuantity:N0}.");
        }

        var productIds = lines.Where(l => l?.ProductId is not null).Select(l => l.ProductId!.Value).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var duplicate in productIds.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            var label = products.TryGetValue(duplicate, out var p) ? p.Sku : $"#{duplicate}";
            Add("lines", $"Product {label} appears more than once; combine the lines.");
        }

        if (errors.Count > 0)
        {
            throw AppException.Validation(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }

        var branchIds = new[] { request.FromBranchId!.Value, request.ToBranchId!.Value };
        var existingBranches = await db.Branches.Where(b => branchIds.Contains(b.Id)).Select(b => b.Id).ToListAsync(ct);
        foreach (var missing in branchIds.Except(existingBranches))
        {
            throw AppException.NotFound($"Branch #{missing} was not found.");
        }

        foreach (var missing in productIds.Except(products.Keys))
        {
            throw AppException.NotFound($"Product #{missing} was not found.");
        }

        return lines.Select(l => new ValidLine(l.ProductId!.Value, l.Quantity!.Value)).ToList();
    }

    private IQueryable<Transfer> QueryTransfers() =>
        db.Transfers.AsNoTracking()
            .Include(t => t.FromBranch)
            .Include(t => t.ToBranch)
            .Include(t => t.Lines).ThenInclude(l => l.Product)
            .AsSplitQuery();
}
