using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Oficina.Execution;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;

var builder = WebApplication.CreateBuilder(args);
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection é obrigatória.");
var internalToken = builder.Configuration["InternalToken"];
if (string.IsNullOrWhiteSpace(internalToken) || internalToken.Length < 32)
    throw new InvalidOperationException("InternalToken deve ter pelo menos 32 caracteres.");

builder.Services.AddDbContext<ExecutionDbContext>(options =>
{
    if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql(connectionString);
    else
        options.UseSqlite(connectionString);
});
builder.Services.AddScoped<ExecutionService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    try { await next(); }
    catch (DomainException error)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = error.Message });
    }
    catch (DbUpdateConcurrencyException)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { error = "Estoque alterado por outra operação. Tente novamente." });
    }
    catch (DbUpdateException)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { error = "Conflito de persistência. Reconsulte o estado da reserva." });
    }
});

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (ExecutionDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "Ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

var internalApi = app.MapGroup("/internal").AddEndpointFilter(async (context, next) =>
{
    var supplied = context.HttpContext.Request.Headers["X-Internal-Token"].ToString();
    var expectedBytes = Encoding.UTF8.GetBytes(internalToken);
    var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
    return suppliedBytes.Length == expectedBytes.Length &&
           CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes)
        ? await next(context)
        : Results.Unauthorized();
});

internalApi.MapPost("/catalog/quote", (QuoteRequest request, ExecutionService service, CancellationToken ct) =>
    service.QuoteAsync(request, ct));
internalApi.MapPost("/stock/reservations", (ReserveStockRequest request, ExecutionService service, CancellationToken ct) =>
    service.ReserveAsync(request, ct));
internalApi.MapPost("/stock/reservations/{id:guid}/release", async (Guid id, ExecutionService service, CancellationToken ct) =>
{
    var result = await service.ReleaseAsync(id, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});
internalApi.MapGet("/catalog/services", async (ExecutionDbContext db, CancellationToken ct) =>
    await db.RepairServices.AsNoTracking().OrderBy(item => item.Name).ToListAsync(ct));
internalApi.MapGet("/catalog/parts", async (ExecutionDbContext db, CancellationToken ct) =>
    await db.PartSupplies.AsNoTracking().OrderBy(item => item.Name).ToListAsync(ct));
internalApi.MapPost("/catalog/services", async (RepairServiceRequest request, ExecutionDbContext db, CancellationToken ct) =>
{
    var service = new RepairService(request.Name, request.Description, request.LaborPrice, request.AverageDurationMinutes);
    db.RepairServices.Add(service);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/internal/catalog/services/{service.Id}", new { service.Id });
});
internalApi.MapPost("/catalog/parts", async (PartSupplyRequest request, ExecutionDbContext db, CancellationToken ct) =>
{
    var part = new PartSupply(request.Name, request.UnitPrice, request.StockQuantity);
    db.PartSupplies.Add(part);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/internal/catalog/parts/{part.Id}", new { part.Id });
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
    if (app.Environment.IsDevelopment()) await db.Database.EnsureCreatedAsync();
    else if (app.Environment.IsProduction()) await db.Database.MigrateAsync();
}

app.Run();

public sealed record RepairServiceRequest(string Name, string Description, decimal LaborPrice, int AverageDurationMinutes);
public sealed record PartSupplyRequest(string Name, decimal UnitPrice, int StockQuantity);
public partial class Program;
