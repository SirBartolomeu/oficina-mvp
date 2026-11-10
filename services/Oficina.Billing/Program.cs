using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Oficina.Billing;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
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
if (!string.IsNullOrWhiteSpace(builder.Configuration["Messaging:WorkOrdersQueueUrl"]))
{
    var sqsServiceUrl = builder.Configuration["Messaging:SqsServiceUrl"];
    builder.Services.AddSingleton<IAmazonSQS>(_ => string.IsNullOrWhiteSpace(sqsServiceUrl)
        ? new AmazonSQSClient(RegionEndpoint.GetBySystemName(region))
        : new AmazonSQSClient(new BasicAWSCredentials("local", "local"), new AmazonSQSConfig
        {
            ServiceURL = sqsServiceUrl
        }));
    builder.Services.AddHostedService<BillingSqsWorker>();
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
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
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

app.Run();

public sealed record CreateBudgetRequest(Guid WorkOrderId, int Version, string? BranchId, IReadOnlyCollection<BudgetLine> Lines);
public sealed record BudgetDecisionRequest(bool Approved);
public partial class Program;
