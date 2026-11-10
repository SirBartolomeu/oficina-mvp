using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Billing;
using OsEvents = OficinaMvp.Application.Contracts;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class OpenedEventFlowTests
{
    [Fact]
    public async Task OutboxToSqsToBillingCreatesOneBudgetForDuplicateDelivery()
    {
        var sqsEndpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        var dynamoEndpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = sqsEndpoint });
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = dynamoEndpoint });
        var queueUrl = (await sqs.CreateQueueAsync("billing-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var table = "billing_flow_test_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });

        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = new OsEvents.IntegrationEnvelope<OsEvents.WorkOrderOpenedPayload>(eventId, "WorkOrderOpened", 1,
            orderId, Guid.NewGuid(), orderId, "sp-centro", DateTime.UtcNow,
            new OsEvents.WorkOrderOpenedPayload(1,
                [new OsEvents.WorkOrderOpenedLine("Service", Guid.NewGuid(), "Revisão", 1, 12500)],
                12500, "BRL", DateTime.UtcNow));
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var sqlite = new SqliteConnection("Data Source=:memory:");
        await sqlite.OpenAsync();
        await using var db = new WorkshopDbContext(
            new DbContextOptionsBuilder<WorkshopDbContext>().UseSqlite(sqlite).Options);
        await db.Database.EnsureCreatedAsync();
        db.OutboxMessages.Add(new OutboxMessage(eventId, "WorkOrderOpened", json, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var dispatcher = new OutboxDispatcher(db, new SqsIntegrationMessageSender(sqs, queueUrl),
            NullLogger<OutboxDispatcher>.Instance);
        Assert.Equal(1, await dispatcher.DispatchBatchAsync(CancellationToken.None));
        Assert.NotNull((await db.OutboxMessages.SingleAsync()).PublishedAtUtc);
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DynamoDb:Table"] = table,
                ["Messaging:WorkOrdersQueueUrl"] = queueUrl
            }).Build();
        var repository = new BillingRepository(dynamo, settings);
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(settings)
            .AddSingleton<IAmazonDynamoDB>(dynamo)
            .AddScoped<BillingRepository>()
            .AddScoped<IBudgetEventStore>(provider => provider.GetRequiredService<BillingRepository>())
            .AddScoped<WorkOrderOpenedHandler>()
            .BuildServiceProvider();
        using var worker = new BillingSqsWorker(sqs, services.GetRequiredService<IServiceScopeFactory>(),
            settings, NullLogger<BillingSqsWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        Budget? budget = null;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            while (budget is null && !timeout.IsCancellationRequested)
            {
                budget = await repository.GetAsync(orderId, 1, CancellationToken.None);
                if (budget is null) await Task.Delay(100, timeout.Token);
            }
        }
        await worker.StopAsync(CancellationToken.None);
        Assert.NotNull(budget);
        var duplicate = await new WorkOrderOpenedHandler(repository).HandleAsync(json, CancellationToken.None);

        Assert.Equal(12500, budget.TotalCents);
        Assert.Equal(budget.WorkOrderId, duplicate.WorkOrderId);
        Assert.NotNull(await repository.GetAsync(orderId, 1, CancellationToken.None));
    }
}
