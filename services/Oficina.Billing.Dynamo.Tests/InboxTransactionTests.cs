using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

public sealed class InboxTransactionTests
{
    [Fact]
    public async Task OpenedEventWritesBudgetAndInboxAtomicallyAndIgnoresDuplicate()
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para executar este teste DynamoDB Local.");
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
        var table = "billing_inbox_test_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["DynamoDb:Table"] = table }).Build();
            var repository = new BillingRepository(dynamo, settings);
            var budget = Budget.Create(Guid.NewGuid(), 1, "sp-centro",
                [new BudgetLine(Guid.NewGuid(), "Revisão", 2, 1250)]);
            var eventId = Guid.NewGuid();

            var first = await repository.CreateFromEventAsync(eventId, budget, CancellationToken.None);
            var second = await repository.CreateFromEventAsync(eventId, budget, CancellationToken.None);

            Assert.Equal(first.WorkOrderId, second.WorkOrderId);
            Assert.Equal(2500, second.TotalCents);
            Assert.NotNull(await repository.GetAsync(budget.WorkOrderId, 1, CancellationToken.None));
            var inbox = await dynamo.GetItemAsync(table, new Dictionary<string, AttributeValue>
            {
                ["pk"] = new() { S = $"EVENT#{eventId:N}" },
                ["sk"] = new() { S = "RECEIVED" }
            });
            Assert.NotEmpty(inbox.Item);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateFromEventAsync(
            eventId, budget with { TotalCents = 9999 }, CancellationToken.None));
    }
}
