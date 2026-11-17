using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oficina.Execution.Persistence;

namespace Oficina.Execution;

public sealed class ExecutionOutboxDispatcher(ExecutionDbContext db,
    Func<ExecutionOutboxMessage, CancellationToken, Task> send,
    ILogger logger, TimeProvider? timeProvider = null)
{
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var now = clock.GetUtcNow().UtcDateTime;
        var postgres = db.Database.IsNpgsql();
        await using var transaction = postgres
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var pending = postgres
            ? await db.OutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM outbox_messages
                WHERE "PublishedAtUtc" IS NULL
                  AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= {now})
                ORDER BY "OccurredAtUtc"
                LIMIT 10 FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken)
            : await db.OutboxMessages.Where(item => item.PublishedAtUtc == null &&
                    (item.NextAttemptAtUtc == null || item.NextAttemptAtUtc <= now))
                .OrderBy(item => item.OccurredAtUtc).Take(10).ToListAsync(cancellationToken);
        foreach (var item in pending)
        {
            try
            {
                await send(item, cancellationToken);
                item.MarkPublished(clock.GetUtcNow().UtcDateTime);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                item.MarkFailed(error.Message, clock.GetUtcNow().UtcDateTime);
                logger.LogWarning(error, "Falha ao publicar evento {EventId} da Execução",
                    item.EventId);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return pending.Count;
    }
}
