using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Oficina.Billing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        .ConfigureResource(resource => resource.AddService("oficina-billing"))
        .WithTracing(tracing => tracing
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)))
            .AddSource(SqsTraceContext.SourceName)
            .AddAspNetCoreInstrumentation(options =>
                options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options => options.Endpoint = endpoint))
        .WithMetrics(metrics => metrics
            .AddMeter(BillingMetrics.MeterName)
            .AddMeter("System.Runtime")
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter((options, readerOptions) =>
            {
                options.Endpoint = endpoint;
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                    metricIntervalSeconds * 1000;
            }));
}
var internalToken = builder.Configuration["InternalToken"];
if (string.IsNullOrWhiteSpace(internalToken) || internalToken.Length < 32)
    throw new InvalidOperationException("InternalToken deve ter pelo menos 32 caracteres.");
var table = builder.Configuration["DynamoDb:Table"]
    ?? throw new InvalidOperationException("DynamoDb:Table é obrigatório.");
var serviceUrl = builder.Configuration["DynamoDb:ServiceUrl"];
var region = builder.Configuration["AWS:Region"] ?? "us-east-1";

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => string.IsNullOrWhiteSpace(serviceUrl)
    ? new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(region))
    : new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"), new AmazonDynamoDBConfig
    {
        ServiceURL = serviceUrl
    }));
builder.Services.AddScoped<BillingRepository>();
builder.Services.AddScoped<IBudgetEventStore>(provider => provider.GetRequiredService<BillingRepository>());
builder.Services.AddScoped<WorkOrderOpenedHandler>();
builder.Services.AddScoped<PaymentRequestHandler>();
builder.Services.AddScoped<RefundRequestHandler>();
var mercadoPagoToken = builder.Configuration["MercadoPago:AccessToken"];
var mercadoPagoPos = builder.Configuration["MercadoPago:ExternalPosId"];
var mercadoPagoWebhookSecret = builder.Configuration["MercadoPago:WebhookSecret"];
var checkoutPro = builder.Configuration["MercadoPago:Mode"] == "CheckoutPro";
var testMode = builder.Configuration.GetValue<bool>("MercadoPago:TestMode");
if (checkoutPro && (!testMode || builder.Environment.IsProduction()))
    throw new InvalidOperationException("Checkout Pro disponível somente para testes, fora de Production.");
if (!string.IsNullOrWhiteSpace(mercadoPagoToken) || !string.IsNullOrWhiteSpace(mercadoPagoPos))
{
    if (string.IsNullOrWhiteSpace(mercadoPagoToken) || !checkoutPro && string.IsNullOrWhiteSpace(mercadoPagoPos))
        throw new InvalidOperationException("MercadoPago:AccessToken e ExternalPosId devem ser configurados juntos.");
    builder.Services.AddHttpClient("mercadopago", client =>
    {
        client.BaseAddress = new Uri("https://api.mercadopago.com/");
        client.Timeout = TimeSpan.FromSeconds(15);
    });
    builder.Services.AddScoped(provider => new MercadoPagoQrClient(
        provider.GetRequiredService<IHttpClientFactory>().CreateClient("mercadopago"),
        new MercadoPagoQrOptions(mercadoPagoToken, mercadoPagoPos ?? "")
        {
            CheckoutPro = checkoutPro, TestMode = testMode,
            ReturnUrl = builder.Configuration["MercadoPago:ReturnUrl"]
        }));
    builder.Services.AddScoped<PaymentOrderProcessor>();
    builder.Services.AddScoped<PaymentReconciliationService>();
    builder.Services.AddHostedService<PaymentOrderWorker>();
    builder.Services.AddScoped<OrderNotificationQueue>();
    builder.Services.AddHostedService<OrderNotificationWorker>();
    builder.Services.AddScoped<RefundOrderProcessor>();
    if (builder.Configuration.GetValue<bool>("MercadoPago:EnableRefundWorker"))
        builder.Services.AddHostedService<RefundOrderWorker>();
}
var workOrdersQueueUrl = builder.Configuration["Messaging:WorkOrdersQueueUrl"];
var osEventsQueueUrl = builder.Configuration["Messaging:OsEventsQueueUrl"];
if (!string.IsNullOrWhiteSpace(workOrdersQueueUrl) || !string.IsNullOrWhiteSpace(osEventsQueueUrl))
{
    var sqsServiceUrl = builder.Configuration["Messaging:SqsServiceUrl"];
    builder.Services.AddSingleton<IAmazonSQS>(_ => string.IsNullOrWhiteSpace(sqsServiceUrl)
        ? new AmazonSQSClient(RegionEndpoint.GetBySystemName(region))
        : new AmazonSQSClient(new BasicAWSCredentials("local", "local"), new AmazonSQSConfig
        {
            ServiceURL = sqsServiceUrl
        }));
    if (!string.IsNullOrWhiteSpace(workOrdersQueueUrl))
        builder.Services.AddHostedService<BillingSqsWorker>();
    if (!string.IsNullOrWhiteSpace(osEventsQueueUrl))
        builder.Services.AddHostedService<BillingOutboxWorker>();
}
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    using var scope = app.Services.CreateScope();
    var dynamo = scope.ServiceProvider.GetRequiredService<IAmazonDynamoDB>();
    var tables = await dynamo.ListTablesAsync();
    if (!tables.TableNames.Contains(table))
    {
        try
        {
            await dynamo.CreateTableAsync(new CreateTableRequest
            {
                TableName = table,
                BillingMode = BillingMode.PAY_PER_REQUEST,
                AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
                KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
            });
        }
        catch (ResourceInUseException) { /* Outra instância criou a tabela. */ }
    }
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (IAmazonDynamoDB dynamo, CancellationToken ct) =>
{
    try
    {
        await dynamo.DescribeTableAsync(table, ct);
        return Results.Ok(new { status = "Ready" });
    }
    catch (ResourceNotFoundException) { return Results.StatusCode(503); }
});

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

internalApi.MapPost("/budgets", async (CreateBudgetRequest request, BillingRepository repository, CancellationToken ct) =>
{
    try
    {
        var budget = Budget.Create(request.WorkOrderId, request.Version, request.BranchId, request.Lines);
        return Results.Ok(await repository.CreateAsync(budget, ct));
    }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
});
internalApi.MapGet("/budgets/{workOrderId:guid}/{version:int}", async (Guid workOrderId, int version, BillingRepository repository, CancellationToken ct) =>
{
    var budget = await repository.GetAsync(workOrderId, version, ct);
    return budget is null ? Results.NotFound() : Results.Ok(budget);
});
internalApi.MapPost("/budgets/{workOrderId:guid}/{version:int}/decision", async (Guid workOrderId, int version, BudgetDecisionRequest request, BillingRepository repository, CancellationToken ct) =>
{
    try
    {
        var budget = await repository.DecideAsync(workOrderId, version, request.Approved, ct);
        return budget is null ? Results.NotFound() : Results.Ok(budget);
    }
    catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
});
internalApi.MapGet("/payments/{paymentAttemptId:guid}", async (Guid paymentAttemptId,
    BillingRepository repository, CancellationToken ct) =>
{
    var payment = await repository.GetPaymentAttemptAsync(paymentAttemptId, ct);
    return payment is null ? Results.NotFound() : Results.Ok(payment);
});
if (!string.IsNullOrWhiteSpace(mercadoPagoToken))
{
    internalApi.MapPost("/payments/{paymentAttemptId:guid}/reconcile", async (
        Guid paymentAttemptId, BillingRepository repository,
        PaymentReconciliationService reconciliation, CancellationToken ct) =>
    {
        var attempt = await repository.GetPaymentAttemptAsync(paymentAttemptId, ct);
        if (attempt is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(attempt.ProviderOrderId)) return Results.Conflict();
        try
        {
            await reconciliation.ReconcileAsync(attempt.ProviderOrderId, ct);
            return Results.Ok(await repository.GetPaymentAttemptAsync(paymentAttemptId, ct));
        }
        catch (HttpRequestException) { return Results.StatusCode(503); }
        catch (InvalidOperationException) { return Results.Conflict(); }
    });
    internalApi.MapPost("/payments/{paymentAttemptId:guid}/reconcile-refund", async (
        Guid paymentAttemptId, BillingRepository repository,
        PaymentReconciliationService reconciliation, CancellationToken ct) =>
    {
        var attempt = await repository.GetPaymentAttemptAsync(paymentAttemptId, ct);
        if (attempt is null) return Results.NotFound();
        if (attempt.Status == "Refunded") return Results.Ok(new { attempt.Status, Reconciled = false });
        if (attempt.Status is not "RefundRequested" and not "RefundSubmitting" ||
            string.IsNullOrWhiteSpace(attempt.ProviderOrderId)) return Results.Conflict();
        try
        {
            var reconciled = await reconciliation.ReconcileAsync(attempt.ProviderOrderId, ct);
            var current = await repository.GetPaymentAttemptAsync(paymentAttemptId, ct);
            return Results.Ok(new { Status = current!.Status, Reconciled = reconciled });
        }
        catch (HttpRequestException) { return Results.StatusCode(503); }
        catch (InvalidOperationException) { return Results.Conflict(); }
    });
}
if (!string.IsNullOrWhiteSpace(mercadoPagoToken) &&
    !string.IsNullOrWhiteSpace(mercadoPagoWebhookSecret))
{
    app.MapPost("/webhooks/mercado-pago/orders", async (HttpRequest request,
        OrderNotificationQueue notifications, CancellationToken ct) =>
    {
        var orderId = request.Query["data.id"].ToString();
        if (!MercadoPagoWebhookSignature.IsValid(request.Headers["x-signature"],
            request.Headers["x-request-id"], orderId, mercadoPagoWebhookSecret))
            return Results.Unauthorized();
        if (orderId.Length > 64 || !orderId.StartsWith("ORD", StringComparison.Ordinal) ||
            orderId.Any(ch => !char.IsLetterOrDigit(ch))) return Results.BadRequest();
        if (checkoutPro && !orderId.StartsWith("ORDTST", StringComparison.Ordinal))
            return Results.BadRequest();
        JsonDocument notification;
        try { notification = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct); }
        catch (JsonException) { return Results.BadRequest(); }
        using (notification)
        {
        var root = notification.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "order" ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != orderId)
            return Results.BadRequest();
        if (checkoutPro && (!root.TryGetProperty("live_mode", out var live) || live.ValueKind != JsonValueKind.False))
            return Results.BadRequest();
        await notifications.EnqueueAsync(orderId, ct);
        return Results.Ok();
        }
    });
}

if (app.Environment.IsDevelopment() && checkoutPro)
{
    app.MapGet("/dev/checkout", () => Results.Content(CheckoutTestPage.Html, "text/html; charset=utf-8"));
}
app.MapGet("/checkout/return", () => Results.Content(
    "Retorno recebido. O resultado do pagamento será confirmado pelo servidor após consulta ao Mercado Pago.", "text/plain; charset=utf-8"));

app.Run();

public sealed record CreateBudgetRequest(Guid WorkOrderId, int Version, string? BranchId, IReadOnlyCollection<BudgetLine> Lines);
public sealed record BudgetDecisionRequest(bool Approved);
public partial class Program;
