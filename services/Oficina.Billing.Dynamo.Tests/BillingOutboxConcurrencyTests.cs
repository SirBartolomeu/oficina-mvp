using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Dynamo.Tests;

public sealed class BillingOutboxConcurrencyTests
{
    [Fact]
    public async Task TwoDispatchersPublishOnePendingEventOnce()
    {
        using var dynamo = CreateClient();
        var repository = await CreateRepositoryAsync(dynamo);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var firstTask = new BillingOutboxDispatcher(repository, async (_, ct) =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(ct);
                Interlocked.Increment(ref sent);
            }, NullLogger<BillingOutboxDispatcher>.Instance)
                .DispatchBatchAsync(timeout.Token);
            await started.Task.WaitAsync(timeout.Token);
            var second = new BillingOutboxDispatcher(repository, (_, _) =>
            {
                Interlocked.Increment(ref sent);
                return Task.CompletedTask;
            }, NullLogger<BillingOutboxDispatcher>.Instance);
            Assert.Equal(0, await second.DispatchBatchAsync(timeout.Token));
            Assert.Equal(0, Volatile.Read(ref sent));
            release.TrySetResult();
            Assert.Equal(1, await firstTask);
            Assert.Equal(1, Volatile.Read(ref sent));
            Assert.Empty(await repository.GetPendingOutboxAsync(timeout.Token));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task FailedSendReleasesClaimForRetry()
    {
        using var dynamo = CreateClient();
        var repository = await CreateRepositoryAsync(dynamo);
        var first = new BillingOutboxDispatcher(repository, (_, _) =>
            throw new InvalidOperationException("SQS indisponível"),
            NullLogger<BillingOutboxDispatcher>.Instance);
        Assert.Equal(0, await first.DispatchBatchAsync(CancellationToken.None));
        Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));

        var sent = 0;
        var retry = new BillingOutboxDispatcher(repository, (_, _) =>
        {
            sent++;
            return Task.CompletedTask;
        }, NullLogger<BillingOutboxDispatcher>.Instance);
        Assert.Equal(1, await retry.DispatchBatchAsync(CancellationToken.None));
        Assert.Equal(1, sent);
        Assert.Empty(await repository.GetPendingOutboxAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredClaimCanBeReclaimedButPreviousOwnerCannotDelete()
    {
        using var dynamo = CreateClient();
        var repository = await CreateRepositoryAsync(dynamo);
        var pending = Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
        var now = DateTimeOffset.UtcNow;
        var first = await repository.TryClaimOutboxAsync(pending.EventId, now,
            CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(await repository.TryClaimOutboxAsync(pending.EventId, now.AddMinutes(1),
            CancellationToken.None));
        var second = await repository.TryClaimOutboxAsync(pending.EventId, now.AddMinutes(3),
            CancellationToken.None);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        await Assert.ThrowsAsync<ConditionalCheckFailedException>(() =>
            repository.MarkClaimedOutboxPublishedAsync(pending.EventId, first!.Value,
                CancellationToken.None));
        await repository.MarkClaimedOutboxPublishedAsync(pending.EventId, second!.Value,
            CancellationToken.None);
        Assert.Empty(await repository.GetPendingOutboxAsync(CancellationToken.None));
    }

    private static AmazonDynamoDBClient CreateClient()
    {
        var endpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        return new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = endpoint });
    }

    private static async Task<BillingRepository> CreateRepositoryAsync(IAmazonDynamoDB dynamo)
    {
        var table = "billing_outbox_test_" + Guid.NewGuid().ToString("N");
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
            [new BudgetLine(Guid.NewGuid(), "Revisão", 1, 12500)]);
        await repository.CreateAsync(budget, CancellationToken.None);
        await repository.DecideAsync(budget.WorkOrderId, budget.Version, true,
            CancellationToken.None);
        return repository;
    }
}
