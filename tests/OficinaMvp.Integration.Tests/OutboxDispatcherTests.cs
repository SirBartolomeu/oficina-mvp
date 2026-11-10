using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Integration.Tests;

public sealed class OutboxDispatcherTests
{
    [Fact]
    public async Task SuccessfulSendMarksMessagePublished()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var eventId = Guid.NewGuid();
        db.OutboxMessages.Add(new OutboxMessage(eventId, "WorkOrderOpened", "{}", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var sender = new RecordingSender();

        var dispatched = await new OutboxDispatcher(db, sender,
            NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(CancellationToken.None);

        Assert.Equal(1, dispatched);
        Assert.Equal(eventId, Assert.Single(sender.Sent));
        Assert.NotNull((await db.OutboxMessages.SingleAsync()).PublishedAtUtc);
        Assert.Equal(1, (await db.OutboxMessages.SingleAsync()).PublishAttempts);
    }

    [Fact]
    public async Task FailedSendRemainsPendingForRetry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), "WorkOrderOpened", "{}", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var sender = new RecordingSender { Fail = true };
        var dispatcher = new OutboxDispatcher(db, sender, NullLogger<OutboxDispatcher>.Instance);

        await dispatcher.DispatchBatchAsync(CancellationToken.None);
        Assert.Null((await db.OutboxMessages.SingleAsync()).PublishedAtUtc);
        Assert.Equal(1, (await db.OutboxMessages.SingleAsync()).PublishAttempts);
        sender.Fail = false;
        await dispatcher.DispatchBatchAsync(CancellationToken.None);
        Assert.NotNull((await db.OutboxMessages.SingleAsync()).PublishedAtUtc);
        Assert.Equal(2, (await db.OutboxMessages.SingleAsync()).PublishAttempts);
    }

    private static WorkshopDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<WorkshopDbContext>().UseSqlite(connection).Options);

    private sealed class RecordingSender : IIntegrationMessageSender
    {
        public bool Fail { get; set; }
        public List<Guid> Sent { get; } = [];
        public Task SendAsync(Guid eventId, string eventType, string payloadJson,
            CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("queue unavailable");
            Sent.Add(eventId);
            return Task.CompletedTask;
        }
    }
}
