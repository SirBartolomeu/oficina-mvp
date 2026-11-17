using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Execution;
using Oficina.Execution.Persistence;
using Xunit;

namespace Oficina.Execution.Postgres.Tests;

public sealed class ExecutionOutboxConcurrencyTests
{
    public async Task TwoDispatchersSelectOneOutboxMessageOnlyOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "EXECUTION_OUTBOX_POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Defina EXECUTION_OUTBOX_POSTGRES_CONNECTION_STRING para schema isolado e migrado.");
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseNpgsql(connectionString).Options;
        var eventId = Guid.NewGuid();
        await using (var setup = new ExecutionDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.OutboxMessages.Add(new ExecutionOutboxMessage(eventId,
                "StockReserved", "{}", DateTime.UtcNow));
            await setup.SaveChangesAsync();
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = 0;
        try
        {
            await using var first = new ExecutionDbContext(options);
            await using var second = new ExecutionDbContext(options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var firstTask = new ExecutionOutboxDispatcher(first, async (_, ct) =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(ct);
                Interlocked.Increment(ref sent);
            }, NullLogger<ExecutionOutboxDispatcher>.Instance)
                .DispatchBatchAsync(timeout.Token);
            await started.Task.WaitAsync(timeout.Token);
            var secondTask = new ExecutionOutboxDispatcher(second, (_, _) =>
            {
                Interlocked.Increment(ref sent);
                return Task.CompletedTask;
            }, NullLogger<ExecutionOutboxDispatcher>.Instance)
                .DispatchBatchAsync(timeout.Token);
            Assert.Equal(0, await secondTask);
            Assert.Equal(0, Volatile.Read(ref sent));
            release.TrySetResult();
            Assert.Equal(1, await firstTask);
            Assert.Equal(1, Volatile.Read(ref sent));
            await using var verify = new ExecutionDbContext(options);
            var persisted = await verify.OutboxMessages.SingleAsync(item => item.EventId == eventId);
            Assert.NotNull(persisted.PublishedAtUtc);
            Assert.Equal(1, persisted.PublishAttempts);
        }
        finally
        {
            release.TrySetResult();
            await using var cleanup = new ExecutionDbContext(options);
            await cleanup.OutboxMessages.Where(item => item.EventId == eventId)
                .ExecuteDeleteAsync();
        }
    }
}
