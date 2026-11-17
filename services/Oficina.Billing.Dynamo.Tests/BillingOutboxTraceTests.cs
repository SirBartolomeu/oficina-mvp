using System.Diagnostics;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

public sealed class BillingOutboxTraceTests
{
    [Fact]
    public async Task OutboxRetainsConsumedTraceInDynamoDb()
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
        var table = "billing_trace_test_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table, BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DynamoDb:Table"] = table }).Build();
        var repository = new BillingRepository(dynamo, settings);
        var budget = Budget.Create(Guid.NewGuid(), 1, "demo",
            [new BudgetLine(Guid.NewGuid(), "Revisão", 1, 12500)]);
        var incomingEventId = Guid.NewGuid();
        var ready = new BudgetReadyEnvelope(Guid.NewGuid(), "BudgetReady", 1,
            budget.WorkOrderId, incomingEventId, budget.WorkOrderId, budget.BranchId,
            DateTime.UtcNow, new BudgetReadyPayload(budget.Id, budget.Version,
                budget.TotalCents, "BRL"));

        ActivityTraceId traceId;
        using (var consumed = new Activity("SQS consume").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            traceId = consumed.TraceId;
            await repository.CreateFromEventAsync(incomingEventId, budget, ready, CancellationToken.None);
        }
        var persisted = Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
        using var body = JsonDocument.Parse(persisted.Body);
        var parent = body.RootElement.GetProperty("traceparent").GetString();
        Assert.True(ActivityContext.TryParse(parent, null, out var context));
        Assert.Equal(traceId, context.TraceId);
    }
}
