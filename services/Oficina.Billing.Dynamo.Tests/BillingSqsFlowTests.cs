using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

public sealed class BillingSqsFlowTests
{
    [Fact]
    public async Task OpenedOrderIsConsumedAndBudgetReadyIsPublished()
    {
        var dynamoEndpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT.");
        var sqsEndpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT.");
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = dynamoEndpoint });
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = sqsEndpoint });
        var suffix = Guid.NewGuid().ToString("N");
        var table = "billing_sqs_flow_" + suffix;
        var inboundUrl = (await sqs.CreateQueueAsync("billing-in-" + suffix)).QueueUrl;
        var outboundUrl = (await sqs.CreateQueueAsync("billing-out-" + suffix)).QueueUrl;
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table, BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DynamoDb:Table"] = table,
                ["Messaging:WorkOrdersQueueUrl"] = inboundUrl,
                ["Messaging:OsEventsQueueUrl"] = outboundUrl
            }).Build();
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(settings)
            .AddSingleton<IAmazonDynamoDB>(dynamo)
            .AddScoped<BillingRepository>()
            .AddScoped<IBudgetEventStore>(provider => provider.GetRequiredService<BillingRepository>())
            .AddScoped<WorkOrderOpenedHandler>()
            .BuildServiceProvider();
        var repository = new BillingRepository(dynamo, settings);
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = new WorkOrderOpenedEnvelope(eventId, "WorkOrderOpened", 1,
            orderId, Guid.NewGuid(), orderId, "sp-centro", DateTime.UtcNow,
            new WorkOrderOpenedPayload(1,
                [new WorkOrderOpenedLine("Service", Guid.NewGuid(), "Revisão", 1, 12500)],
                12500, "BRL", DateTime.UtcNow));
        await sqs.SendMessageAsync(inboundUrl, JsonSerializer.Serialize(envelope,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        using var inbound = new BillingSqsWorker(sqs,
            services.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<BillingSqsWorker>.Instance);
        using var outbound = new BillingOutboxWorker(sqs,
            services.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<BillingOutboxWorker>.Instance);
        await inbound.StartAsync(CancellationToken.None);
        await outbound.StartAsync(CancellationToken.None);
        try
        {
            Budget? budget = null;
            Message? ready = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!timeout.IsCancellationRequested && ready is null)
            {
                budget ??= await repository.GetAsync(orderId, 1, timeout.Token);
                if (budget is not null)
                {
                    var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                    {
                        QueueUrl = outboundUrl, WaitTimeSeconds = 1
                    }, timeout.Token);
                    ready = response.Messages?.FirstOrDefault();
                }
                if (ready is null) await Task.Delay(100, timeout.Token);
            }
            Assert.NotNull(budget);
            Assert.Equal(12500, budget.TotalCents);
            Assert.NotNull(ready);
            using var body = JsonDocument.Parse(ready.Body);
            Assert.Equal("BudgetReady", body.RootElement.GetProperty("eventType").GetString());
            Assert.Equal(orderId.ToString("D"),
                body.RootElement.GetProperty("workOrderId").GetString());
            IReadOnlyCollection<BillingOutboxMessage> pending = [];
            for (var attempt = 0; attempt < 50; attempt++)
            {
                pending = await repository.GetPendingOutboxAsync(CancellationToken.None);
                if (pending.Count == 0) break;
                await Task.Delay(100);
            }
            Assert.Empty(pending);
        }
        finally
        {
            await inbound.StopAsync(CancellationToken.None);
            await outbound.StopAsync(CancellationToken.None);
        }
    }
}
