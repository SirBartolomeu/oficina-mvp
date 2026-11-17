using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class OutboxDispatcher(
    WorkshopDbContext dbContext,
    IIntegrationMessageSender sender,
    ILogger<OutboxDispatcher> logger,
    TimeProvider? timeProvider = null)
{
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var postgres = dbContext.Database.IsNpgsql();
        await using var transaction = postgres
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var pending = postgres
            ? await dbContext.OutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM outbox_messages
                WHERE "PublishedAtUtc" IS NULL
                  AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= {now})
                ORDER BY "OccurredAtUtc"
                LIMIT 10 FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken)
            : await dbContext.OutboxMessages
                .Where(item => item.PublishedAtUtc == null &&
                    (item.NextAttemptAtUtc == null || item.NextAttemptAtUtc <= now))
                .OrderBy(item => item.OccurredAtUtc)
                .Take(10)
                .ToListAsync(cancellationToken);
        foreach (var item in pending)
        {
            try
            {
                await sender.SendAsync(item.EventId, item.EventType, item.PayloadJson, cancellationToken);
                item.MarkPublished((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                item.MarkFailed(error.Message,
                    (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);
                logger.LogWarning(error, "Falha ao publicar evento {EventId} da Outbox", item.EventId);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return pending.Count;
    }
}
