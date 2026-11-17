using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Oficina.Execution.Persistence;
using Xunit;

namespace Oficina.Execution.Tests;

public sealed class ExecutionOutboxTraceTests
{
    [Fact]
    public async Task OutboxRetainsConsumedTraceAfterPersistence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new ExecutionDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var eventId = Guid.NewGuid();
        ActivityTraceId traceId;
        using (var consumed = new Activity("SQS consume").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            traceId = consumed.TraceId;
            db.OutboxMessages.Add(new ExecutionOutboxMessage(eventId, "StockReserved", "{}", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        db.ChangeTracker.Clear();
        var persisted = await db.OutboxMessages.SingleAsync(item => item.EventId == eventId);
        using var body = JsonDocument.Parse(persisted.Body);
        var parent = body.RootElement.GetProperty("traceparent").GetString();
        Assert.True(ActivityContext.TryParse(parent, null, out var context));
        Assert.Equal(traceId, context.TraceId);
    }
}
