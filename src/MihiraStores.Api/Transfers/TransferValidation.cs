using MihiraStores.Api.Data;
using MihiraStores.Api.Domain;
using MihiraStores.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace MihiraStores.Api.Transfers;

public readonly record struct ValidLine(int ProductId, int Quantity);

public static class TransferValidation
{
    public const int MaxLines = 50;
    public const int MaxQuantity = 1_000_000;

    /// <summary>Reports every input problem at once (400), then checks that referenced branches and products exist (404).</summary>
    public static async Task<IReadOnlyList<ValidLine>> ValidateCreateAsync(AppDbContext db, CreateTransferRequest request, CancellationToken ct)
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

    /// <summary>Checks a partial-receipt body against what was sent. Products left out count as fully received.</summary>
    public static Dictionary<int, int> ValidateReceive(Transfer transfer, ReceiveTransferRequest? request)
    {
        var result = new Dictionary<int, int>();
        var errors = new Dictionary<string, string[]>();
        var lines = request?.Lines ?? [];

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var sent = transfer.Lines.FirstOrDefault(l => l.ProductId == line?.ProductId);

            if (line?.ProductId is null || sent is null)
            {
                errors[$"lines[{i}].productId"] = ["This product is not part of the transfer."];
            }
            else if (result.ContainsKey(sent.ProductId))
            {
                errors[$"lines[{i}].productId"] = ["Each product can be listed only once."];
            }
            else if (line.QuantityReceived is not int received || received < 0 || received > sent.Quantity)
            {
                errors[$"lines[{i}].quantityReceived"] = [$"Received quantity must be between 0 and the {sent.Quantity} sent."];
            }
            else
            {
                result[sent.ProductId] = received;
            }
        }

        return errors.Count > 0 ? throw AppException.Validation(errors) : result;
    }
}
