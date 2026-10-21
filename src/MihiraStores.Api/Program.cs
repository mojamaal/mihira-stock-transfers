using System.Text.Json.Serialization;
using MihiraStores.Api.Branches;
using MihiraStores.Api.Data;
using MihiraStores.Api.Errors;
using MihiraStores.Api.Live;
using MihiraStores.Api.Products;
using MihiraStores.Api.Stock;
using MihiraStores.Api.Transfers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Default' is not configured.");

// Transient SQL failures are retried. Commands run their transactions through EF's execution strategy
// (see TransferService.InTransactionAsync), and are idempotent, so a retry can never move stock twice.
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString, sql =>
    sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorNumbersToAdd: null)));
builder.Services.AddScoped<TransferService>();
builder.Services.AddScoped<TransferQueries>();
builder.Services.AddScoped<StockMover>();
builder.Services.AddSingleton<TransferEvents>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Plain HTTP in Development keeps local setup free of dev-certificate trust issues.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapBranchEndpoints();
app.MapProductEndpoints();
app.MapTransferEndpoints();
app.MapLiveEndpoints();

// Apply migrations and seed on startup so the app runs with no manual database steps.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.Run();

public partial class Program;
