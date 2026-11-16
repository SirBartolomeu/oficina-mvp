using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;
using Xunit;

namespace OficinaMvp.Postgres.Tests;

public sealed class OutboxConcurrencyTests
{
    public async Task TwoDispatchersDoNotPublishTheSamePendingMessage()
    {
        var connectionString = Environment.GetEnvironmentVariable("OS_POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Defina OS_POSTGRES_CONNECTION_STRING para schema PostgreSQL isolado e migrado.");
        var options = new DbContextOptionsBuilder<WorkshopDbContext>()
            .UseNpgsql(connectionString).Options;
        var eventId = Guid.NewGuid();
        await using (var setup = new WorkshopDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.OutboxMessages.Add(new OutboxMessage(eventId, "WorkOrderOpened",
                "{}", DateTime.UtcNow));
            await setup.SaveChangesAsync();
        }

        var blockingSender = new BlockingSender();
        try
        {
            await using var first = new WorkshopDbContext(options);
            await using var second = new WorkshopDbContext(options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var firstDispatch = new OutboxDispatcher(first, blockingSender,
                NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(timeout.Token);
            await blockingSender.Started.WaitAsync(timeout.Token);
            var competingSender = new RecordingSender();
            var secondCount = await new OutboxDispatcher(second, competingSender,
                NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(timeout.Token);
            Assert.Equal(0, secondCount);
            Assert.Equal(0, competingSender.Count);
            blockingSender.Release();
            Assert.Equal(1, await firstDispatch);
            Assert.Equal(1, blockingSender.Count);

            await using var verify = new WorkshopDbContext(options);
            var persisted = await verify.OutboxMessages.SingleAsync(item => item.EventId == eventId);
            Assert.NotNull(persisted.PublishedAtUtc);
            Assert.Equal(1, persisted.PublishAttempts);
        }
        finally
        {
            blockingSender.Release();
            await using var cleanup = new WorkshopDbContext(options);
            await cleanup.OutboxMessages.Where(item => item.EventId == eventId).ExecuteDeleteAsync();
        }
    }

    private sealed class BlockingSender : IIntegrationMessageSender
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => _started.Task;
        public int Count { get; private set; }
        public void Release() => _release.TrySetResult();

        public async Task SendAsync(Guid eventId, string eventType, string payloadJson,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            Count++;
        }
    }

    private sealed class RecordingSender : IIntegrationMessageSender
    {
        public int Count { get; private set; }
        public Task SendAsync(Guid eventId, string eventType, string payloadJson,
            CancellationToken cancellationToken)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
