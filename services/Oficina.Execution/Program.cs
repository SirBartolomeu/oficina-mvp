using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Oficina.Execution;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    if (!Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var endpoint) ||
        endpoint.Scheme is not ("http" or "https"))
        throw new InvalidOperationException("Observability:OtlpEndpoint deve ser uma URL HTTP válida.");
    var sampleRatio = builder.Configuration.GetValue<double?>("Observability:TraceSampleRatio") ?? 0.1;
    if (sampleRatio is < 0 or > 1)
        throw new InvalidOperationException("Observability:TraceSampleRatio deve estar entre 0 e 1.");
    var metricIntervalSeconds = builder.Configuration.GetValue<int?>(
        "Observability:MetricExportIntervalSeconds") ?? 60;
    if (metricIntervalSeconds is < 5 or > 300)
        throw new InvalidOperationException(
            "Observability:MetricExportIntervalSeconds deve estar entre 5 e 300.");
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("oficina-execution"))
        .WithTracing(tracing => tracing
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)))
            .AddSource(SqsTraceContext.SourceName)
            .AddAspNetCoreInstrumentation(options =>
                options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options => options.Endpoint = endpoint))
        .WithMetrics(metrics => metrics
            .AddMeter(ExecutionMetrics.MeterName)
            .AddMeter("System.Runtime")
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter((options, readerOptions) =>
            {
                options.Endpoint = endpoint;
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                    metricIntervalSeconds * 1000;
            }));
}
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
builder.Services.AddScoped<ReserveStockRequestedHandler>();
builder.Services.AddScoped<ReleaseStockRequestedHandler>();
builder.Services.AddScoped<ConsumeStockRequestedHandler>();
builder.Services.AddScoped<ExecutionRequestedHandler>();
var commandsQueueUrl = builder.Configuration["Messaging:CommandsQueueUrl"];
var osEventsQueueUrl = builder.Configuration["Messaging:OsEventsQueueUrl"];
if (!string.IsNullOrWhiteSpace(commandsQueueUrl) || !string.IsNullOrWhiteSpace(osEventsQueueUrl))
{
    var sqsServiceUrl = builder.Configuration["Messaging:SqsServiceUrl"];
    var region = RegionEndpoint.GetBySystemName(builder.Configuration["AWS:Region"] ?? "us-east-1");
    builder.Services.AddSingleton<IAmazonSQS>(_ => string.IsNullOrWhiteSpace(sqsServiceUrl)
        ? new AmazonSQSClient(region)
        : new AmazonSQSClient(new BasicAWSCredentials("local", "local"), new AmazonSQSConfig
        {
            ServiceURL = sqsServiceUrl
        }));
    if (!string.IsNullOrWhiteSpace(commandsQueueUrl))
        builder.Services.AddHostedService<ExecutionCommandsWorker>();
    if (!string.IsNullOrWhiteSpace(osEventsQueueUrl))
        builder.Services.AddHostedService<ExecutionOutboxWorker>();
}
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
internalApi.MapGet("/executions", async (ExecutionDbContext db, CancellationToken ct) =>
    await db.StockReservations.AsNoTracking()
        .Where(item => item.RepairStatus == RepairStatus.Started)
        .OrderBy(item => item.RepairStartedAtUtc)
        .Select(item => new { item.WorkOrderId, item.Id, item.RepairStartedAtUtc })
        .ToListAsync(ct));
internalApi.MapPost("/executions/{reservationId:guid}/finish", async (
    Guid reservationId, ExecutionService service, CancellationToken ct) =>
{
    var finished = await service.FinishRepairAsync(reservationId, ct);
    return finished is null ? Results.NotFound() : Results.Ok(new
    {
        status = "Finished", published = finished.Value
    });
});
internalApi.MapPost("/executions/{reservationId:guid}/reject", async (
    Guid reservationId, RejectRepairRequest request, ExecutionService service,
    CancellationToken ct) =>
{
    var rejected = await service.RejectRepairAsync(reservationId, request.Reason, ct);
    return rejected is null ? Results.NotFound() : Results.Ok(new
    {
        status = "Rejected", published = rejected.Value
    });
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
public sealed record RejectRepairRequest(string Reason);
public partial class Program;
